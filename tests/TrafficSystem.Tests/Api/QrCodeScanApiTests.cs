// 遊客掃商家的 QR Code：GET /api/qrcode/scan/{qrUid}
// 商家資訊與店家照片（image_url）都用商家的景點 ID（store.store_uid）向 Neo4j 拿，優惠券來自 MySQL。
using System.Net;
using System.Net.Http.Headers;
using backend.Services.Neo4j;
using backend.ViewModels;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Allure.Net.Commons.Attributes;

namespace TrafficSystem.Tests.Api;

[Collection(ApiCollection.Name)]
[AllureSuiteHierarchy("商家與優惠券", "API 整合測試")]
[AllureBddHierarchy("商家與優惠券", "API 整合測試")]
public class QrCodeScanApiTests
{
    private readonly ApiFactory _api;

    public QrCodeScanApiTests(ApiFactory api)
    {
        _api = api;
        _api.FakeNeo4j.Reset();
    }

    /// <summary>
    /// 假的 Neo4j：查景點目前生效資料（版本鏈）時回傳這間店；查照片（PlaceLookupService）時回傳指定的照片，
    /// 一個景點有多張時跟真的查詢一樣取網址排序的第一張。
    /// </summary>
    private class StubGateway : INeo4jGatewayService
    {
        public string StoreUid { get; init; }
        public List<string> Images { get; init; } = new();
        /// <summary>景點的營業時段（day_of_week, open_time, close_time），照 Neo4j collect 回來的順序放</summary>
        public List<(string day, string open, string close)> Hours { get; init; } = new();

        public Task<List<Dictionary<string, object>>> ExecuteCypherAsync(string query, object parameters = null)
        {
            var rows = new List<Dictionary<string, object>>();

            if (query.Contains("identityLabels"))
            {
                rows.Add(new Dictionary<string, object>
                {
                    ["uid"] = StoreUid,
                    ["identityLabels"] = new List<object> { "Place", "MerchantPlace" },
                    ["govAddress"] = "臺中市西區三民路一段198號",
                    ["images"] = Images.Select(url => (object)new Dictionary<string, object> { ["url"] = url, ["description"] = null }).ToList(),
                    ["operatingHours"] = Hours.Select(h => (object)new Dictionary<string, object>
                    {
                        ["day_of_week"] = h.day, ["open_time"] = h.open, ["close_time"] = h.close
                    }).ToList()
                });
            }
            else if (query.Contains("collect(img.url)[0]"))
            {
                var uids = (IEnumerable<string>)parameters.GetType().GetProperty("uids").GetValue(parameters);
                if (uids.Contains(StoreUid) && Images.Count > 0)
                    rows.Add(new Dictionary<string, object> { ["uid"] = StoreUid, ["image_url"] = Images.OrderBy(url => url, StringComparer.Ordinal).First() });
            }

            return Task.FromResult(rows);
        }
    }

    /// <summary>建立商家、優惠券與綁好的 QR Code 貼紙，回傳 (qr_uid, store_uid, coupon_id)</summary>
    private async Task<(string qrUid, string storeUid, int couponId)> CreateQrCodeAsync()
    {
        string id = Guid.NewGuid().ToString("N")[..10];
        string storeUid = Guid.NewGuid().ToString();
        string qrUid = $"QR-{id}";

        await _api.ExecuteAsync(@"
            INSERT INTO auth (auth_name, auth_type, auth_email, auth_pswd, is_active, is_email_verified)
            VALUES (@name, 2, @email, 'x', 1, 1);", new { name = $"商家{id}", email = $"scan{id}@example.com" });
        int auId = await _api.QueryAsync<int>("SELECT au_id FROM auth WHERE auth_email = @email;", new { email = $"scan{id}@example.com" });
        await _api.ExecuteAsync("INSERT INTO store (au_id, store_name, store_city, store_uid) VALUES (@auId, @name, '臺中市', @storeUid);",
            new { auId, name = $"掃碼小吃{id}", storeUid });
        int sId = await _api.QueryAsync<int>("SELECT s_id FROM store WHERE au_id = @auId;", new { auId });

        await _api.ExecuteAsync(@"
            INSERT INTO coupon (s_id, coupon_code, coupon_name, discount_commodity, discount_type, discount_value, status)
            VALUES (@sId, @code, '滷肉飯折 10 元', '滷肉飯', 'amount', 10, 'active');", new { sId, code = $"C{id}" });
        int couponId = await _api.QueryAsync<int>("SELECT coupon_id FROM coupon WHERE s_id = @sId;", new { sId });
        await _api.ExecuteAsync("INSERT INTO qrcode_coupon (qr_uid, coupon_id, used_count) VALUES (@qrUid, @couponId, 0);", new { qrUid, couponId });

        return (qrUid, storeUid, couponId);
    }

    /// <summary>Neo4j 換成假的那一份的 HttpClient；token 為 null 時不登入</summary>
    private HttpClient ClientWith(StubGateway gateway, string token = null)
    {
        HttpClient client = _api.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddScoped<INeo4jGatewayService>(_ => gateway))).CreateClient();
        if (token != null)
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [Fact]
    public async Task 掃QRCode回傳店家照片_用商家的景點ID向Neo4j拿()
    {
        var (qrUid, storeUid, couponId) = await CreateQrCodeAsync();
        var gateway = new StubGateway { StoreUid = storeUid, Images = { "https://neo4j-img.test/b-inside.jpg", "https://neo4j-img.test/a-front.jpg" } };

        // 不用登入也能看
        HttpResponseMessage response = await ClientWith(gateway).GetAsync($"/api/qrcode/scan/{qrUid}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        QrCodeScanResponse scan = (await response.ReadResultAsync<QrCodeScanResponse>()).Result;
        Assert.Equal("https://neo4j-img.test/a-front.jpg", scan.image_url);   // 多張時固定同一張（網址排序第一張）
        Assert.Equal(2, scan.merchant_place.images.Count);                    // 全部的照片在 merchant_place.images
        Assert.Equal(storeUid, scan.merchant_place.uid);
        Assert.Equal(couponId, scan.coupon.coupon_id);
        Assert.False(scan.is_newly_claimed);
    }

    [Fact]
    public async Task 店家在Neo4j沒有照片時image_url是null_其他資料照常回傳()
    {
        var (qrUid, storeUid, couponId) = await CreateQrCodeAsync();
        var gateway = new StubGateway { StoreUid = storeUid };
        int tourist = _api.NewUserId();

        HttpResponseMessage response = await ClientWith(gateway, ApiFactory.TokenWithRole(tourist, "Tourist")).GetAsync($"/api/qrcode/scan/{qrUid}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        QrCodeScanResponse scan = (await response.ReadResultAsync<QrCodeScanResponse>()).Result;
        Assert.Null(scan.image_url);
        Assert.Empty(scan.merchant_place.images);
        Assert.Equal(couponId, scan.coupon.coupon_id);
        Assert.True(scan.is_newly_claimed);   // 遊客登入掃描，同時領到優惠券
        Assert.Empty(scan.operating_hours);   // 沒有營業時間時是空陣列，不是 null
    }

    [Fact]
    public async Task 掃QRCode頂層也有營業時間_跟merchant_place同一份_依星期排序()
    {
        var (qrUid, storeUid, _) = await CreateQrCodeAsync();
        var gateway = new StubGateway
        {
            StoreUid = storeUid,
            Hours = { ("Saturday", "18:00", "02:00"), ("Tuesday", "17:00", "21:00"), ("Tuesday", "11:00", "14:00") }
        };

        HttpResponseMessage response = await ClientWith(gateway).GetAsync($"/api/qrcode/scan/{qrUid}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        QrCodeScanResponse scan = (await response.ReadResultAsync<QrCodeScanResponse>()).Result;
        Assert.Equal(
            new[] { "Tuesday 11:00-14:00", "Tuesday 17:00-21:00", "Saturday 18:00-02:00" },
            scan.operating_hours.Select(h => $"{h.day_of_week} {h.open_time}-{h.close_time}"));
        Assert.Equal(
            scan.operating_hours.Select(h => $"{h.day_of_week} {h.open_time}-{h.close_time}"),
            scan.merchant_place.operating_hours.Select(h => $"{h.day_of_week} {h.open_time}-{h.close_time}"));
    }
}
