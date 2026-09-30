// 商家後台（不需要 Neo4j 的部分）：店家名稱、優惠券增刪改查與上下架、NFC 綁定、核銷、題庫增刪改
// 這個階段商家 API 不驗證登入，以 request 帶入的 s_id / au_id 識別
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace TrafficSystem.Tests.Api;

[Collection(ApiCollection.Name)]
public class MerchantManageApiTests
{
    private readonly ApiFactory _api;

    public MerchantManageApiTests(ApiFactory api) => _api = api;

    /// <summary>建立一個商家帳號與店家，回傳 (au_id, s_id)</summary>
    private async Task<(int auId, int sId)> CreateStoreAsync()
    {
        string id = Guid.NewGuid().ToString("N")[..10];
        await _api.ExecuteAsync(@"
            INSERT INTO auth (auth_name, auth_type, auth_email, auth_pswd, is_active, is_email_verified)
            VALUES (@name, 2, @email, 'x', 1, 1);", new { name = $"商家{id}", email = $"store{id}@example.com" });
        int auId = await _api.QueryAsync<int>("SELECT au_id FROM auth WHERE auth_email = @email;", new { email = $"store{id}@example.com" });
        await _api.ExecuteAsync("INSERT INTO store (au_id, store_name, store_city) VALUES (@auId, @name, '臺中市');", new { auId, name = $"測試小吃{id}" });
        int sId = await _api.QueryAsync<int>("SELECT s_id FROM store WHERE au_id = @auId;", new { auId });
        return (auId, sId);
    }

    private static object Coupon(int sId, string code) => new
    {
        s_id = sId, coupon_code = code, coupon_name = "滷肉飯折 10 元", discount_commodity = "滷肉飯",
        discount_type = "amount", discount_value = 10, valid_from = "2026-09-01T00:00:00", valid_to = "2026-12-31T23:59:59"
    };

    private async Task<int> CreateCouponAsync(int sId)
    {
        var created = await (await _api.CreateClient().PostAsJsonAsync("/api/merchant/coupons", Coupon(sId, $"C{Guid.NewGuid():N}"[..12]))).ReadResultAsync();
        Assert.True(created.isSuccess, created.message);
        return created.Result.GetProperty("coupon_id").GetInt32();
    }

    [Fact]
    public async Task 商家修改店家名稱()
    {
        var (auId, sId) = await CreateStoreAsync();

        var result = await (await _api.ClientFor(auId).PostAsJsonAsync("/api/Merchant/StoreName", new { store_name = "阿嬤的芒果冰" })).ReadResultAsync();

        Assert.True(result.isSuccess, result.message);
        Assert.Equal("阿嬤的芒果冰", await _api.QueryAsync<string>("SELECT store_name FROM store WHERE s_id = @sId;", new { sId }));
    }

    [Fact]
    public async Task 優惠券新增_查詢_修改_下架_刪除()
    {
        var (_, sId) = await CreateStoreAsync();
        HttpClient client = _api.CreateClient();

        int couponId = await CreateCouponAsync(sId);

        var one = await (await client.GetAsync($"/api/merchant/coupons/{couponId}")).ReadResultAsync();
        Assert.Equal("active", one.Result.GetProperty("status").GetString());

        var update = await (await client.PutAsJsonAsync($"/api/merchant/coupons/{couponId}", new
        {
            coupon_code = "SUMMER", coupon_name = "芒果冰 9 折", discount_commodity = "芒果冰", discount_type = "percent",
            discount_value = 90, valid_from = "2026-09-01T00:00:00", valid_to = "2026-12-31T23:59:59"
        })).ReadResultAsync();
        Assert.True(update.isSuccess, update.message);

        var off = await (await client.PatchAsJsonAsync($"/api/merchant/coupons/{couponId}/status", new { status = "inactive" })).ReadResultAsync();
        Assert.True(off.isSuccess, off.message);

        var list = await (await client.GetAsync($"/api/merchant/coupons?storeId={sId}")).ReadResultAsync();
        JsonElement coupon = list.Result.EnumerateArray().Single(c => c.GetProperty("coupon_id").GetInt32() == couponId);
        Assert.Equal("芒果冰 9 折", coupon.GetProperty("coupon_name").GetString());
        Assert.Equal("inactive", coupon.GetProperty("status").GetString());

        var deleted = await (await client.DeleteAsync($"/api/merchant/coupons/{couponId}")).ReadResultAsync();
        Assert.True(deleted.isSuccess, deleted.message);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/merchant/coupons/{couponId}")).StatusCode);
    }

    [Fact]
    public async Task 優惠券資料不合規定時擋下()
    {
        var (_, sId) = await CreateStoreAsync();
        HttpClient client = _api.CreateClient();
        int couponId = await CreateCouponAsync(sId);

        var badStatus = await (await client.PatchAsJsonAsync($"/api/merchant/coupons/{couponId}/status", new { status = "deleted" })).ReadResultAsync();
        Assert.False(badStatus.isSuccess);

        var badType = await (await client.PostAsJsonAsync("/api/merchant/coupons", new
        {
            s_id = sId, coupon_code = "BAD", coupon_name = "錯誤", discount_commodity = "x",
            discount_type = "free", discount_value = 1, valid_from = "2026-09-01T00:00:00", valid_to = "2026-12-31T23:59:59"
        })).ReadResultAsync();
        Assert.False(badType.isSuccess);
    }

    [Fact]
    public async Task NFC貼紙綁定優惠券_同一張貼紙不能綁兩次()
    {
        var (_, sId) = await CreateStoreAsync();
        int couponId = await CreateCouponAsync(sId);
        string nfcUid = $"NFC-{Guid.NewGuid():N}"[..20];

        var first = await (await _api.CreateClient().PostAsJsonAsync("/api/merchant/nfc/bind", new { nfc_uid = nfcUid, coupon_id = couponId })).ReadResultAsync();
        Assert.True(first.isSuccess, first.message);

        HttpResponseMessage second = await _api.CreateClient().PostAsJsonAsync("/api/merchant/nfc/bind", new { nfc_uid = nfcUid, coupon_id = couponId });
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task 領取過的優惠券可以核銷一次()
    {
        var (_, sId) = await CreateStoreAsync();
        int couponId = await CreateCouponAsync(sId);
        int player = _api.NewUserId();
        // 領取是在掃 NFC 時發生（需要 Neo4j），這裡直接放一筆「已領取」
        await _api.ExecuteAsync("INSERT INTO user_coupon (au_id, coupon_id, obtained_at, is_used) VALUES (@player, @couponId, NOW(), 0);", new { player, couponId });

        var redeem = await (await _api.CreateClient().PostAsJsonAsync($"/api/coupons/{couponId}/redeem", new { au_id = player })).ReadResultAsync();
        Assert.True(redeem.isSuccess, redeem.message);

        HttpResponseMessage again = await _api.CreateClient().PostAsJsonAsync($"/api/coupons/{couponId}/redeem", new { au_id = player });
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Fact]
    public async Task 沒領取過的優惠券不能核銷()
    {
        var (_, sId) = await CreateStoreAsync();
        int couponId = await CreateCouponAsync(sId);

        HttpResponseMessage response = await _api.CreateClient().PostAsJsonAsync($"/api/coupons/{couponId}/redeem", new { au_id = _api.NewUserId() });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task 題庫新增_修改_刪除()
    {
        var (_, sId) = await CreateStoreAsync();
        HttpClient client = _api.CreateClient();
        object Options(string correct) => new[]
        {
            new { option_key = "A", option_context = "芒果", is_correct = correct == "A", option_url = (string)null },
            new { option_key = "B", option_context = "鳳梨", is_correct = correct == "B", option_url = (string)null },
        };

        var created = await (await client.PostAsJsonAsync("/api/merchant/questions",
            new { store_id = sId, question_describe = "本店招牌冰品用的是哪種水果？", options = Options("A") })).ReadResultAsync();
        Assert.True(created.isSuccess, created.message);
        int questionId = created.Result.GetProperty("question_id").GetInt32();

        var updated = await (await client.PutAsJsonAsync($"/api/merchant/questions/{questionId}",
            new { question_describe = "夏季限定冰品用的是哪種水果？", options = Options("B") })).ReadResultAsync();
        Assert.True(updated.isSuccess, updated.message);

        var list = await (await client.GetAsync($"/api/merchant/questions?storeId={sId}")).ReadResultAsync();
        Assert.Contains("夏季限定", list.Result.ToString());

        var deleted = await (await client.DeleteAsync($"/api/merchant/questions/{questionId}")).ReadResultAsync();
        Assert.True(deleted.isSuccess, deleted.message);
        Assert.DoesNotContain("夏季限定", (await (await client.GetAsync($"/api/merchant/questions?storeId={sId}")).ReadResultAsync()).Result.ToString());
    }

    [Fact]
    public async Task 題目已經被任務使用時不能刪除()
    {
        var (_, sId) = await CreateStoreAsync();
        HttpClient client = _api.CreateClient();
        var created = await (await client.PostAsJsonAsync("/api/merchant/questions", new
        {
            store_id = sId, question_describe = "店門口的招牌是什麼顏色？",
            options = new[] { new { option_key = "A", option_context = "紅色", is_correct = true, option_url = (string)null } }
        })).ReadResultAsync();
        int questionId = created.Result.GetProperty("question_id").GetInt32();
        await _api.ExecuteAsync("INSERT INTO task (task_type, question_id) VALUES (6, @questionId);", new { questionId });

        HttpResponseMessage response = await client.DeleteAsync($"/api/merchant/questions/{questionId}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(1, await _api.QueryAsync<int>("SELECT COUNT(*) FROM store_question WHERE question_id = @questionId;", new { questionId }));
    }
}
