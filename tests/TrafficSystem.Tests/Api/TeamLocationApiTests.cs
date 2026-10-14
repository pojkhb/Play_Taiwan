// 隊友位置：POST /api/Pair/Story/{story_id}/Location（回報自己的位置並拿到隊友的）、GET /api/Pair/Story/{story_id}/Locations
using System.Net;
using System.Net.Http.Json;
using Allure.Net.Commons.Attributes;
using backend.ViewModels;

namespace TrafficSystem.Tests.Api;

[Collection(ApiCollection.Name)]
[AllureSuiteHierarchy("協作解謎", "API 整合測試")]
[AllureBddHierarchy("協作解謎", "API 整合測試")]
public class TeamLocationApiTests
{
    private readonly ApiFactory _api;

    public TeamLocationApiTests(ApiFactory api) => _api = api;

    /// <summary>建立帳號（隊員名稱來自 auth），回傳 au_id</summary>
    private async Task<int> CreatePlayerAsync(string name)
    {
        string email = $"team{Guid.NewGuid():N}@example.com";
        await _api.ExecuteAsync(@"
            INSERT INTO auth (auth_name, auth_type, auth_email, auth_pswd, is_active, is_email_verified)
            VALUES (@name, 1, @email, 'x', 1, 1);", new { name, email });
        return await _api.QueryAsync<int>("SELECT au_id FROM auth WHERE auth_email = @email;", new { email });
    }

    /// <summary>建立兩人劇本，隊長產生配對碼、隊員輸入配對碼加入，回傳 (story_id, 隊長, 隊員)</summary>
    private async Task<(int storyId, int host, int member)> CreateTeamAsync()
    {
        int host = await CreatePlayerAsync("隊長小明");
        int member = await CreatePlayerAsync("隊員小華");
        int storyId = await _api.CreateStoryAsync(host, "臺中市", night: false, $"隊友位置景點{Guid.NewGuid():N}");
        await _api.ExecuteAsync("UPDATE story SET party_size = 2 WHERE s_id = @storyId;", new { storyId });

        ApiResult<PairSessionResponse> code = await (await _api.ClientFor(host).PostAsJsonAsync("/api/Pair/Code", new { story_id = storyId })).ReadResultAsync<PairSessionResponse>();
        Assert.True(code.isSuccess, code.message);
        ApiResult<PairSessionResponse> joined = await (await _api.ClientFor(member).PostAsJsonAsync("/api/Pair/Join", new { pair_code = code.Result.pair_code })).ReadResultAsync<PairSessionResponse>();
        Assert.True(joined.isSuccess, joined.message);

        return (storyId, host, member);
    }

    private async Task<TeamLocationsResponse> ReportAsync(int user, int storyId, object location)
    {
        HttpResponseMessage response = await _api.ClientFor(user).PostAsJsonAsync($"/api/Pair/Story/{storyId}/Location", location);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.ReadResultAsync<TeamLocationsResponse>()).Result;
    }

    private async Task<TeamLocationsResponse> LocationsAsync(int user, int storyId) =>
        (await (await _api.ClientFor(user).GetAsync($"/api/Pair/Story/{storyId}/Locations")).ReadResultAsync<TeamLocationsResponse>()).Result;

    [Fact]
    public async Task 回報自己的位置後_隊友看得到我在哪()
    {
        var (storyId, host, member) = await CreateTeamAsync();

        // 隊長先回報：隊員還沒回報過，沒有位置
        TeamLocationsResponse first = await ReportAsync(host, storyId, new { lat = 24.1378, lng = 120.6835, accuracy = 12.5, heading = 90 });

        Assert.Equal(storyId, first.story_id);
        Assert.Equal(60, first.online_within_seconds);
        Assert.Equal(new[] { host, member }, first.members.Select(m => m.au_id));   // 依座位排序
        Assert.Equal(new[] { "隊長小明", "隊員小華" }, first.members.Select(m => m.auth_name));
        Assert.Equal(new[] { true, false }, first.members.Select(m => m.is_me));
        Assert.Equal(new[] { true, false }, first.members.Select(m => m.is_host));
        Assert.Equal((24.1378, 120.6835), (first.members[0].lat, first.members[0].lng));
        Assert.Null(first.members[1].lat);
        Assert.Null(first.members[1].seconds_ago);
        Assert.False(first.members[1].is_online);

        // 隊員回報：同一趟就拿到隊長的位置
        TeamLocationsResponse second = await ReportAsync(member, storyId, new { lat = 24.1423, lng = 120.6787 });

        TeamMemberLocation hostSeen = second.members.Single(m => m.au_id == host);
        Assert.False(hostSeen.is_me);
        Assert.Equal((24.1378, 120.6835, 12.5, 90), (hostSeen.lat, hostSeen.lng, hostSeen.accuracy, hostSeen.heading));
        Assert.True(hostSeen.is_online);
        Assert.InRange(hostSeen.seconds_ago.Value, 0, 30);
        Assert.NotNull(hostSeen.updated_at);

        // 隊長只看、不回報：看得到隊員，自己的位置不變
        TeamLocationsResponse seenByHost = await LocationsAsync(host, storyId);
        Assert.Equal((24.1423, 120.6787), (seenByHost.members[1].lat, seenByHost.members[1].lng));
        Assert.Null(seenByHost.members[1].accuracy);   // 沒帶就沒有
        Assert.Equal(24.1378, seenByHost.members[0].lat);

        // 再回報一次：位置更新成最新的
        await ReportAsync(member, storyId, new { lat = 24.1430, lng = 120.6790 });
        Assert.Equal(24.1430, (await LocationsAsync(host, storyId)).members[1].lat);
    }

    [Fact]
    public async Task 沒有隊伍的劇本_只有擁有者自己()
    {
        int owner = await CreatePlayerAsync("獨行俠");
        int storyId = await _api.CreateStoryAsync(owner, "臺中市", night: false, $"單人景點{Guid.NewGuid():N}");

        TeamLocationsResponse result = await ReportAsync(owner, storyId, new { lat = 24.15, lng = 120.66 });

        TeamMemberLocation me = Assert.Single(result.members);
        Assert.Equal((owner, "獨行俠", 1, true, true), (me.au_id, me.auth_name, me.seat_no, me.is_host, me.is_me));
        Assert.Equal(24.15, me.lat);
    }

    [Fact]
    public async Task 沒有參與的人不能回報也看不到位置()
    {
        var (storyId, host, _) = await CreateTeamAsync();
        await ReportAsync(host, storyId, new { lat = 24.1378, lng = 120.6835 });
        HttpClient stranger = _api.ClientFor(await CreatePlayerAsync("路人"));

        HttpResponseMessage report = await stranger.PostAsJsonAsync($"/api/Pair/Story/{storyId}/Location", new { lat = 24.1, lng = 120.6 });
        HttpResponseMessage read = await stranger.GetAsync($"/api/Pair/Story/{storyId}/Locations");

        Assert.Equal(HttpStatusCode.Forbidden, report.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, read.StatusCode);
        Assert.DoesNotContain("24.1378", await read.Content.ReadAsStringAsync());
        Assert.Equal(2, (await LocationsAsync(host, storyId)).members.Count);   // 路人沒有被加進名單
    }

    [Fact]
    public async Task 隊員退出後_位置不會再被隊友看到()
    {
        var (storyId, host, member) = await CreateTeamAsync();
        await ReportAsync(member, storyId, new { lat = 24.1423, lng = 120.6787 });

        await _api.ClientFor(member).PostAsJsonAsync("/api/Pair/Leave", new { story_id = storyId });

        TeamLocationsResponse result = await LocationsAsync(host, storyId);
        Assert.Equal(new[] { host }, result.members.Select(m => m.au_id));
        Assert.Equal(HttpStatusCode.Forbidden, (await _api.ClientFor(member).GetAsync($"/api/Pair/Story/{storyId}/Locations")).StatusCode);
    }

    [Fact]
    public async Task 位置不合理_找不到劇本_沒登入()
    {
        var (storyId, host, _) = await CreateTeamAsync();
        HttpClient client = _api.ClientFor(host);

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/Pair/Story/{storyId}/Location", new { lat = 124.1, lng = 120.6 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/Pair/Story/{storyId}/Location", new { })).StatusCode);   // 沒帶位置
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/Pair/Story/999999/Locations")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _api.ClientFor(null).GetAsync($"/api/Pair/Story/{storyId}/Locations")).StatusCode);
    }
}
