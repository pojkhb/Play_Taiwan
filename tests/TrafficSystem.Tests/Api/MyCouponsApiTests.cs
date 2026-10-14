// 遊客查看自己擁有的優惠券：GET /api/coupons
// 回傳領過的全部優惠券（含已核銷、已下架、已過期），用 is_used／is_usable 區分；店家照片用商家的景點 ID 向 Neo4j 拿。
using System.Net;
using backend.ViewModels;
using Allure.Net.Commons.Attributes;

namespace TrafficSystem.Tests.Api;

[Collection(ApiCollection.Name)]
[AllureSuiteHierarchy("商家與優惠券", "API 整合測試")]
[AllureBddHierarchy("商家與優惠券", "API 整合測試")]
public class MyCouponsApiTests
{
    private readonly ApiFactory _api;

    public MyCouponsApiTests(ApiFactory api)
    {
        _api = api;
        _api.FakeNeo4j.Reset();
    }

    /// <summary>建立商家帳號與店家（臺中市西區三民路一段198號），回傳 (s_id, store_uid, store_name)</summary>
    private async Task<(int sId, string storeUid, string storeName)> CreateStoreAsync()
    {
        string id = Guid.NewGuid().ToString("N")[..10];
        string storeUid = Guid.NewGuid().ToString();
        string storeName = $"券包小吃{id}";

        await _api.ExecuteAsync(@"
            INSERT INTO auth (auth_name, auth_type, auth_email, auth_pswd, is_active, is_email_verified)
            VALUES (@name, 2, @email, 'x', 1, 1);", new { name = $"商家{id}", email = $"mine{id}@example.com" });
        int auId = await _api.QueryAsync<int>("SELECT au_id FROM auth WHERE auth_email = @email;", new { email = $"mine{id}@example.com" });
        await _api.ExecuteAsync(@"
            INSERT INTO store (au_id, store_name, store_city, store_town, store_address, store_uid)
            VALUES (@auId, @storeName, '臺中市', '西區', '三民路一段198號', @storeUid);", new { auId, storeName, storeUid });
        int sId = await _api.QueryAsync<int>("SELECT s_id FROM store WHERE au_id = @auId;", new { auId });
        return (sId, storeUid, storeName);
    }

    /// <summary>建立優惠券；有效期間用「離現在幾天」表示，null 代表不限</summary>
    private async Task<int> CreateCouponAsync(int sId, string name, string status = "active", int? fromDays = null, int? toDays = null)
    {
        string code = $"C{Guid.NewGuid():N}"[..12];
        await _api.ExecuteAsync(@"
            INSERT INTO coupon (s_id, coupon_code, coupon_name, discount_commodity, discount_type, discount_value, valid_from, valid_to, status)
            VALUES (@sId, @code, @name, '滷肉飯', 'amount', 10,
                    IF(@fromDays IS NULL, NULL, NOW() + INTERVAL @fromDays DAY),
                    IF(@toDays IS NULL, NULL, NOW() + INTERVAL @toDays DAY),
                    @status);", new { sId, code, name, fromDays, toDays, status });
        return await _api.QueryAsync<int>("SELECT coupon_id FROM coupon WHERE coupon_code = @code;", new { code });
    }

    /// <summary>直接放一筆領券紀錄（領取是在掃 QR Code 時發生，需要 Neo4j）</summary>
    private Task ClaimAsync(int auId, int couponId, int minutesAgo, bool used = false) =>
        _api.ExecuteAsync(@"
            INSERT INTO user_coupon (au_id, coupon_id, obtained_at, is_used, used_at)
            VALUES (@auId, @couponId, NOW() - INTERVAL @minutesAgo MINUTE, @used, IF(@used, NOW(), NULL));",
            new { auId, couponId, minutesAgo, used });

    private async Task<List<MyCouponResponse>> GetMineAsync(int auId)
    {
        HttpResponseMessage response = await _api.ClientFor(auId).GetAsync("/api/coupons");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.ReadResultAsync<List<MyCouponResponse>>()).Result;
    }

    [Fact]
    public async Task 列出自己領過的全部優惠券_附店家資訊與能不能用()
    {
        var (sId, storeUid, storeName) = await CreateStoreAsync();
        int usable = await CreateCouponAsync(sId, "可以用", fromDays: -1, toDays: 30);
        int used = await CreateCouponAsync(sId, "用過了");
        int inactive = await CreateCouponAsync(sId, "商家下架", status: "inactive");
        int expired = await CreateCouponAsync(sId, "過期了", fromDays: -30, toDays: -1);
        int notYet = await CreateCouponAsync(sId, "還沒開始", fromDays: 1, toDays: 30);

        int player = _api.NewUserId();
        await ClaimAsync(player, usable, minutesAgo: 1);
        await ClaimAsync(player, used, minutesAgo: 2, used: true);
        await ClaimAsync(player, inactive, minutesAgo: 3);
        await ClaimAsync(player, expired, minutesAgo: 4);
        await ClaimAsync(player, notYet, minutesAgo: 5);
        await ClaimAsync(_api.NewUserId(), usable, minutesAgo: 1);   // 別人領的不會出現

        _api.FakeNeo4j.Rows = new List<Dictionary<string, object>>
        {
            new() { ["uid"] = storeUid, ["image_url"] = "https://neo4j-img.test/store-front.jpg" }
        };
        try
        {
            List<MyCouponResponse> mine = await GetMineAsync(player);

            // 新領的排前面
            Assert.Equal(new[] { usable, used, inactive, expired, notYet }, mine.Select(c => c.coupon.coupon_id));
            Assert.Equal(new[] { true, false, false, false, false }, mine.Select(c => c.is_usable));

            MyCouponResponse first = mine[0];
            Assert.Equal("可以用", first.coupon.coupon_name);
            Assert.Equal(storeName, first.store_name);
            Assert.Equal("臺中市西區三民路一段198號", first.full_address);
            Assert.Equal("https://neo4j-img.test/store-front.jpg", first.image_url);
            Assert.False(first.is_used);
            Assert.Null(first.used_at);

            MyCouponResponse usedOne = mine[1];
            Assert.True(usedOne.is_used);
            Assert.NotNull(usedOne.used_at);
            Assert.Equal("inactive", mine[2].coupon.status);
        }
        finally
        {
            _api.FakeNeo4j.Reset();   // 其他測試預設 Neo4j 連不上
        }
    }

    [Fact]
    public async Task 核銷之後列表上變成已使用()
    {
        var (sId, _, _) = await CreateStoreAsync();
        int couponId = await CreateCouponAsync(sId, "滷肉飯折 10 元");
        int player = _api.NewUserId();
        await ClaimAsync(player, couponId, minutesAgo: 1);

        Assert.True(Assert.Single(await GetMineAsync(player)).is_usable);

        var redeem = await (await _api.ClientFor(player).PostAsync($"/api/coupons/{couponId}/redeem", null)).ReadResultAsync();
        Assert.True(redeem.isSuccess, redeem.message);

        MyCouponResponse after = Assert.Single(await GetMineAsync(player));
        Assert.True(after.is_used);
        Assert.NotNull(after.used_at);
        Assert.False(after.is_usable);
    }

    [Fact]
    public async Task Neo4j連不上時照片是null_列表照常回傳()
    {
        var (sId, _, storeName) = await CreateStoreAsync();
        int couponId = await CreateCouponAsync(sId, "滷肉飯折 10 元");
        int player = _api.NewUserId();
        await ClaimAsync(player, couponId, minutesAgo: 1);

        MyCouponResponse coupon = Assert.Single(await GetMineAsync(player));
        Assert.Equal(storeName, coupon.store_name);
        Assert.Null(coupon.image_url);
        Assert.Empty(coupon.operating_hours);
    }

    [Fact]
    public async Task 每張券附上店家營業時間_依星期排序()
    {
        var (sId, storeUid, _) = await CreateStoreAsync();
        int couponId = await CreateCouponAsync(sId, "滷肉飯折 10 元");
        int player = _api.NewUserId();
        await ClaimAsync(player, couponId, minutesAgo: 1);

        _api.FakeNeo4j.Rows = new List<Dictionary<string, object>>
        {
            new()
            {
                ["uid"] = storeUid,
                ["operatingHours"] = new List<object>
                {
                    new Dictionary<string, object> { ["day_of_week"] = "Sunday", ["open_time"] = "10:00", ["close_time"] = "20:00" },
                    new Dictionary<string, object> { ["day_of_week"] = "Tuesday", ["open_time"] = "17:00", ["close_time"] = "21:00" },
                    new Dictionary<string, object> { ["day_of_week"] = "Tuesday", ["open_time"] = "11:00", ["close_time"] = "14:00" }
                }
            }
        };
        try
        {
            MyCouponResponse coupon = Assert.Single(await GetMineAsync(player));
            Assert.Equal(
                new[] { "Tuesday 11:00-14:00", "Tuesday 17:00-21:00", "Sunday 10:00-20:00" },
                coupon.operating_hours.Select(h => $"{h.day_of_week} {h.open_time}-{h.close_time}"));
        }
        finally
        {
            _api.FakeNeo4j.Reset();
        }
    }

    [Fact]
    public async Task 沒領過優惠券時回傳空陣列()
    {
        Assert.Empty(await GetMineAsync(_api.NewUserId()));
    }

    [Fact]
    public async Task 沒登入不能看()
    {
        HttpResponseMessage response = await _api.ClientFor(null).GetAsync("/api/coupons");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
