// 帳號流程：註冊 → 信箱驗證 → 登入 → 改名稱 → 改密碼 → 忘記密碼 → 重設密碼 → 登出；商家也用同一個登入
// 寄信在測試環境一律失敗（ApiFactory 把 SMTP 指到不存在的位址），驗證碼、重設碼從資料庫讀
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using backend.Models;
using Allure.Net.Commons.Attributes;

namespace TrafficSystem.Tests.Api;

[Collection(ApiCollection.Name)]
[AllureSuiteHierarchy("帳號與身分驗證", "API 整合測試")]
[AllureBddHierarchy("帳號與身分驗證", "API 整合測試")]
public class AuthFlowApiTests
{
    private readonly ApiFactory _api;

    public AuthFlowApiTests(ApiFactory api) => _api = api;

    private static (string name, string email) NewAccount()
    {
        string id = Guid.NewGuid().ToString("N")[..10];
        return ($"探員{id}", $"agent{id}@example.com");
    }

    private async Task<ApiResult<LoginResponse>> LoginAsync(string account, string password) =>
        await (await _api.CreateClient().PostAsJsonAsync("/api/Auth/Login", new { auth_name = account, auth_pswd = password }))
            .ReadResultAsync<LoginResponse>();

    private HttpClient WithToken(string token)
    {
        HttpClient client = _api.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task RegisterAndVerifyAsync(string name, string email, string password)
    {
        HttpResponseMessage register = await _api.CreateClient().PostAsJsonAsync("/api/Auth/Register",
            new { Username = name, Email = email, Password = password, Birthday = "2005-01-24", Gender = "Female", AccountType = 1 });
        Assert.True((await register.ReadResultAsync()).isSuccess);

        string token = await _api.QueryAsync<string>("SELECT au_email_token FROM auth WHERE auth_email = @email;", new { email });
        HttpResponseMessage verify = await _api.CreateClient().GetAsync($"/api/Auth/VerifyEmail?token={token}");
        Assert.Equal(HttpStatusCode.OK, verify.StatusCode);
    }

    [Fact]
    public async Task 註冊後要先驗證信箱才能登入()
    {
        var (name, email) = NewAccount();
        await _api.CreateClient().PostAsJsonAsync("/api/Auth/Register",
            new { Username = name, Email = email, Password = "Pass1234!", AccountType = 1 });

        ApiResult<LoginResponse> before = await LoginAsync(email, "Pass1234!");
        Assert.False(before.isSuccess);
        Assert.Contains("驗證", before.message);

        // 驗證連結 24 小時內有效（用資料庫時間算，不受時區影響）
        int minutesLeft = await _api.QueryAsync<int>("SELECT TIMESTAMPDIFF(MINUTE, NOW(), au_email_expires) FROM auth WHERE auth_email = @email;", new { email });
        Assert.InRange(minutesLeft, 24 * 60 - 2, 24 * 60);

        string token = await _api.QueryAsync<string>("SELECT au_email_token FROM auth WHERE auth_email = @email;", new { email });
        HttpResponseMessage verify = await _api.CreateClient().GetAsync($"/api/Auth/VerifyEmail?token={token}");
        Assert.Equal(HttpStatusCode.OK, verify.StatusCode);
        Assert.Equal("text/html", verify.Content.Headers.ContentType.MediaType);

        ApiResult<LoginResponse> after = await LoginAsync(email, "Pass1234!");
        Assert.True(after.isSuccess, after.message);
        Assert.False(string.IsNullOrEmpty(after.Result.token));
        Assert.Equal("Tourist", after.Result.account_type_name);
    }

    private async Task<HttpResponseMessage> RegisterAsync(object body) =>
        await _api.CreateClient().PostAsJsonAsync("/api/Auth/Register", body);

    private async Task<HttpResponseMessage> LoginResponseAsync(string account, string password) =>
        await _api.CreateClient().PostAsJsonAsync("/api/Auth/Login", new { auth_name = account, auth_pswd = password });

    private Task<int> AuIdOf(string email) =>
        _api.QueryAsync<int>("SELECT au_id FROM auth WHERE auth_email = @email;", new { email });

    [Fact]
    public async Task 驗證過的Email不能再註冊_回409()
    {
        var (name, email) = NewAccount();
        await RegisterAndVerifyAsync(name, email, "Pass1234!");

        HttpResponseMessage second = await RegisterAsync(new { Username = NewAccount().name, Email = email.ToUpperInvariant(), Password = "Pass1234!", AccountType = 1 });

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Contains("已被註冊", (await second.ReadResultAsync()).message);
    }

    [Fact]
    public async Task 還沒驗證的Email可以重新註冊_換成新資料並重寄驗證信()
    {
        var (name, email) = NewAccount();
        await RegisterAsync(new { Username = name, Email = email, Password = "Pass1234!", AccountType = 1 });
        string oldToken = await _api.QueryAsync<string>("SELECT au_email_token FROM auth WHERE auth_email = @email;", new { email });

        string newName = NewAccount().name;
        HttpResponseMessage again = await RegisterAsync(new { Username = newName, Email = email, Password = "Other123!", AccountType = 1 });
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal(1, await _api.QueryAsync<int>("SELECT COUNT(1) FROM auth WHERE auth_email = @email;", new { email }));

        // 舊的驗證連結失效，新的才有用
        string newToken = await _api.QueryAsync<string>("SELECT au_email_token FROM auth WHERE auth_email = @email;", new { email });
        Assert.NotEqual(oldToken, newToken);
        Assert.Contains("驗證失敗", await (await _api.CreateClient().GetAsync($"/api/Auth/VerifyEmail?token={oldToken}")).Content.ReadAsStringAsync());
        await _api.CreateClient().GetAsync($"/api/Auth/VerifyEmail?token={newToken}");

        ApiResult<LoginResponse> login = await LoginAsync(newName, "Other123!");
        Assert.True(login.isSuccess, login.message);
        Assert.False((await LoginAsync(email, "Pass1234!")).isSuccess);
    }

    [Fact]
    public async Task 帳號名稱重複回409_不分大小寫_也不能跟別人的Email一樣()
    {
        var (name, email) = NewAccount();
        await RegisterAsync(new { Username = name, Email = email, Password = "Pass1234!" });

        HttpResponseMessage sameName = await RegisterAsync(new { Username = $"  {name.ToUpperInvariant()} ", Email = NewAccount().email, Password = "Pass1234!" });
        Assert.Equal(HttpStatusCode.Conflict, sameName.StatusCode);
        Assert.Contains("名稱", (await sameName.ReadResultAsync()).message);

        HttpResponseMessage nameIsEmail = await RegisterAsync(new { Username = email, Email = NewAccount().email, Password = "Pass1234!" });
        Assert.Equal(HttpStatusCode.Conflict, nameIsEmail.StatusCode);
    }

    [Fact]
    public async Task 遊客註冊不能建立商家帳號_請改用商家註冊()
    {
        var (name, email) = NewAccount();

        HttpResponseMessage response = await RegisterAsync(new { Username = name, Email = email, Password = "Pass1234!", AccountType = 2 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Merchant/Register", (await response.ReadResultAsync()).message);
        Assert.Equal(0, await _api.QueryAsync<int>("SELECT COUNT(1) FROM auth WHERE auth_email = @email;", new { email }));
    }

    [Theory]
    [InlineData("", "ok@example.com", "Pass1234!", null, "帳號名稱")]
    [InlineData("   ", "ok@example.com", "Pass1234!", null, "帳號名稱")]
    [InlineData("探員", "", "Pass1234!", null, "Email")]
    [InlineData("探員", "not-an-email", "Pass1234!", null, "Email")]
    [InlineData("探員", "ok@example.com", "12345", null, "密碼")]
    [InlineData("探員", "ok@example.com", " Pass1234!", null, "密碼")]
    [InlineData("探員", "ok@example.com", "Pass1234!", "2999-01-01", "生日")]
    public async Task 註冊欄位不合規則回400_不會建立帳號(string username, string emailInput, string password, string birthday, string expected)
    {
        string id = Guid.NewGuid().ToString("N")[..10];
        string name = username.Length > 0 && username.Trim().Length > 0 ? username + id : username;
        string email = emailInput == "ok@example.com" ? $"bad{id}@example.com" : emailInput;

        HttpResponseMessage response = await RegisterAsync(new { Username = name, Email = email, Password = password, Birthday = birthday });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(expected, (await response.ReadResultAsync()).message);
        Assert.Equal(0, await _api.QueryAsync<int>("SELECT COUNT(1) FROM auth WHERE auth_email = @email;", new { email }));
    }

    [Fact]
    public async Task 登入失敗回400_訊息是原因()
    {
        var (name, email) = NewAccount();
        await RegisterAndVerifyAsync(name, email, "Pass1234!");

        HttpResponseMessage wrong = await LoginResponseAsync(name, "Wrong123!");
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        Assert.Equal("帳號或密碼錯誤", (await wrong.ReadResultAsync()).message);

        HttpResponseMessage nobody = await LoginResponseAsync($"沒有這個人{Guid.NewGuid():N}", "Pass1234!");
        Assert.Equal(HttpStatusCode.BadRequest, nobody.StatusCode);

        HttpResponseMessage empty = await LoginResponseAsync(" ", "");
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);

        // 名稱前後多打空白也能登入
        Assert.True((await LoginAsync($" {name} ", "Pass1234!")).isSuccess);
    }

    [Fact]
    public async Task 密碼對了才告訴對方信箱還沒驗證()
    {
        var (name, email) = NewAccount();
        await RegisterAsync(new { Username = name, Email = email, Password = "Pass1234!" });

        ApiResult<LoginResponse> wrongPassword = await LoginAsync(email, "Wrong123!");
        Assert.Equal("帳號或密碼錯誤", wrongPassword.message);

        HttpResponseMessage rightPassword = await LoginResponseAsync(email, "Pass1234!");
        Assert.Equal(HttpStatusCode.BadRequest, rightPassword.StatusCode);
        Assert.Contains("驗證", (await rightPassword.ReadResultAsync()).message);
    }

    [Fact]
    public async Task 過去留下的重名帳號_用密碼對得上且已驗證的那個登入()
    {
        // 舊資料：同一個名稱有兩個帳號，先註冊的沒驗證信箱、後註冊的有驗證，密碼一樣（今天 admin2 的情況）
        var (name, pendingEmail) = NewAccount();
        await RegisterAsync(new { Username = name, Email = pendingEmail, Password = "Pass1234!" });
        var (otherName, verifiedEmail) = NewAccount();
        await RegisterAndVerifyAsync(otherName, verifiedEmail, "Pass1234!");
        await _api.ExecuteAsync("UPDATE auth SET auth_name = @name WHERE auth_email = @verifiedEmail;", new { name, verifiedEmail });

        ApiResult<LoginResponse> login = await LoginAsync(name, "Pass1234!");
        Assert.True(login.isSuccess, login.message);
        Assert.Equal(await AuIdOf(verifiedEmail), login.Result.au_id);

        // 密碼不一樣時，各自用自己的密碼登入
        var (_, thirdEmail) = NewAccount();
        await RegisterAndVerifyAsync(NewAccount().name, thirdEmail, "Third123!");
        await _api.ExecuteAsync("UPDATE auth SET auth_name = @name WHERE auth_email = @thirdEmail;", new { name, thirdEmail });

        Assert.Equal(await AuIdOf(thirdEmail), (await LoginAsync(name, "Third123!")).Result.au_id);
        Assert.Equal(await AuIdOf(verifiedEmail), (await LoginAsync(name, "Pass1234!")).Result.au_id);
    }

    [Fact]
    public async Task 改帳號名稱也不能跟別人重複()
    {
        var (name, email) = NewAccount();
        await RegisterAndVerifyAsync(name, email, "Pass1234!");
        var (otherName, otherEmail) = NewAccount();
        await RegisterAndVerifyAsync(otherName, otherEmail, "Pass1234!");
        HttpClient client = WithToken((await LoginAsync(email, "Pass1234!")).Result.token);

        HttpResponseMessage taken = await client.PostAsJsonAsync("/api/Auth/Profile", new { auth_name = otherName });
        Assert.Equal(HttpStatusCode.Conflict, taken.StatusCode);

        HttpResponseMessage blank = await client.PostAsJsonAsync("/api/Auth/Profile", new { auth_name = "  " });
        Assert.Equal(HttpStatusCode.BadRequest, blank.StatusCode);

        // 改成自己原本的名稱（只差大小寫）可以
        HttpResponseMessage same = await client.PostAsJsonAsync("/api/Auth/Profile", new { auth_name = name.ToUpperInvariant() });
        Assert.Equal(HttpStatusCode.OK, same.StatusCode);
    }

    [Fact]
    public async Task 用登入Token改名稱_改密碼_登出()
    {
        var (name, email) = NewAccount();
        await RegisterAndVerifyAsync(name, email, "OldPass1!");
        string token = (await LoginAsync(email, "OldPass1!")).Result.token;
        HttpClient client = WithToken(token);

        // 改名稱
        Assert.True((await (await client.PostAsJsonAsync("/api/Auth/Profile", new { auth_name = name + "改" })).ReadResultAsync()).isSuccess);
        LoginResponse profile = (await (await client.GetAsync("/api/Auth/Profile")).ReadResultAsync<LoginResponse>()).Result;
        Assert.Equal(name + "改", profile.auth_name);

        // 舊密碼打錯不能改
        var wrong = await (await client.PostAsJsonAsync("/api/Auth/ChangePassword", new { OldPassword = "nope", NewPassword = "NewPass1!" })).ReadResultAsync();
        Assert.False(wrong.isSuccess);

        // 改密碼：新的能登入、舊的不行
        Assert.True((await (await client.PostAsJsonAsync("/api/Auth/ChangePassword", new { OldPassword = "OldPass1!", NewPassword = "NewPass1!" })).ReadResultAsync()).isSuccess);
        Assert.True((await LoginAsync(email, "NewPass1!")).isSuccess);
        Assert.False((await LoginAsync(email, "OldPass1!")).isSuccess);

        // 登出
        Assert.True((await (await client.PostAsync("/api/Auth/Logout", null)).ReadResultAsync()).isSuccess);
    }

    [Fact]
    public async Task 忘記密碼後用重設碼設定新密碼()
    {
        var (name, email) = NewAccount();
        await RegisterAndVerifyAsync(name, email, "OldPass1!");

        Assert.True((await (await _api.CreateClient().PostAsJsonAsync("/api/Auth/ForgotPassword", new { Email = email })).ReadResultAsync()).isSuccess);
        string resetToken = await _api.QueryAsync<string>("SELECT pwd_reset_token FROM auth WHERE auth_email = @email;", new { email });
        Assert.False(string.IsNullOrEmpty(resetToken));
        int minutesLeft = await _api.QueryAsync<int>("SELECT TIMESTAMPDIFF(MINUTE, NOW(), pwd_reset_expires) FROM auth WHERE auth_email = @email;", new { email });
        Assert.InRange(minutesLeft, 28, 30);   // 重設連結 30 分鐘內有效

        var reset = await (await _api.CreateClient().PostAsJsonAsync("/api/Auth/ResetPassword", new { Token = resetToken, NewPassword = "Reset123!" })).ReadResultAsync();
        Assert.True(reset.isSuccess, reset.message);
        Assert.True((await LoginAsync(email, "Reset123!")).isSuccess);

        // 重設碼只能用一次
        var again = await (await _api.CreateClient().PostAsJsonAsync("/api/Auth/ResetPassword", new { Token = resetToken, NewPassword = "Other123!" })).ReadResultAsync();
        Assert.False(again.isSuccess);
    }

    [Fact]
    public async Task 重設密碼信的連結打開是重設網頁_不會用掉重設碼()
    {
        var (name, email) = NewAccount();
        await RegisterAndVerifyAsync(name, email, "OldPass1!");
        await _api.CreateClient().PostAsJsonAsync("/api/Auth/ForgotPassword", new { Email = email });
        string resetToken = await _api.QueryAsync<string>("SELECT pwd_reset_token FROM auth WHERE auth_email = @email;", new { email });

        HttpResponseMessage page = await _api.CreateClient().GetAsync($"/api/Auth/ResetPassword?token={resetToken}");

        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Equal("text/html", page.Content.Headers.ContentType.MediaType);
        Assert.Contains("id='reset-form'", await page.Content.ReadAsStringAsync());

        // 網頁送出的就是 POST ResetPassword；開過網頁後重設碼還能用
        var reset = await (await _api.CreateClient().PostAsJsonAsync("/api/Auth/ResetPassword", new { Token = resetToken, NewPassword = "Reset123!" })).ReadResultAsync();
        Assert.True(reset.isSuccess, reset.message);
        Assert.True((await LoginAsync(email, "Reset123!")).isSuccess);
    }

    [Fact]
    public async Task 重設碼無效時重設網頁顯示連結已失效()
    {
        HttpResponseMessage page = await _api.CreateClient().GetAsync("/api/Auth/ResetPassword?token=not-a-real-token");

        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        string html = await page.Content.ReadAsStringAsync();
        Assert.Contains("連結已失效", html);
        Assert.DoesNotContain("id='reset-form'", html);
    }

    [Fact]
    public async Task 忘記密碼用沒註冊過的Email回傳錯誤()
    {
        var result = await (await _api.CreateClient().PostAsJsonAsync("/api/Auth/ForgotPassword", new { Email = "nobody@example.com" })).ReadResultAsync();

        Assert.False(result.isSuccess);
    }

    /// <summary>註冊並驗證一個帳號後改成商家帳號（auth_type = 2），withStore 時一併建立店家，回傳 (email, s_id)</summary>
    private async Task<(string email, int? sId)> CreateMerchantAccountAsync(string password, bool withStore)
    {
        var (name, email) = NewAccount();
        await RegisterAndVerifyAsync(name, email, password);
        int auId = await _api.QueryAsync<int>("SELECT au_id FROM auth WHERE auth_email = @email;", new { email });
        await _api.ExecuteAsync("UPDATE auth SET auth_type = 2 WHERE au_id = @auId;", new { auId });
        if (!withStore) return (email, null);

        await _api.ExecuteAsync("INSERT INTO store (au_id, store_name, store_city) VALUES (@auId, @name, '臺中市');", new { auId, name = $"測試小吃{auId}" });
        return (email, await _api.QueryAsync<int>("SELECT s_id FROM store WHERE au_id = @auId;", new { auId }));
    }

    [Fact]
    public async Task 商家帳號也用一般登入_Token可以直接呼叫商家API()
    {
        var (email, sId) = await CreateMerchantAccountAsync("Pass1234!", withStore: true);

        ApiResult<LoginResponse> login = await LoginAsync(email, "Pass1234!");

        Assert.True(login.isSuccess, login.message);
        Assert.Equal(2, login.Result.auth_type);
        Assert.Equal("Merchant", login.Result.account_type_name);
        Assert.Equal(sId, login.Result.store_id);

        var profile = await (await WithToken(login.Result.token).GetAsync("/api/Merchant/Profile")).ReadResultAsync();
        Assert.True(profile.isSuccess, profile.message);
    }

    [Fact]
    public async Task 商家帳號還沒有店家資料時不能登入()
    {
        var (email, _) = await CreateMerchantAccountAsync("Pass1234!", withStore: false);

        ApiResult<LoginResponse> login = await LoginAsync(email, "Pass1234!");

        Assert.False(login.isSuccess);
        Assert.Contains("商家註冊", login.message);
    }

    [Fact]
    public async Task 遊客登入不會帶店家代號()
    {
        var (name, email) = NewAccount();
        await RegisterAndVerifyAsync(name, email, "Pass1234!");

        ApiResult<LoginResponse> login = await LoginAsync(email, "Pass1234!");

        Assert.True(login.isSuccess, login.message);
        Assert.Null(login.Result.store_id);
    }

    [Fact]
    public async Task 已經沒有獨立的商家登入()
    {
        HttpResponseMessage response = await _api.CreateClient().PostAsJsonAsync("/api/Merchant/Login", new { auth_name = "someone", auth_pswd = "Pass1234!" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
