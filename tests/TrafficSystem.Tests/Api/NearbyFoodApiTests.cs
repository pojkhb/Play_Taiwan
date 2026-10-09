// 用餐時間推播：GET /api/Map/NearbyFood（附近美食、優惠券店家優先、沒營業的不列出）
using System.Globalization;
using System.Net;
using Allure.Net.Commons.Attributes;
using backend.Models;

namespace TrafficSystem.Tests.Api;

[Collection(ApiCollection.Name)]
[AllureSuiteHierarchy("解謎地圖與迷霧", "API 整合測試")]
[AllureBddHierarchy("解謎地圖與迷霧", "API 整合測試")]
public class NearbyFoodApiTests
{
    private readonly ApiFactory _api;

    public NearbyFoodApiTests(ApiFactory api)
    {
        _api = api;
        _api.FakeAi.Reset();   // 預設模擬 AI service 的 Neo4j 連不上 → 只用 MySQL
    }

    // 每個測試用不同的中心點（彼此相距 20 公里以上），互不干擾；附近也沒有其他測試建立的景點
    private static (double lat, double lng) Center(int n) => (22.1 + n * 0.2, 120.2);

    private const double MetersPerDegree = 111320.0;

    /// <summary>在中心點正北方 meters 公尺建立一間店；category 為 null 時不設分類</summary>
    private async Task<string> AddPlaceAsync((double lat, double lng) center, double meters, string category, string openTime)
    {
        string name = $"美食測試{Guid.NewGuid():N}";
        await _api.ExecuteAsync(@"
            INSERT INTO place (p_name, p_category, p_type, p_address, p_latitude, p_longitude, p_open_time)
            VALUES (@name, @category, '小吃', '高雄市測試路 1 號', @lat, @lng, @openTime);",
            new { name, category, lat = center.lat + meters / MetersPerDegree, lng = center.lng, openTime });
        return name;
    }

    /// <summary>把店家綁成商家（place_type 標成 Restaurant、建立 store），並上架一張優惠券</summary>
    private async Task<string> GiveCouponAsync(string placeName)
    {
        string uid = Guid.NewGuid().ToString();
        int nextTypeRow = await _api.QueryAsync<int>("SELECT COALESCE(MAX(place_type_id), 0) + 1 FROM place_type;");
        await _api.ExecuteAsync(@"
            INSERT INTO place_type (place_type_id, place_id, place_name, place_category, type_id)
            VALUES (@id, @uid, @placeName, 'Restaurant', 4);", new { id = nextTypeRow, uid, placeName });
        await _api.ExecuteAsync(@"
            INSERT INTO store (au_id, store_name, store_uid) VALUES (@au, @placeName, @uid);",
            new { au = _api.NewUserId(), placeName, uid });
        int storeId = await _api.QueryAsync<int>("SELECT MAX(s_id) FROM store WHERE store_uid = @uid;", new { uid });
        await _api.ExecuteAsync(@"
            INSERT INTO coupon (s_id, coupon_code, coupon_name, discount_commodity, discount_type, discount_value, status)
            VALUES (@storeId, 'LUNCH10', '午餐九折', '全品項', 'percent', 10, 'active');", new { storeId });
        return uid;
    }

    private static string ClosedNow()
    {
        DateTime now = DateTime.UtcNow.AddHours(8);
        return $"{now.AddHours(2):HH:mm}-{now.AddHours(3):HH:mm}";   // 兩小時後才開，現在一定沒營業
    }

    private async Task<NearbyFoodResponse> GetAsync((double lat, double lng) c, string extra = "")
    {
        ApiResult<NearbyFoodResponse> result = await (await _api.ClientFor(_api.NewUserId())
            .GetAsync($"/api/Map/NearbyFood?lat={c.lat.ToString(CultureInfo.InvariantCulture)}&lng={c.lng.ToString(CultureInfo.InvariantCulture)}{extra}")).ReadResultAsync<NearbyFoodResponse>();
        Assert.True(result.isSuccess, result.message);
        return result.Result;
    }

    [Fact]
    public async Task 附近美食_有優惠券的排前面_其餘由近到遠_沒營業與太遠的不列出()
    {
        var c = Center(0);
        string near = await AddPlaceAsync(c, 200, "飲食", "00:00-24:00");
        string withCoupon = await AddPlaceAsync(c, 500, "飲食", "00:00-24:00");
        string unknownHours = await AddPlaceAsync(c, 300, "飲食", null);
        string closed = await AddPlaceAsync(c, 100, "飲食", ClosedNow());
        string tooFar = await AddPlaceAsync(c, 2000, "飲食", "00:00-24:00");
        string notFood = await AddPlaceAsync(c, 50, "其他", "00:00-24:00");
        string uid = await GiveCouponAsync(withCoupon);

        NearbyFoodResponse food = await GetAsync(c);

        Assert.Equal(new[] { withCoupon, near, unknownHours }, food.places.Select(p => p.name));
        Assert.Equal(uid, food.places[0].place_id);
        Assert.Equal("午餐九折", Assert.Single(food.places[0].coupons).coupon_name);
        Assert.Empty(food.places[1].coupons);
        Assert.Equal(new bool?[] { true, true, null }, food.places.Select(p => p.is_open_now));
        Assert.InRange(food.places[1].distance_m, 190, 210);
        Assert.StartsWith("https://www.google.com/maps/", food.places[1].maps_deeplink_url);
        Assert.Equal(800, food.radius_m);
        Assert.Contains(food.meal, new[] { "早餐", "午餐", "下午茶", "晚餐", "宵夜" });
        Assert.Contains($"「{withCoupon}」", food.message);
        Assert.Contains("優惠券", food.message);
        Assert.DoesNotContain(food.places, p => p.name == closed || p.name == tooFar || p.name == notFood);
    }

    [Fact]
    public async Task 半徑放大就找得到比較遠的店()
    {
        var c = Center(1);
        string far = await AddPlaceAsync(c, 2000, "飲食", "00:00-24:00");

        Assert.Empty((await GetAsync(c)).places);
        Assert.Equal(far, Assert.Single((await GetAsync(c, "&radius_m=2500")).places).name);
    }

    [Fact]
    public async Task 附近沒有美食時回傳空清單與提示文字()
    {
        NearbyFoodResponse food = await GetAsync(Center(2));

        Assert.Empty(food.places);
        Assert.Contains("暫時沒有找到美食", food.message);
    }

    [Fact]
    public async Task Neo4j的餐廳與MySQL的同一間會合併成一筆()
    {
        var c = Center(3);
        string name = await AddPlaceAsync(c, 400, "飲食", "00:00-24:00");
        string today = DateTime.UtcNow.AddHours(8).DayOfWeek.ToString();
        _api.FakeAi.Neo4jCypherResponse = new
        {
            status = "success",
            count = 2,
            data = new object[]
            {
                new { uid = "neo-food-1", name = "政府資料的麵店", address = "高雄市測試路 9 號", lat = c.lat + 250 / MetersPerDegree, lon = c.lng, distance_m = 250.0,
                      image_url = "https://img.test/noodle.jpg", hours = new[] { new { day_of_week = today, open_time = "00:00", close_time = "23:59" } } },
                new { uid = "neo-food-2", name, address = (string)null, lat = c.lat + 400 / MetersPerDegree, lon = c.lng, distance_m = 400.0,
                      image_url = (string)null, hours = Array.Empty<object>() }
            }
        };

        NearbyFoodResponse food = await GetAsync(c);

        Assert.Equal(new[] { "政府資料的麵店", name }, food.places.Select(p => p.name));
        Assert.Equal("https://img.test/noodle.jpg", food.places[0].image_url);
        Assert.True(food.places[0].is_open_now);
        Assert.Equal("00:00-23:59", food.places[0].open_time);
        Assert.Equal("高雄市測試路 1 號", food.places[1].address);   // Neo4j 沒有的資料用 MySQL 補上
        Assert.True(food.places[1].is_open_now);                       // 營業時間也用 MySQL 補上
    }

    [Fact]
    public async Task 沒帶位置回傳400_沒登入回傳401()
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await _api.ClientFor(_api.NewUserId()).GetAsync("/api/Map/NearbyFood")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _api.ClientFor(null).GetAsync("/api/Map/NearbyFood?lat=22.6&lng=120.3")).StatusCode);
    }
}
