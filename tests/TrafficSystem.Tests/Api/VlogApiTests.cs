// 商家 / 遊客 VLOG API：不需要 AI 服務就能驗證的部分（選項、輸入檢查、權限）
using System.Net.Http.Json;
using backend.ViewModels;
using Allure.Net.Commons.Attributes;

namespace TrafficSystem.Tests.Api;

[Collection(ApiCollection.Name)]
[AllureSuiteHierarchy("VLOG 與回顧", "API 整合測試")]
[AllureBddHierarchy("VLOG 與回顧", "API 整合測試")]
public class VlogApiTests
{
    private readonly ApiFactory _api;

    public VlogApiTests(ApiFactory api) => _api = api;

    [Fact]
    public async Task 商家VLOG可以取得敘事語氣選項()
    {
        List<NarrativeToneItem> tones =
            (await (await _api.MerchantClientFor(_api.NewUserId()).GetAsync("/api/MerchantVlog/Tones")).ReadResultAsync<List<NarrativeToneItem>>()).Result;

        Assert.Equal(3, tones.Count);
        Assert.Contains(tones, t => t.nt_name == "溫情走心");
    }

    [Fact]
    public async Task 商家VLOG沒填推廣資訊不能產生草稿()
    {
        using var form = new MultipartFormDataContent
        {
            { new StringContent("阿嬤的芒果冰"), "store_name" },
            { new StringContent("11:00–21:00"), "open_time" },
        };

        HttpResponseMessage response = await _api.MerchantClientFor(_api.NewUserId()).PostAsync("/api/MerchantVlog/Preview", form);

        ApiResult<string> result = await response.ReadResultAsync<string>();
        Assert.False(result.isSuccess);
        Assert.Contains("推廣資訊", result.message);
    }

    [Fact]
    public async Task 沒玩過的劇本不能做遊客VLOG()
    {
        int owner = _api.NewUserId(), stranger = _api.NewUserId();
        int storyId = await _api.CreateStoryAsync(owner, "臺北市", night: false, "台北101");

        HttpResponseMessage response = await _api.ClientFor(stranger).PostAsJsonAsync("/api/VisitorVlog/Preview", new { story_id = storyId });

        ApiResult<string> result = await response.ReadResultAsync<string>();
        Assert.False(result.isSuccess);
        Assert.Equal("你還沒有玩過這個劇本", result.message);
    }

    [Fact]
    public async Task 還沒產生遊客VLOG時查詢狀態會提示先產生草稿()
    {
        int user = _api.NewUserId();
        int storyId = await _api.CreateStoryAsync(user, "臺北市", night: false, "台北101");

        ApiResult<string> result = await (await _api.ClientFor(user).GetAsync($"/api/VisitorVlog/Status/{storyId}")).ReadResultAsync<string>();

        Assert.False(result.isSuccess);
        Assert.Contains("還沒有 VLOG", result.message);
    }
}
