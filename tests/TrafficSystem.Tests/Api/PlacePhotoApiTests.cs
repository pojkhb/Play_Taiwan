// 景點照片直接用景點 ID 向 Neo4j 拿（PlaceLookupService.GetPlaceImagesAsync），Neo4j 沒有照片或連不上時才用 MySQL place.p_image。
// 地圖、NPC 互動、劇本詳情、喜愛清單、過往紀錄、回顧都走同一個入口；還沒抵達的站給的是這張照片的迷霧圖。
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using backend.Models;
using backend.Services.Neo4j;
using backend.utils;
using backend.ViewModels;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Allure.Net.Commons.Attributes;

namespace TrafficSystem.Tests.Api;

[Collection(ApiCollection.Name)]
[AllureSuiteHierarchy("劇本", "API 整合測試")]
[AllureBddHierarchy("劇本", "API 整合測試")]
public class PlacePhotoApiTests
{
    private readonly ApiFactory _api;

    public PlacePhotoApiTests(ApiFactory api)
    {
        _api = api;
        _api.FakeNeo4j.Reset();
    }

    /// <summary>
    /// 假的 Neo4j：查照片（HAS_IMAGE）時回傳指定的照片；其他查詢（例如座標）回傳空的，讓後端改用 MySQL。
    /// Down = true 時模擬連不上。
    /// </summary>
    private class StubGateway : INeo4jGatewayService
    {
        public Dictionary<string, string> Images { get; init; } = new();
        public bool Down { get; init; }
        public List<string> ImageQueries { get; } = new();

        public Task<List<Dictionary<string, object>>> ExecuteCypherAsync(string query, object parameters = null)
        {
            if (Down) throw new InvalidOperationException("Neo4j 連不上");
            if (!query.Contains("HAS_IMAGE")) return Task.FromResult(new List<Dictionary<string, object>>());

            ImageQueries.Add(query);
            var uids = (IEnumerable<string>)parameters.GetType().GetProperty("uids").GetValue(parameters);
            return Task.FromResult(uids
                .Where(Images.ContainsKey)
                .Select(uid => new Dictionary<string, object> { ["uid"] = uid, ["image_url"] = Images[uid] })
                .ToList());
        }
    }

    private PlaceLookupService LookupWith(StubGateway gateway) =>
        new(gateway, Options.Create(new AppSettings { mydb = _api.ConnectionString }), NullLogger<PlaceLookupService>.Instance);

    /// <summary>Neo4j 換成假的那一份的 HttpClient</summary>
    private HttpClient ClientWith(StubGateway gateway, int user)
    {
        HttpClient client = _api.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddScoped<INeo4jGatewayService>(_ => gateway))).CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiFactory.Token(user));
        return client;
    }

    private async Task<List<string>> PlaceIdsAsync(int storyId)
    {
        using var conn = new MySql.Data.MySqlClient.MySqlConnection(_api.ConnectionString);
        return (await Dapper.SqlMapper.QueryAsync<string>(conn,
            "SELECT place_id FROM story_node WHERE s_id = @storyId ORDER BY sn_order;", new { storyId })).ToList();
    }

    /// <summary>一張 Neo4j 的景點照片網址（內容由 FakeAi 提供，背景做迷霧圖時會下載）</summary>
    private string Neo4jPhoto()
    {
        string url = $"https://neo4j-img.test/{Guid.NewGuid():N}.jpg";
        _api.FakeAi.Images[url] = ApiFactory.TinyPng;
        return url;
    }

    /// <summary>建立 3 站的劇本：第 1、2 站 Neo4j 有照片；第 2 站 MySQL 也有照片（要以 Neo4j 為準）；第 3 站只有 MySQL 有</summary>
    private async Task<(int storyId, List<string> uids, string[] neo4j, string mysqlOnly, StubGateway gateway)> CreateStoryAsync(int user)
    {
        string[] places = Enumerable.Range(1, 3).Select(i => $"照片景點{i}_{Guid.NewGuid():N}").ToArray();
        int storyId = await _api.CreateStoryAsync(user, "臺北市", night: false, places);
        List<string> uids = await PlaceIdsAsync(storyId);

        string[] neo4j = { Neo4jPhoto(), Neo4jPhoto() };
        await _api.GivePlacePhotoAsync(places[1]);
        string mysqlOnly = await _api.GivePlacePhotoAsync(places[2]);

        var gateway = new StubGateway { Images = { [uids[0]] = neo4j[0], [uids[1]] = neo4j[1] } };
        return (storyId, uids, neo4j, mysqlOnly, gateway);
    }

    [Fact]
    public async Task 用景點ID向Neo4j拿照片_Neo4j沒有的才用MySQL()
    {
        var (_, uids, neo4j, mysqlOnly, gateway) = await CreateStoryAsync(_api.NewUserId());

        Dictionary<string, string> images = await LookupWith(gateway).GetPlaceImagesAsync(uids.Append("沒有這個景點").Append(""));

        Assert.Equal(new Dictionary<string, string> { [uids[0]] = neo4j[0], [uids[1]] = neo4j[1], [uids[2]] = mysqlOnly }, images);

        // 一個景點有多張照片時固定取同一張（迷霧圖是依照片網址對應的）
        string query = Assert.Single(gateway.ImageQueries);
        Assert.Contains("ORDER BY img.url", query);
        Assert.Contains("p.uid IN $uids", query);
    }

    [Fact]
    public async Task Neo4j連不上時_照片全部改用MySQL()
    {
        var (_, uids, _, mysqlOnly, _) = await CreateStoryAsync(_api.NewUserId());

        Dictionary<string, string> images = await LookupWith(new StubGateway { Down = true }).GetPlaceImagesAsync(uids);

        Assert.Equal(new[] { uids[1], uids[2] }, images.Keys.OrderBy(k => uids.IndexOf(k)));   // 第 1 站 MySQL 沒有照片
        Assert.Equal(mysqlOnly, images[uids[2]]);
    }

    [Fact]
    public async Task 劇本詳情_地圖_NPC互動_喜愛清單_過往紀錄_回顧_都用Neo4j的景點照片()
    {
        int user = _api.NewUserId();
        var (storyId, _, neo4j, mysqlOnly, gateway) = await CreateStoryAsync(user);
        HttpClient client = ClientWith(gateway, user);

        async Task<StoryDetailResponse> DetailAsync() =>
            (await (await client.GetAsync($"/api/Story/{storyId}/Detail")).ReadResultAsync<StoryDetailResponse>()).Result;
        async Task<FavoriteStoryItem> FavoriteAsync() =>
            Assert.Single((await (await client.GetAsync("/api/Story/Favorites")).ReadResultAsync<List<FavoriteStoryItem>>()).Result);

        // 還沒抵達：劇本詳情的每一站、喜愛清單的封面（第一站）、地圖、NPC 互動的背景都是迷霧圖
        await client.PostAsJsonAsync($"/api/Story/{storyId}/Favorite", new { is_favorite = true });
        Assert.All((await DetailAsync()).nodes, n => Assert.Contains("/images/fog/", n.image_url));
        Assert.Contains("/images/fog/", (await FavoriteAsync()).cover_image_url);

        MapResponse map = (await (await client.GetAsync($"/api/Map/{storyId}")).ReadResultAsync<MapResponse>()).Result;
        int first = map.nodes[0].node_id;
        Assert.All(map.nodes, n => Assert.Contains("/images/fog/", n.image_url));

        NpcInteractionResponse npc = (await (await client.GetAsync($"/api/Map/Node/{first}/Interact")).ReadResultAsync<NpcInteractionResponse>()).Result;
        Assert.Contains("/images/fog/", npc.scene_image_url);

        // 抵達第 1 站後：第 1 站才是 Neo4j 的照片，還沒到的站仍然是迷霧圖
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"/api/Map/Node/{first}/Arrive?lat=25.03&lng=121.5", null)).StatusCode);

        map = (await (await client.GetAsync($"/api/Map/{storyId}")).ReadResultAsync<MapResponse>()).Result;
        Assert.Equal(neo4j[0], map.nodes[0].image_url);
        npc = (await (await client.GetAsync($"/api/Map/Node/{first}/Interact")).ReadResultAsync<NpcInteractionResponse>()).Result;
        Assert.Equal(neo4j[0], npc.scene_image_url);

        StoryDetailResponse detail = await DetailAsync();
        Assert.Equal(neo4j[0], detail.nodes[0].image_url);
        Assert.All(detail.nodes.Skip(1), n => Assert.Contains("/images/fog/", n.image_url));
        Assert.Equal(neo4j[0], (await FavoriteAsync()).cover_image_url);

        // 每一站都抵達後：劇本詳情每一站都是景點照片
        await client.PostAsync($"/api/Map/Node/{map.nodes[1].node_id}/Arrive?lat=25.031&lng=121.5", null);
        await client.PostAsync($"/api/Map/Node/{map.nodes[2].node_id}/Arrive?lat=25.032&lng=121.5", null);
        Assert.Equal(new[] { neo4j[0], neo4j[1], mysqlOnly }, (await DetailAsync()).nodes.Select(n => n.image_url));

        // 過往紀錄（玩完才有）：玩家這趟沒拍照片，封面與上方的照片用每一站的景點照片
        await _api.ExecuteAsync("UPDATE story_session SET ss_status = 'completed', completed_at = NOW() WHERE s_id = @storyId AND au_id = @user;", new { storyId, user });

        List<HistoryStoryItem> history = (await (await client.GetAsync("/api/History")).ReadResultAsync<List<HistoryStoryItem>>()).Result;
        Assert.Equal(neo4j[0], Assert.Single(history).cover_image_url);

        HistoryStoryItem past = (await (await client.GetAsync($"/api/History/{storyId}")).ReadResultAsync<HistoryStoryItem>()).Result;
        Assert.Equal(new[] { neo4j[0], neo4j[1], mysqlOnly }, past.photos.Select(p => p.image_url));
        Assert.All(past.photos, p => Assert.True(p.is_place_photo));
        Assert.Equal(neo4j[0], past.cover_image_url);

        // 回顧（玩完才能看）：每一站的景點照片
        StoryRecapResponse recap = (await (await client.GetAsync($"/api/History/{storyId}/Recap")).ReadResultAsync<StoryRecapResponse>()).Result;
        Assert.Equal(new[] { neo4j[0], neo4j[1], mysqlOnly }, recap.nodes.Select(n => n.place_image_url));
    }
}
