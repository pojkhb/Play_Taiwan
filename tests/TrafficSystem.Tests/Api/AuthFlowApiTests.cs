// 帳號流程：註冊 → 信箱驗證 → 登入 → 改名稱 → 改密碼 → 忘記密碼 → 重設密碼 → 登出
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

    [Fact]
    public async Task 同一個Email不能註冊兩次()
    {
        var (name, email) = NewAccount();
        var body = new { Username = name, Email = email, Password = "Pass1234!", AccountType = 1 };
        await _api.CreateClient().PostAsJsonAsync("/api/Auth/Register", body);

        ApiResult<string> second = await (await _api.CreateClient().PostAsJsonAsync("/api/Auth/Register", body)).ReadResultAsync<string>();

        Assert.False(second.isSuccess);
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
    public async Task 忘記密碼用沒註冊過的Email回傳錯誤()
    {
        var result = await (await _api.CreateClient().PostAsJsonAsync("/api/Auth/ForgotPassword", new { Email = "nobody@example.com" })).ReadResultAsync();

        Assert.False(result.isSuccess);
    }
}
