// 店家照片網址：商家在店家資料（PUT /api/Merchant/Profile、POST /api/Merchant/Register）填 image_url，
// 存在 Neo4j 版本鏈的 :Current 版本節點；遊客掃 QR Code 領券、我的優惠券的 image_url 優先用這張，沒填才用景點照片
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using backend.Services.Neo4j;
using backend.ViewModels;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Allure.Net.Commons.Attributes;

namespace TrafficSystem.Tests.Api;

[Collection(ApiCollection.Name)]
[AllureSuiteHierarchy("商家與優惠券", "API 整合測試")]
[AllureBddHierarchy("商家與優惠券", "API 整合測試")]
public class MerchantStoreImageApiTests
{
    private const string GovPhoto = "https://neo4j-img.test/gov-front.jpg";
    private const string StorePhoto = "https://cdn.store.test/front.jpg";

    private readonly ApiFactory _api;

    public MerchantStoreImageApiTests(ApiFactory api) => _api = api;

    /// <summary>
    /// 假的 Neo4j：記住景點目前的 :Current 版本節點，寫版本節點（CreateNewVersionAsync、CreateMerchantPlaceAsync）時換成新的一顆。
    /// 景點照片固定是 PlaceImages；查店家照片時跟真的查詢一樣讀 :Current 的 image_url。
    /// </summary>
    private class StoreGateway : INeo4jGatewayService
    {
        public string StoreUid { get; init; }
        public List<object> Labels { get; init; } = new() { "Place", "Restaurant" };   // 預設綁定政府資料的景點
        public List<string> PlaceImages { get; init; } = new();

        /// <summary>目前 :Current 版本節點的屬性；沒有版本節點時為 null</summary>
        public Dictionary<string, object> Current { get; private set; }

        /// <summary>寫入版本節點時收到的參數</summary>
        public List<Dictionary<string, object>> VersionWrites { get; } = new();

        private static Dictionary<string, object> Props(object parameters) =>
            parameters.GetType().GetProperties().ToDictionary(p => p.Name, p => p.GetValue(parameters));

        private void WriteVersion(object parameters)
        {
            Dictionary<string, object> p = Props(parameters);
            VersionWrites.Add(p);
            // Neo4j 不會存值是 null 的屬性
            Current = new Dictionary<string, object>
            {
                ["name"] = p["name"], ["address"] = p["address"], ["description"] = p["description"],
                ["phone"] = p["phone"], ["website"] = p["website"], ["image_url"] = p["imageUrl"]
            }.Where(kv => kv.Value != null).ToDictionary(kv => kv.Key, kv => kv.Value);
        }

        public Task<List<Dictionary<string, object>>> ExecuteCypherAsync(string query, object parameters = null)
        {
            var rows = new List<Dictionary<string, object>>();

            if (query.Contains("identityLabels"))
            {
                rows.Add(new Dictionary<string, object>
                {
                    ["uid"] = StoreUid,
                    ["identityLabels"] = Labels,
                    ["images"] = PlaceImages.Select(url => (object)new Dictionary<string, object> { ["url"] = url, ["description"] = null }).ToList(),
                    ["overrideData"] = Current
                });
            }
            else if (query.Contains("SET old:Historical"))
            {
                WriteVersion(parameters);
            }
            else if (query.Contains("CREATE (a:Place:MerchantPlace"))
            {
                WriteVersion(parameters);
                rows.Add(new Dictionary<string, object> { ["uid"] = StoreUid });
            }
            else if (query.Contains("MATCH (t:Town)"))
            {
                rows.Add(new Dictionary<string, object> { ["town_id"] = "台中市_西區", ["town"] = "西區", ["city"] = "台中市" });
            }
            else if (query.Contains("v.image_url"))
            {
                if (Current?.GetValueOrDefault("image_url") is string url)
                    rows.Add(new Dictionary<string, object> { ["uid"] = StoreUid, ["store_image_url"] = url });
            }
            else if (query.Contains("collect(img.url)[0]") && PlaceImages.Count > 0)
            {
                rows.Add(new Dictionary<string, object> { ["uid"] = StoreUid, ["image_url"] = PlaceImages.OrderBy(u => u, StringComparer.Ordinal).First() });
            }

            return Task.FromResult(rows);
        }
    }

    /// <summary>建立綁定景點的商家、優惠券與 QR Code 貼紙，回傳 (au_id, s_id, qr_uid)</summary>
    private async Task<(int auId, int sId, string qrUid)> CreateStoreAsync(string storeUid)
    {
        string id = Guid.NewGuid().ToString("N")[..10];
        string qrUid = $"QR-{id}";

        await _api.ExecuteAsync(@"
            INSERT INTO auth (auth_name, auth_type, auth_email, auth_pswd, is_active, is_email_verified)
            VALUES (@name, 2, @email, 'x', 1, 1);", new { name = $"商家{id}", email = $"photo{id}@example.com" });
        int auId = await _api.QueryAsync<int>("SELECT au_id FROM auth WHERE auth_email = @email;", new { email = $"photo{id}@example.com" });
        await _api.ExecuteAsync("INSERT INTO store (au_id, store_name, store_city, store_uid) VALUES (@auId, @name, '臺中市', @storeUid);",
            new { auId, name = $"照片小吃{id}", storeUid });
        int sId = await _api.QueryAsync<int>("SELECT s_id FROM store WHERE au_id = @auId;", new { auId });

        await _api.ExecuteAsync(@"
            INSERT INTO coupon (s_id, coupon_code, coupon_name, discount_commodity, discount_type, discount_value, status)
            VALUES (@sId, @code, '滷肉飯折 10 元', '滷肉飯', 'amount', 10, 'active');", new { sId, code = $"C{id}" });
        int couponId = await _api.QueryAsync<int>("SELECT coupon_id FROM coupon WHERE s_id = @sId;", new { sId });
        await _api.ExecuteAsync("INSERT INTO qrcode_coupon (qr_uid, coupon_id, used_count) VALUES (@qrUid, @couponId, 0);", new { qrUid, couponId });

        return (auId, sId, qrUid);
    }

    /// <summary>Neo4j 換成假的那一份的 HttpClient；token 為 null 時不登入</summary>
    private HttpClient ClientWith(StoreGateway gateway, string token = null)
    {
        HttpClient client = _api.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddScoped<INeo4jGatewayService>(_ => gateway))).CreateClient();
        if (token != null)
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<QrCodeScanResponse> ScanAsync(HttpClient client, string qrUid)
    {
        HttpResponseMessage response = await client.GetAsync($"/api/qrcode/scan/{qrUid}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.ReadResultAsync<QrCodeScanResponse>()).Result;
    }

    private static async Task UpdateProfileAsync(HttpClient merchant, object body)
    {
        var result = await (await merchant.PutAsJsonAsync("/api/Merchant/Profile", body)).ReadResultAsync();
        Assert.True(result.isSuccess, result.message);
    }

    [Fact]
    public async Task 店家資料填了照片網址_遊客掃QRCode領券與我的優惠券都拿到這張()
    {
        string storeUid = Guid.NewGuid().ToString();
        var (auId, sId, qrUid) = await CreateStoreAsync(storeUid);
        var gateway = new StoreGateway { StoreUid = storeUid, PlaceImages = { GovPhoto } };
        HttpClient merchant = ClientWith(gateway, ApiFactory.TokenWithRole(auId, "Merchant", sId));

        // 還沒填：用景點照片
        Assert.Equal(GovPhoto, (await ScanAsync(ClientWith(gateway), qrUid)).image_url);

        await UpdateProfileAsync(merchant, new { image_url = $"  {StorePhoto}  " });

        Assert.Equal(StorePhoto, Assert.Single(gateway.VersionWrites)["imageUrl"]);   // 去掉前後空白後寫進版本節點

        int tourist = _api.NewUserId();
        QrCodeScanResponse scan = await ScanAsync(ClientWith(gateway, ApiFactory.TokenWithRole(tourist, "Tourist")), qrUid);
        Assert.True(scan.is_newly_claimed);
        Assert.Equal(StorePhoto, scan.image_url);
        Assert.Equal(StorePhoto, scan.merchant_place.merchant_override["image_url"].ToString());

        var mine = await (await ClientWith(gateway, ApiFactory.TokenWithRole(tourist, "Tourist")).GetAsync("/api/coupons"))
            .ReadResultAsync<List<MyCouponResponse>>();
        Assert.Equal(StorePhoto, Assert.Single(mine.Result).image_url);

        var profile = await (await merchant.GetAsync("/api/Merchant/Profile")).ReadResultAsync<MerchantProfileResponse>();
        Assert.Equal(StorePhoto, profile.Result.image_url);
    }

    [Fact]
    public async Task 沒帶image_url沿用原本的_帶空字串就清掉改回景點照片()
    {
        string storeUid = Guid.NewGuid().ToString();
        var (auId, sId, qrUid) = await CreateStoreAsync(storeUid);
        var gateway = new StoreGateway { StoreUid = storeUid, PlaceImages = { GovPhoto } };
        HttpClient merchant = ClientWith(gateway, ApiFactory.TokenWithRole(auId, "Merchant", sId));

        await UpdateProfileAsync(merchant, new { image_url = StorePhoto });
        await UpdateProfileAsync(merchant, new { store_name = "阿嬤的芒果冰" });

        Assert.Equal(StorePhoto, gateway.VersionWrites[1]["imageUrl"]);   // 只改店名，照片沿用
        Assert.Equal(StorePhoto, (await ScanAsync(ClientWith(gateway), qrUid)).image_url);

        await UpdateProfileAsync(merchant, new { image_url = "" });

        Assert.Null(gateway.VersionWrites[2]["imageUrl"]);
        Assert.Equal(GovPhoto, (await ScanAsync(ClientWith(gateway), qrUid)).image_url);
        var profile = await (await merchant.GetAsync("/api/Merchant/Profile")).ReadResultAsync<MerchantProfileResponse>();
        Assert.Null(profile.Result.image_url);
    }

    [Fact]
    public async Task 自建景點也能填照片網址()
    {
        string storeUid = Guid.NewGuid().ToString();
        var (auId, sId, qrUid) = await CreateStoreAsync(storeUid);
        var gateway = new StoreGateway { StoreUid = storeUid, Labels = { "Place", "MerchantPlace" } };   // 自建景點沒有景點照片

        Assert.Null((await ScanAsync(ClientWith(gateway), qrUid)).image_url);

        await UpdateProfileAsync(ClientWith(gateway, ApiFactory.TokenWithRole(auId, "Merchant", sId)), new { image_url = StorePhoto });

        Assert.Equal(StorePhoto, (await ScanAsync(ClientWith(gateway), qrUid)).image_url);
    }

    [Theory]
    [InlineData("not a url")]
    [InlineData("ftp://cdn.store.test/front.jpg")]
    [InlineData("/uploads/store/front.jpg")]
    public async Task image_url不是http網址回400_其他欄位也不會被改到(string imageUrl)
    {
        string storeUid = Guid.NewGuid().ToString();
        var (auId, sId, _) = await CreateStoreAsync(storeUid);
        var gateway = new StoreGateway { StoreUid = storeUid };
        string nameBefore = await _api.QueryAsync<string>("SELECT store_name FROM store WHERE s_id = @sId;", new { sId });

        HttpResponseMessage response = await ClientWith(gateway, ApiFactory.TokenWithRole(auId, "Merchant", sId))
            .PutAsJsonAsync("/api/Merchant/Profile", new { store_name = "不該被改到", image_url = imageUrl });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(gateway.VersionWrites);
        Assert.Equal(nameBefore, await _api.QueryAsync<string>("SELECT store_name FROM store WHERE s_id = @sId;", new { sId }));
    }

    private static object RegisterBody(string email, string placeUid = null, string imageUrl = null, bool newPlace = false) => newPlace
        ? new
        {
            auth_name = "新商家" + email.Split('@')[0], auth_email = email, auth_pswd = "pw123456", store_name = "新開的小吃店",
            store_city = "臺中市", store_town = "西區", store_address = "三民路一段198號", lat = 24.14, lng = 120.67, image_url = imageUrl
        }
        : new { auth_name = "新商家" + email.Split('@')[0], auth_email = email, auth_pswd = "pw123456", store_name = "綁定的小吃店", place_uid = placeUid, image_url = imageUrl };

    [Fact]
    public async Task 註冊選既有景點時帶照片網址_補一顆版本節點()
    {
        var gateway = new StoreGateway { StoreUid = Guid.NewGuid().ToString(), PlaceImages = { GovPhoto } };

        var result = await (await ClientWith(gateway).PostAsJsonAsync("/api/Merchant/Register",
            RegisterBody($"reg{Guid.NewGuid():N}@example.com", gateway.StoreUid, StorePhoto))).ReadResultAsync<MerchantRegisterResponse>();

        Assert.True(result.isSuccess, result.message);
        Dictionary<string, object> written = Assert.Single(gateway.VersionWrites);
        Assert.Equal(StorePhoto, written["imageUrl"]);
        Assert.Equal("綁定的小吃店", written["name"]);
        Assert.Equal(result.Result.s_id.ToString(), written["submittedBy"]);

        var profile = await (await ClientWith(gateway, ApiFactory.TokenWithRole(result.Result.au_id, "Merchant", result.Result.s_id))
            .GetAsync("/api/Merchant/Profile")).ReadResultAsync<MerchantProfileResponse>();
        Assert.Equal(StorePhoto, profile.Result.image_url);
    }

    [Fact]
    public async Task 註冊選既有景點沒帶照片網址_不建版本節點()
    {
        var gateway = new StoreGateway { StoreUid = Guid.NewGuid().ToString() };

        var result = await (await ClientWith(gateway).PostAsJsonAsync("/api/Merchant/Register",
            RegisterBody($"reg{Guid.NewGuid():N}@example.com", gateway.StoreUid))).ReadResultAsync<MerchantRegisterResponse>();

        Assert.True(result.isSuccess, result.message);
        Assert.Empty(gateway.VersionWrites);
    }

    [Fact]
    public async Task 註冊建立新景點時帶照片網址_寫在第一顆版本節點()
    {
        var gateway = new StoreGateway { StoreUid = Guid.NewGuid().ToString(), Labels = { "Place", "MerchantPlace" } };

        var result = await (await ClientWith(gateway).PostAsJsonAsync("/api/Merchant/Register",
            RegisterBody($"reg{Guid.NewGuid():N}@example.com", imageUrl: StorePhoto, newPlace: true))).ReadResultAsync<MerchantRegisterResponse>();

        Assert.True(result.isSuccess, result.message);
        Assert.Equal(StorePhoto, Assert.Single(gateway.VersionWrites)["imageUrl"]);
    }

    [Fact]
    public async Task 註冊時照片網址格式不對回400_不會建立帳號()
    {
        var gateway = new StoreGateway { StoreUid = Guid.NewGuid().ToString() };
        string email = $"reg{Guid.NewGuid():N}@example.com";

        HttpResponseMessage response = await ClientWith(gateway).PostAsJsonAsync("/api/Merchant/Register", RegisterBody(email, gateway.StoreUid, "not a url"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(gateway.VersionWrites);
        Assert.Equal(0, await _api.QueryAsync<int>("SELECT COUNT(1) FROM auth WHERE auth_email = @email;", new { email }));
    }
}
