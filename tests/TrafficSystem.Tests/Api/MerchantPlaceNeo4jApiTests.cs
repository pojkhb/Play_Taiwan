// 商家自建景點在 Neo4j 跟政府資料的景點一樣：註冊時依 category 加類型標籤、掛細分類（sub_category），
// 節點上帶名稱、介紹與介紹的語意向量（embedding 服務 tools/embedding_service，測試由 FakeAi 的 /embed 回應）；
// 商家改店名或介紹時，節點上的名稱、介紹與向量跟著更新
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using backend.Services.Neo4j;
using backend.ViewModels;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Allure.Net.Commons.Attributes;

namespace TrafficSystem.Tests.Api;

[Collection(ApiCollection.Name)]
[AllureSuiteHierarchy("商家與優惠券", "API 整合測試")]
[AllureBddHierarchy("商家與優惠券", "API 整合測試")]
public class MerchantPlaceNeo4jApiTests
{
    private readonly ApiFactory _api;

    public MerchantPlaceNeo4jApiTests(ApiFactory api) => _api = api;

    /// <summary>
    /// 假的 Neo4j：細分類清單固定是 SubCategories；記下建立自建景點、更新節點名稱介紹、改分類、停用與還原時收到的參數。
    /// 查景點目前資料時回傳一個景點（標籤、細分類、名稱、介紹為 Labels、Categories、Name、Description，預設是自建的餐廳）。
    /// </summary>
    private class PlaceGateway : INeo4jGatewayService
    {
        public string StoreUid { get; init; } = Guid.NewGuid().ToString();
        public List<object> Labels { get; init; } = new() { "Place", "MerchantPlace", "Restaurant" };
        public List<object> Categories { get; init; } = new();
        public string Name { get; init; }
        public string Description { get; init; }
        public bool HasEmbedding { get; init; }

        public Dictionary<string, List<string>> SubCategories { get; } = new()
        {
            ["景點"] = new() { "文化類", "其他" },
            ["餐廳"] = new() { "中式料理", "甜點冰品", "其他" },
            ["住宿"] = new() { "民宿", "一般旅館" },
            ["活動"] = new() { "一般活動" }
        };

        public List<string> Queries { get; } = new();
        public Dictionary<string, object> Created { get; private set; }
        public Dictionary<string, object> TextUpdate { get; private set; }
        public Dictionary<string, object> CategoryUpdate { get; private set; }
        public List<object> Deactivated { get; } = new();
        public List<object> Reactivated { get; } = new();

        private static Dictionary<string, object> Props(object parameters) =>
            parameters.GetType().GetProperties().ToDictionary(p => p.Name, p => p.GetValue(parameters));

        public Task<List<Dictionary<string, object>>> ExecuteCypherAsync(string query, object parameters = null)
        {
            Queries.Add(query);
            var rows = new List<Dictionary<string, object>>();

            if (query.Contains("MATCH (m:MainCategory)"))
            {
                rows.AddRange(SubCategories.Select(kv => new Dictionary<string, object>
                {
                    ["main_category"] = kv.Key,
                    ["sub_categories"] = kv.Value.Cast<object>().ToList()
                }));
            }
            else if (query.Contains("CREATE (a:Place:MerchantPlace"))
            {
                Created = Props(parameters);
                rows.Add(new Dictionary<string, object> { ["uid"] = StoreUid });
            }
            else if (query.Contains("MATCH (t:Town)"))
            {
                rows.Add(new Dictionary<string, object> { ["town_id"] = "台中市_西區", ["town"] = "西區", ["city"] = "台中市" });
            }
            else if (query.Contains("identityLabels"))
            {
                rows.Add(new Dictionary<string, object>
                {
                    ["uid"] = StoreUid,
                    ["identityLabels"] = Labels,
                    ["categories"] = Categories,
                    ["govName"] = Name,
                    ["govDescription"] = Description
                });
            }
            else if (query.Contains("AS has_embedding"))
            {
                rows.Add(new Dictionary<string, object> { ["has_embedding"] = HasEmbedding });
            }
            else if (query.Contains("SET a.description_embedding = $embedding"))
            {
                TextUpdate = Props(parameters);
            }
            else if (query.Contains("a.inactive_category = $category"))
            {
                CategoryUpdate = Props(parameters);
            }
            else if (query.Contains("a.deactivated_at = datetime()"))
            {
                Deactivated.Add(Props(parameters)["uid"]);
            }
            else if (query.Contains("WHERE a.deactivated_at IS NOT NULL"))
            {
                Reactivated.Add(Props(parameters)["uid"]);
            }

            return Task.FromResult(rows);
        }
    }

    private HttpClient ClientWith(PlaceGateway gateway, string token = null)
    {
        HttpClient client = _api.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddScoped<INeo4jGatewayService>(_ => gateway))).CreateClient();
        if (token != null)
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static object RegisterBody(string email, string category = null, string subCategory = null, string storeDec = null) => new
    {
        auth_name = "新商家" + email.Split('@')[0], auth_email = email, auth_pswd = "pw123456", store_name = "阿嬤芒果冰", store_dec = storeDec,
        store_city = "臺中市", store_town = "西區", store_address = "三民路一段198號", lat = 24.14, lng = 120.67,
        category, sub_category = subCategory
    };

    private static string NewEmail() => $"neo{Guid.NewGuid():N}@example.com";

    /// <summary>這次測試送到 embedding 服務的文字</summary>
    private List<string> EmbeddedTextsSince(int before) => _api.FakeAi.Requests.Where(r => r.Path == "/embed").Skip(before)
        .SelectMany(r => JsonDocument.Parse(r.Fields["body"]).RootElement.GetProperty("texts").EnumerateArray().Select(t => t.GetString()))
        .ToList();

    private int EmbedRequestCount() => _api.FakeAi.Requests.Count(r => r.Path == "/embed");

    [Fact]
    public async Task 註冊建立新景點_節點帶類型標籤細分類名稱介紹與介紹的語意向量()
    {
        var gateway = new PlaceGateway();
        int before = EmbedRequestCount();

        var result = await (await ClientWith(gateway).PostAsJsonAsync("/api/Merchant/Register",
            RegisterBody(NewEmail(), "restaurant", " 甜點冰品 ", "夏天限定愛文芒果雪花冰"))).ReadResultAsync<MerchantRegisterResponse>();

        Assert.True(result.isSuccess, result.message);
        Assert.Equal(gateway.StoreUid, result.Result.store_uid);

        string create = Assert.Single(gateway.Queries, q => q.Contains("CREATE (a:Place:MerchantPlace"));
        Assert.Contains("SET a:$(", create);   // 類型標籤
        Assert.Contains("[:HAS_CATEGORY]", create);
        Assert.Equal("Restaurant", gateway.Created["category"]);       // 分類統一成類型標籤的寫法
        Assert.Equal("甜點冰品", gateway.Created["subCategory"]);
        Assert.Equal("阿嬤芒果冰", gateway.Created["name"]);
        Assert.Equal("夏天限定愛文芒果雪花冰", gateway.Created["description"]);
        Assert.Equal(FakeAiService.FakeEmbedding, (List<double>)gateway.Created["embedding"]);
        Assert.Equal(new[] { "夏天限定愛文芒果雪花冰" }, EmbeddedTextsSince(before));   // 跟資料清洗一樣用介紹原文算向量

        Assert.Equal("Restaurant", await _api.QueryAsync<string>(
            "SELECT MIN(place_category) FROM place_type WHERE place_id = @uid;", new { uid = gateway.StoreUid }));
    }

    [Theory]
    [InlineData(null, "Restaurant", "其他")]
    [InlineData("Attraction", "Attraction", "其他")]
    [InlineData("Event", "Event", "一般活動")]
    [InlineData("Hotel", "Hotel", null)]
    public async Task 註冊沒帶細分類_用分類的預設細分類_沒填介紹時用店名算向量(string category, string expectedCategory, string expectedSubCategory)
    {
        var gateway = new PlaceGateway();
        int before = EmbedRequestCount();

        var result = await (await ClientWith(gateway).PostAsJsonAsync("/api/Merchant/Register",
            RegisterBody(NewEmail(), category))).ReadResultAsync<MerchantRegisterResponse>();

        Assert.True(result.isSuccess, result.message);
        Assert.Equal(expectedCategory, gateway.Created["category"]);
        Assert.Equal(expectedSubCategory, gateway.Created["subCategory"]);
        Assert.Equal(new[] { "阿嬤芒果冰" }, EmbeddedTextsSince(before));
        Assert.DoesNotContain(gateway.Queries, q => q.Contains("MATCH (m:MainCategory)"));   // 用預設值不用查細分類
    }

    [Fact]
    public async Task 註冊旅宿帶細分類_也掛同名的旅宿等級()
    {
        var gateway = new PlaceGateway();

        var result = await (await ClientWith(gateway).PostAsJsonAsync("/api/Merchant/Register",
            RegisterBody(NewEmail(), "Hotel", "民宿", "老宅改建的民宿"))).ReadResultAsync<MerchantRegisterResponse>();

        Assert.True(result.isSuccess, result.message);
        Assert.Equal("民宿", gateway.Created["subCategory"]);
        Assert.Contains("[:HAS_CLASS]", Assert.Single(gateway.Queries, q => q.Contains("CREATE (a:Place:MerchantPlace")));
    }

    [Theory]
    [InlineData("Hotel", "中式料理")]
    [InlineData("Restaurant", "不存在的分類")]
    public async Task 細分類不屬於該分類回400_不會建立帳號與景點(string category, string subCategory)
    {
        var gateway = new PlaceGateway();
        string email = NewEmail();
        int before = EmbedRequestCount();

        HttpResponseMessage response = await ClientWith(gateway).PostAsJsonAsync("/api/Merchant/Register",
            RegisterBody(email, category, subCategory, "介紹"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("register/categories", (await response.ReadResultAsync()).message);
        Assert.Null(gateway.Created);
        Assert.Equal(before, EmbedRequestCount());
        Assert.Equal(0, await _api.QueryAsync<int>("SELECT COUNT(1) FROM auth WHERE auth_email = @email;", new { email }));
    }

    [Fact]
    public async Task 帳號名稱或Email重複回409_不會建立景點()
    {
        string email = NewEmail();
        var first = await (await ClientWith(new PlaceGateway()).PostAsJsonAsync("/api/Merchant/Register", RegisterBody(email))).ReadResultAsync();
        Assert.True(first.isSuccess, first.message);
        string name = await _api.QueryAsync<string>("SELECT auth_name FROM auth WHERE auth_email = @email;", new { email });

        // 同一個名稱（大小寫不同也算）、不同 Email
        var sameName = new PlaceGateway();
        string otherEmail = NewEmail();
        var body = new
        {
            auth_name = name.ToUpperInvariant(), auth_email = otherEmail, auth_pswd = "pw123456", store_name = "阿嬤芒果冰",
            store_city = "臺中市", store_town = "西區", store_address = "三民路一段198號", lat = 24.14, lng = 120.67
        };
        HttpResponseMessage nameTaken = await ClientWith(sameName).PostAsJsonAsync("/api/Merchant/Register", body);
        Assert.Equal(HttpStatusCode.Conflict, nameTaken.StatusCode);
        Assert.Contains("名稱", (await nameTaken.ReadResultAsync()).message);
        Assert.Null(sameName.Created);
        Assert.Equal(0, await _api.QueryAsync<int>("SELECT COUNT(1) FROM auth WHERE auth_email = @otherEmail;", new { otherEmail }));

        // 同一個 Email
        var sameEmail = new PlaceGateway();
        HttpResponseMessage emailTaken = await ClientWith(sameEmail).PostAsJsonAsync("/api/Merchant/Register", RegisterBody(email.ToUpperInvariant()));
        Assert.Equal(HttpStatusCode.Conflict, emailTaken.StatusCode);
        Assert.Null(sameEmail.Created);
    }

    [Fact]
    public async Task embedding服務沒開_註冊照常成功_先不寫向量()
    {
        var gateway = new PlaceGateway();
        _api.FakeAi.EmbeddingAvailable = false;
        try
        {
            var result = await (await ClientWith(gateway).PostAsJsonAsync("/api/Merchant/Register",
                RegisterBody(NewEmail(), "Restaurant", "中式料理", "古早味炒飯"))).ReadResultAsync<MerchantRegisterResponse>();

            Assert.True(result.isSuccess, result.message);
            Assert.Null(gateway.Created["embedding"]);
            Assert.Equal("古早味炒飯", gateway.Created["description"]);
        }
        finally
        {
            _api.FakeAi.EmbeddingAvailable = true;
        }
    }

    [Fact]
    public async Task 查詢自建景點可選的分類與細分類()
    {
        var gateway = new PlaceGateway();

        var result = await (await ClientWith(gateway).GetAsync("/api/merchant/register/categories"))
            .ReadResultAsync<List<MerchantPlaceCategoryItem>>();

        Assert.True(result.isSuccess, result.message);
        Assert.Equal(new[] { "Attraction", "Restaurant", "Hotel", "Event" }, result.Result.Select(c => c.category));
        MerchantPlaceCategoryItem restaurant = result.Result[1];
        Assert.Equal("餐廳", restaurant.name);
        Assert.Equal("其他", restaurant.default_sub_category);
        Assert.Equal(new[] { "中式料理", "甜點冰品", "其他" }, restaurant.sub_categories);
        Assert.Null(result.Result[2].default_sub_category);
    }

    /// <summary>建立綁定自建景點的商家，回傳 (au_id, s_id)</summary>
    private async Task<(int auId, int sId)> CreateStoreAsync(string storeUid, string storeName, string storeDec)
    {
        string id = Guid.NewGuid().ToString("N")[..10];
        await _api.ExecuteAsync(@"
            INSERT INTO auth (auth_name, auth_type, auth_email, auth_pswd, is_active, is_email_verified)
            VALUES (@name, 2, @email, 'x', 1, 1);", new { name = $"商家{id}", email = $"text{id}@example.com" });
        int auId = await _api.QueryAsync<int>("SELECT au_id FROM auth WHERE auth_email = @email;", new { email = $"text{id}@example.com" });
        await _api.ExecuteAsync(
            "INSERT INTO store (au_id, store_name, store_dec, store_city, store_uid) VALUES (@auId, @storeName, @storeDec, '臺中市', @storeUid);",
            new { auId, storeName, storeDec, storeUid });
        int sId = await _api.QueryAsync<int>("SELECT s_id FROM store WHERE au_id = @auId;", new { auId });
        return (auId, sId);
    }

    [Fact]
    public async Task 修改介紹_自建景點節點的名稱介紹與向量跟著更新()
    {
        var gateway = new PlaceGateway { Name = "阿嬤芒果冰", Description = "舊的介紹", HasEmbedding = true };
        var (auId, sId) = await CreateStoreAsync(gateway.StoreUid, "阿嬤芒果冰", "舊的介紹");
        int before = EmbedRequestCount();

        var result = await (await ClientWith(gateway, ApiFactory.TokenWithRole(auId, "Merchant", sId))
            .PutAsJsonAsync("/api/Merchant/Profile", new { store_dec = "冬天賣芒果乾與熱豆花" })).ReadResultAsync();

        Assert.True(result.isSuccess, result.message);
        Assert.Equal(gateway.StoreUid, gateway.TextUpdate["uid"]);
        Assert.Equal("阿嬤芒果冰", gateway.TextUpdate["name"]);
        Assert.Equal("冬天賣芒果乾與熱豆花", gateway.TextUpdate["description"]);
        Assert.Equal(FakeAiService.FakeEmbedding, (List<double>)gateway.TextUpdate["embedding"]);
        Assert.Equal(new[] { "冬天賣芒果乾與熱豆花" }, EmbeddedTextsSince(before));
    }

    [Fact]
    public async Task 名稱介紹沒改而且已經有向量_不重算()
    {
        var gateway = new PlaceGateway { Name = "阿嬤芒果冰", Description = "玉井在地三代經營", HasEmbedding = true };
        var (auId, sId) = await CreateStoreAsync(gateway.StoreUid, "阿嬤芒果冰", "玉井在地三代經營");
        int before = EmbedRequestCount();

        var result = await (await ClientWith(gateway, ApiFactory.TokenWithRole(auId, "Merchant", sId))
            .PutAsJsonAsync("/api/Merchant/Profile", new { store_name = "阿嬤芒果冰", image_url = "https://cdn.store.test/ice.jpg" })).ReadResultAsync();

        Assert.True(result.isSuccess, result.message);
        Assert.Null(gateway.TextUpdate);
        Assert.Equal(before, EmbedRequestCount());
    }

    [Fact]
    public async Task 名稱介紹沒改但還沒有向量_修改資料時補算()
    {
        var gateway = new PlaceGateway { Name = "阿嬤芒果冰", Description = "玉井在地三代經營", HasEmbedding = false };
        var (auId, sId) = await CreateStoreAsync(gateway.StoreUid, "阿嬤芒果冰", "玉井在地三代經營");

        var result = await (await ClientWith(gateway, ApiFactory.TokenWithRole(auId, "Merchant", sId))
            .PutAsJsonAsync("/api/Merchant/Profile", new { image_url = "https://cdn.store.test/ice.jpg" })).ReadResultAsync();

        Assert.True(result.isSuccess, result.message);
        Assert.Equal(FakeAiService.FakeEmbedding, (List<double>)gateway.TextUpdate["embedding"]);
    }

    #region 修改分類

    /// <summary>建立綁定自建景點的商家，並寫入註冊時會有的 place_type（分類 placeCategory），回傳 (au_id, s_id)</summary>
    private async Task<(int auId, int sId)> CreateSelfBuiltStoreAsync(string storeUid, string placeCategory = "Restaurant")
    {
        var store = await CreateStoreAsync(storeUid, "阿嬤芒果冰", "玉井在地三代經營");
        int placeTypeId = await _api.QueryAsync<int>("SELECT COALESCE(MAX(place_type_id), 0) + 1 FROM place_type;");
        await _api.ExecuteAsync(@"
            INSERT INTO place_type (place_type_id, place_id, place_name, place_category, type_id)
            VALUES (@placeTypeId, @storeUid, '阿嬤芒果冰', @placeCategory, 6);", new { placeTypeId, storeUid, placeCategory });
        return store;
    }

    private Task<string> PlaceCategoryInMySqlAsync(string storeUid) =>
        _api.QueryAsync<string>("SELECT MIN(place_category) FROM place_type WHERE place_id = @storeUid;", new { storeUid });

    private async Task<ApiResult<JsonElement>> UpdateProfileAsync(PlaceGateway gateway, int auId, int sId, object body) =>
        await (await ClientWith(gateway, ApiFactory.TokenWithRole(auId, "Merchant", sId)).PutAsJsonAsync("/api/Merchant/Profile", body)).ReadResultAsync();

    [Fact]
    public async Task 修改分類與細分類_換掉Neo4j的類型標籤與細分類_place_type也一起改()
    {
        var gateway = new PlaceGateway { Name = "阿嬤芒果冰", Description = "玉井在地三代經營", HasEmbedding = true };
        var (auId, sId) = await CreateSelfBuiltStoreAsync(gateway.StoreUid);

        var result = await UpdateProfileAsync(gateway, auId, sId, new { category = "hotel", sub_category = "民宿" });

        Assert.True(result.isSuccess, result.message);
        Assert.Equal(gateway.StoreUid, gateway.CategoryUpdate["uid"]);
        Assert.Equal("Hotel", gateway.CategoryUpdate["category"]);
        Assert.Equal("民宿", gateway.CategoryUpdate["subCategory"]);
        Assert.Equal("Hotel", await PlaceCategoryInMySqlAsync(gateway.StoreUid));
    }

    [Theory]
    [InlineData(null, "中式料理", "Restaurant", "中式料理")]   // 只改細分類：沿用目前的分類
    [InlineData("Event", null, "Event", "一般活動")]          // 換分類沒帶細分類：用新分類的預設細分類
    [InlineData("Hotel", null, "Hotel", null)]
    public async Task 只帶分類或細分類_另一個用目前的分類或預設細分類(string category, string subCategory, string expectedCategory, string expectedSubCategory)
    {
        var gateway = new PlaceGateway { Name = "阿嬤芒果冰", Description = "玉井在地三代經營", HasEmbedding = true };
        var (auId, sId) = await CreateSelfBuiltStoreAsync(gateway.StoreUid);

        var result = await UpdateProfileAsync(gateway, auId, sId, new { category, sub_category = subCategory });

        Assert.True(result.isSuccess, result.message);
        Assert.Equal(expectedCategory, gateway.CategoryUpdate["category"]);
        Assert.Equal(expectedSubCategory, gateway.CategoryUpdate["subCategory"]);
        Assert.Equal(expectedCategory, await PlaceCategoryInMySqlAsync(gateway.StoreUid));
    }

    [Fact]
    public async Task 帶目前的分類沒帶細分類_不改分類()
    {
        var gateway = new PlaceGateway { Name = "阿嬤芒果冰", Description = "玉井在地三代經營", HasEmbedding = true };
        var (auId, sId) = await CreateSelfBuiltStoreAsync(gateway.StoreUid);

        var result = await UpdateProfileAsync(gateway, auId, sId, new { store_name = "阿嬤芒果冰", category = "Restaurant" });

        Assert.True(result.isSuccess, result.message);
        Assert.Null(gateway.CategoryUpdate);
    }

    [Fact]
    public async Task 修改時細分類不屬於分類回400_店家資料不會被改到()
    {
        var gateway = new PlaceGateway { Name = "阿嬤芒果冰", Description = "玉井在地三代經營", HasEmbedding = true };
        var (auId, sId) = await CreateSelfBuiltStoreAsync(gateway.StoreUid);

        HttpResponseMessage response = await ClientWith(gateway, ApiFactory.TokenWithRole(auId, "Merchant", sId))
            .PutAsJsonAsync("/api/Merchant/Profile", new { store_name = "不該被改到", category = "Hotel", sub_category = "中式料理" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(gateway.CategoryUpdate);
        Assert.Equal("阿嬤芒果冰", await _api.QueryAsync<string>("SELECT store_name FROM store WHERE s_id = @sId;", new { sId }));
        Assert.Equal("Restaurant", await PlaceCategoryInMySqlAsync(gateway.StoreUid));
    }

    [Fact]
    public async Task 綁定既有景點的商家帶分類_不會改政府資料()
    {
        var gateway = new PlaceGateway { Labels = new() { "Place", "Restaurant" } };
        var (auId, sId) = await CreateStoreAsync(gateway.StoreUid, "綁定的小吃店", null);

        var result = await UpdateProfileAsync(gateway, auId, sId, new { category = "Hotel", sub_category = "民宿" });

        Assert.True(result.isSuccess, result.message);
        Assert.Null(gateway.CategoryUpdate);
    }

    [Fact]
    public async Task 查店家資料_回傳自建景點目前的分類與細分類()
    {
        var gateway = new PlaceGateway { Categories = new() { "甜點冰品" } };
        var (auId, sId) = await CreateSelfBuiltStoreAsync(gateway.StoreUid);

        var profile = await (await ClientWith(gateway, ApiFactory.TokenWithRole(auId, "Merchant", sId))
            .GetAsync("/api/Merchant/Profile")).ReadResultAsync<MerchantProfileResponse>();

        Assert.True(profile.isSuccess, profile.message);
        Assert.Equal("Restaurant", profile.Result.category);
        Assert.Equal("甜點冰品", profile.Result.sub_category);
    }

    #endregion

    #region 管理員停用（軟刪除）

    private HttpClient AdminClient(PlaceGateway gateway) => ClientWith(gateway, ApiFactory.TokenWithRole(_api.NewUserId(), "Admin"));

    private async Task<object> AdminUserBody(int auId, bool isActive) => new
    {
        auth_name = await _api.QueryAsync<string>("SELECT auth_name FROM auth WHERE au_id = @auId;", new { auId }),
        auth_email = await _api.QueryAsync<string>("SELECT auth_email FROM auth WHERE au_id = @auId;", new { auId }),
        auth_type = 2,
        is_active = isActive,
        is_email_verified = true
    };

    [Fact]
    public async Task 管理員停用商家_自建景點在Neo4j軟刪除_啟用後還原()
    {
        var gateway = new PlaceGateway();
        var (auId, _) = await CreateSelfBuiltStoreAsync(gateway.StoreUid);

        var deleted = await (await AdminClient(gateway).DeleteAsync($"/api/Admin/Users/{auId}")).ReadResultAsync();

        Assert.True(deleted.isSuccess, deleted.message);
        Assert.Equal(new object[] { gateway.StoreUid }, gateway.Deactivated);
        Assert.Empty(gateway.Reactivated);

        var restored = await (await AdminClient(gateway).PutAsJsonAsync($"/api/Admin/Users/{auId}", await AdminUserBody(auId, true))).ReadResultAsync();

        Assert.True(restored.isSuccess, restored.message);
        Assert.Equal(new object[] { gateway.StoreUid }, gateway.Reactivated);
    }

    [Fact]
    public async Task 管理員用修改API停用商家_自建景點也軟刪除()
    {
        var gateway = new PlaceGateway();
        var (auId, _) = await CreateSelfBuiltStoreAsync(gateway.StoreUid);

        var result = await (await AdminClient(gateway).PutAsJsonAsync($"/api/Admin/Users/{auId}", await AdminUserBody(auId, false))).ReadResultAsync();

        Assert.True(result.isSuccess, result.message);
        Assert.Equal(new object[] { gateway.StoreUid }, gateway.Deactivated);
    }

    [Fact]
    public async Task 同一個景點還有其他啟用中的商家_停用其中一間不會軟刪除景點()
    {
        var gateway = new PlaceGateway();
        var (auId, _) = await CreateSelfBuiltStoreAsync(gateway.StoreUid);
        await CreateStoreAsync(gateway.StoreUid, "同一個景點的另一間店", null);

        var result = await (await AdminClient(gateway).DeleteAsync($"/api/Admin/Users/{auId}")).ReadResultAsync();

        Assert.True(result.isSuccess, result.message);
        Assert.Empty(gateway.Deactivated);
    }

    [Fact]
    public async Task 停用遊客帳號_不會動Neo4j()
    {
        var gateway = new PlaceGateway();
        string email = $"tourist{Guid.NewGuid():N}@example.com";
        await _api.ExecuteAsync(@"
            INSERT INTO auth (auth_name, auth_type, auth_email, auth_pswd, is_active, is_email_verified)
            VALUES ('遊客', 1, @email, 'x', 1, 1);", new { email });
        int auId = await _api.QueryAsync<int>("SELECT au_id FROM auth WHERE auth_email = @email;", new { email });

        var result = await (await AdminClient(gateway).DeleteAsync($"/api/Admin/Users/{auId}")).ReadResultAsync();

        Assert.True(result.isSuccess, result.message);
        Assert.Empty(gateway.Queries);
    }

    #endregion
}
