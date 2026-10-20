// 遊你說了算（POST /api/Story/GenerateByText）：送出後先回 job_id，前端輪詢 GET /api/Story/GenerateByText/{job_id}，
// 後端每次查詢向 AI 問一次進度，AI 做完才在那次查詢存成劇本
using System.Net;
using System.Net.Http.Json;
using backend.Models;
using Allure.Net.Commons.Attributes;

namespace TrafficSystem.Tests.Api;

[Collection(ApiCollection.Name)]
[AllureSuiteHierarchy("劇本", "API 整合測試")]
[AllureBddHierarchy("劇本", "API 整合測試")]
public class GenerateByTextApiTests
{
    private const string PollPath = "/api/jobs/" + FakeAiService.TextJobId;

    private readonly ApiFactory _api;

    public GenerateByTextApiTests(ApiFactory api)
    {
        _api = api;
        _api.FakeAi.Reset();
    }

    private static object Running(string stage) => FakeAiService.TextJob("running", stage: stage);

    private static object Completed(string place1, string place2) => Completed(
        new Node(1, "uid-text-a", place1, "穹頂下的信", "開場"),
        new Node(2, "uid-text-b", place2, "古剎的香火", "開場"),
        synopsis: "沿著兩個地標找回卷軸。");

    private record Node(int order, string uid, string place, string title, string opening);

    private static object Intent(string city, string town) =>
        new { city_name = city, town_name = town, traveler_count = 2, preferences = new[] { "歷史人文" }, transportation = new[] { "步行" }, node_count = 2, is_night = false };

    /// <summary>AI 做完而且有劇本（outcome = generated），劇本在 result.blueprint</summary>
    private static object Completed(Node first, Node second, string synopsis, object parsedIntent = null, object selectedArea = null) => FakeAiService.TextJob("done", new
    {
        outcome = "generated",
        original_prompt = "兩個人想去台北中正區走走",
        parsed_intent = parsedIntent ?? Intent("臺北市", "中正區"),
        selected_area = selectedArea ?? new { city_name = "臺北市", town_name = "中正區" },
        retrieval_mode = "name_description_tag_keyword",
        locations = Array.Empty<object>(),
        blueprint = new
        {
            title = "城南的失落卷軸",
            preface = "一封沒有署名的信，把你帶到了城南。",
            synopsis,
            is_night_mode = false,
            nodes = new[] { first, second }.Select(n => new
            {
                node_order = n.order, spot_uuid = n.uid, place_name = n.place, location_codename = "白色穹頂", node_title = n.title,
                task_type = "文化問答型", task_description = "找出牌樓上的字", dialogues = new { opening = n.opening, success = "完成" }
            }).ToArray()
        },
        warnings = new[] { "本次使用關鍵字檢索，尚未使用向量語意搜尋。" }
    });

    /// <summary>AI 做完但沒產出劇本（找不到景點等），blueprint 是 null，message 是原因</summary>
    private static object NoBlueprint(string outcome, string message) => FakeAiService.TextJob("done", new
    {
        outcome,
        message,
        original_prompt = "想去看海",
        parsed_intent = Intent(null, null),
        locations = Array.Empty<object>(),
        blueprint = (object)null
    });

    private static async Task<ApiResult<GenerateByTextResult>> SubmitAsync(HttpClient client, string prompt) =>
        await (await client.PostAsJsonAsync("/api/Story/GenerateByText", new { user_prompt = prompt })).ReadResultAsync<GenerateByTextResult>();

    private static async Task<ApiResult<GenerateByTextResult>> PollAsync(HttpClient client) =>
        await (await client.GetAsync("/api/Story/GenerateByText/" + FakeAiService.TextJobId)).ReadResultAsync<GenerateByTextResult>();

    /// <summary>送出後一直輪詢到不是 processing 為止，其他測試要一份「遊你說算」劇本時用</summary>
    internal static async Task<ApiResult<GenerateByTextResult>> GenerateAsync(HttpClient client, string prompt)
    {
        ApiResult<GenerateByTextResult> result = await SubmitAsync(client, prompt);
        for (int i = 0; i < 20 && result.isSuccess && result.Result.status == GenerateByTextResult.Processing; i++)
            result = await PollAsync(client);
        return result;
    }

    [Fact]
    public async Task 送出後先回job_id_輪詢到AI完成才存成劇本_之後再查不會重複存()
    {
        _api.FakeAi.TextJobPolls.Enqueue(Running("正在解析使用者需求"));
        _api.FakeAi.TextJobPolls.Enqueue(Completed($"文字測試景點A{Guid.NewGuid():N}", $"文字測試景點B{Guid.NewGuid():N}"));
        int user = _api.NewUserId();
        HttpClient client = _api.ClientFor(user);

        ApiResult<GenerateByTextResult> submitted = await SubmitAsync(client, "兩個人想去台北中正區走走，看看古蹟");
        Assert.True(submitted.isSuccess, submitted.message);
        Assert.Equal(FakeAiService.TextJobId, submitted.Result.job_id);
        Assert.Equal("processing", submitted.Result.status);
        Assert.Null(submitted.Result.story_id);
        Assert.Equal(0, await _api.QueryAsync<int>("SELECT COUNT(*) FROM story WHERE au_id = @user;", new { user }));
        string sent = _api.FakeAi.Last("/api/api/admin/generate_script_blueprint_by_text").Fields["body"];
        Assert.Equal("兩個人想去台北中正區走走，看看古蹟", System.Text.Json.JsonDocument.Parse(sent).RootElement.GetProperty("user_prompt").GetString());

        ApiResult<GenerateByTextResult> generating = await PollAsync(client);
        Assert.Equal("processing", generating.Result.status);
        Assert.Equal("正在解析使用者需求", generating.Result.stage);

        ApiResult<GenerateByTextResult> done = await PollAsync(client);
        Assert.True(done.isSuccess, done.message);
        Assert.Equal("done", done.Result.status);
        Assert.Equal("臺北市", done.Result.detected_city);
        Assert.Equal("中正區", done.Result.detected_town);
        Assert.True(done.Result.story_id > 0);
        Assert.Equal(2, await _api.QueryAsync<int>("SELECT COUNT(*) FROM story_node WHERE s_id = @id;", new { id = done.Result.story_id }));
        // AI 解析出的旅遊人數存成劇本人數，2 人以上才能產生協作配對碼
        Assert.Equal(2, await _api.QueryAsync<int>("SELECT party_size FROM story WHERE s_id = @id;", new { id = done.Result.story_id }));

        ApiResult<GenerateByTextResult> again = await PollAsync(client);
        Assert.Equal("done", again.Result.status);
        Assert.Equal(done.Result.story_id, again.Result.story_id);
        Assert.Equal("城南的失落卷軸", again.Result.data.title);
        Assert.Equal(2, again.Result.data.nodes.Count);
        Assert.Equal(1, await _api.QueryAsync<int>("SELECT COUNT(*) FROM story WHERE au_id = @user;", new { user }));
        Assert.Equal(2, _api.FakeAi.Requests.Count(r => r.Path == PollPath));   // 存好之後不用再問 AI
    }

    [Fact]
    public async Task AI生成失敗時回傳失敗階段與原因()
    {
        _api.FakeAi.TextJobPolls.Enqueue(FakeAiService.TextJob("error", error: "任務生成失敗，請聯絡管理員並提供任務 ID"));
        HttpClient client = _api.ClientFor(_api.NewUserId());

        ApiResult<GenerateByTextResult> result = await GenerateAsync(client, "台北中正區");

        Assert.True(result.isSuccess, result.message);
        Assert.Equal("failed", result.Result.status);
        Assert.Equal("AI 劇本生成失敗（處理失敗）：任務生成失敗，請聯絡管理員並提供任務 ID", result.Result.error);
        Assert.Equal(result.Result.error, result.message);
        Assert.Equal("failed", (await PollAsync(client)).Result.status);   // 失敗記在資料庫，再查還是失敗
    }

    [Theory]
    [InlineData("no_candidates", "沒有找到符合目前需求關鍵字的景點；未使用隨機地點補足。")]
    [InlineData("needs_clarification", "未取得可用的旅遊需求關鍵字。請描述想看的景觀或想進行的活動。")]
    [InlineData("candidate_selection_required", "已找到符合需求的候選景點，但候選未共享同一縣市及行政區，或行政區資料不足。請先選擇目的地，再生成單一地區的劇本。")]
    public async Task AI做完但沒產出劇本_馬上回失敗_原因用AI的說明(string outcome, string message)
    {
        // 2026-10-19 AI 改版後，這些情況都是 status = done、blueprint = null；以前後端當成還在生成，前端白等 10 分鐘
        _api.FakeAi.TextJobPolls.Enqueue(NoBlueprint(outcome, message));
        int user = _api.NewUserId();
        HttpClient client = _api.ClientFor(user);

        await SubmitAsync(client, "想去看海");
        ApiResult<GenerateByTextResult> result = await PollAsync(client);

        Assert.True(result.isSuccess, result.message);
        Assert.Equal("failed", result.Result.status);
        Assert.Equal(message, result.Result.error);
        Assert.Equal(message, result.message);
        Assert.Null(result.Result.story_id);
        Assert.Equal(0, await _api.QueryAsync<int>("SELECT COUNT(*) FROM story WHERE au_id = @user;", new { user }));

        Assert.Equal("failed", (await PollAsync(client)).Result.status);   // 失敗記在資料庫，再查不用再問 AI
        Assert.Equal(1, _api.FakeAi.Requests.Count(r => r.Path == PollPath));
    }

    [Fact]
    public async Task AI說做完卻沒有結果_馬上回失敗不用等到逾時()
    {
        _api.FakeAi.TextJobPolls.Enqueue(FakeAiService.TextJob("done"));
        HttpClient client = _api.ClientFor(_api.NewUserId());

        ApiResult<GenerateByTextResult> result = await GenerateAsync(client, "台北中正區");

        Assert.Equal("failed", result.Result.status);
        Assert.Contains("沒有回傳劇本", result.Result.error);
    }

    [Fact]
    public async Task 使用者沒說縣市行政區_劇本存AI選定的縣市行政區()
    {
        _api.FakeAi.TextJobPolls.Enqueue(Completed(
            new Node(1, "uid-text-a", $"文字測試景點A{Guid.NewGuid():N}", "穹頂下的信", "開場"),
            new Node(2, "uid-text-b", $"文字測試景點B{Guid.NewGuid():N}", "古剎的香火", "開場"),
            synopsis: "沿著兩個地標找回卷軸。",
            parsedIntent: Intent(null, null),
            selectedArea: new { city_name = "臺南市", town_name = "安平區" }));

        ApiResult<GenerateByTextResult> result = await GenerateAsync(_api.ClientFor(_api.NewUserId()), "兩個人想去看古蹟");

        Assert.Equal("done", result.Result.status);
        Assert.Equal("臺南市", result.Result.detected_city);
        Assert.Equal("安平區", result.Result.detected_town);
        Assert.Null(result.Result.parsed_intent.city_name);   // 解析條件照 AI 原樣回傳
        var story = await _api.QueryAsync<(string city_name, string district_name)>(
            "SELECT city_name, district_name FROM story WHERE s_id = @id;", new { id = result.Result.story_id });
        Assert.Equal(("臺南市", "安平區"), story);
    }

    [Fact]
    public async Task 別人的job_id查不到進度()
    {
        await SubmitAsync(_api.ClientFor(_api.NewUserId()), "台北中正區");

        HttpResponseMessage response = await _api.ClientFor(_api.NewUserId()).GetAsync("/api/Story/GenerateByText/" + FakeAiService.TextJobId);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.False((await response.ReadResultAsync<string>()).isSuccess);
    }

    [Fact]
    public async Task AI重啟後找不到工作_回失敗請重新送出()
    {
        HttpClient client = _api.ClientFor(_api.NewUserId());
        await SubmitAsync(client, "台北中正區");
        _api.FakeAi.TextJobLost = true;

        ApiResult<GenerateByTextResult> result = await PollAsync(client);

        Assert.Equal("failed", result.Result.status);
        Assert.Contains("請重新送出", result.Result.error);
    }

    [Fact]
    public async Task Swagger的範例是實測AI能生成劇本的句子()
    {
        // 舊範例「去臺南安平區來趟深度文學解謎之旅」在 AI 只做關鍵字比對後一定找不到景點；這句 2026-10-20 實測兩次都生成 4 站
        string json = await (await _api.ClientFor(null).GetAsync("/swagger/v1/swagger.json")).Content.ReadAsStringAsync();
        System.Text.Json.JsonElement prompt = System.Text.Json.JsonDocument.Parse(json).RootElement
            .GetProperty("components").GetProperty("schemas").GetProperty("GenerateByTextRequest").GetProperty("properties").GetProperty("user_prompt");

        Assert.Equal("兩個人想去臺南中西區看古蹟，大約走四個站點", prompt.GetProperty("example").GetString());
    }

    [Fact]
    public async Task 超過10分鐘還沒做完就算失敗()
    {
        HttpClient client = _api.ClientFor(_api.NewUserId());
        await SubmitAsync(client, "台北中正區");
        await _api.ExecuteAsync("UPDATE story_text_job SET created_at = NOW() - INTERVAL 11 MINUTE WHERE job_id = @id;", new { id = FakeAiService.TextJobId });

        ApiResult<GenerateByTextResult> result = await PollAsync(client);

        Assert.Equal("failed", result.Result.status);
        Assert.Contains("超過 10 分鐘", result.Result.error);
    }

    [Fact]
    public async Task 用AI給的spot_uuid當place_id_LLM寫壞的罕用字換回Neo4j的正確名稱()
    {
        // 第 2 站是旅宿：舊的寫法只用名稱查 Neo4j 景點（:Attraction），旅宿、餐廳都查不到，place_id 會是 NULL
        _api.FakeNeo4j.Rows = new List<Dictionary<string, object>>
        {
            new() { ["uid"] = "uid-hotel-1", ["name"] = "簃居青衖", ["lat"] = 22.9931, ["lon"] = 120.1985, ["description"] = "老屋民宿" }
        };
        foreach (var (key, value) in HotelDetails("神農街1號"))
            _api.FakeNeo4j.Rows[0][key] = value;
        try
        {
            _api.FakeAi.TextJobPolls.Enqueue(Completed(
                new Node(1, "uid-attr-1", $"文字測試景點{Guid.NewGuid():N}", "穹頂下的信", "開場"),
                new Node(2, "uid-hotel-1", "�居青衖", "旅途�之間", "入住�居青衖，聽從古老的故事..."),
                synopsis: "從紅樓出發，最後住進�居青衖。"));

            ApiResult<GenerateByTextResult> result = await GenerateAsync(_api.ClientFor(_api.NewUserId()), "台北中正區住一晚");

            Assert.Equal("done", result.Result.status);
            Assert.Equal("簃居青衖", result.Result.data.nodes[1].place_name);
            int storyId = result.Result.story_id!.Value;
            Task<string> NodeColumn(int order, string column) =>
                _api.QueryAsync<string>($"SELECT {column} FROM story_node WHERE s_id = @storyId AND sn_order = @order;", new { storyId, order });
            Assert.Equal("uid-attr-1", await NodeColumn(1, "place_id"));   // Neo4j 查不到也照樣存 AI 給的 uid
            Assert.Equal("uid-hotel-1", await NodeColumn(2, "place_id"));
            Assert.Equal("旅途之間", await NodeColumn(2, "sn_title"));   // 對不回去的 � 直接拿掉
            Assert.Equal("入住簃居青衖，聽從古老的故事...", await NodeColumn(2, "sn_opening_text"));
            Assert.Equal("從紅樓出發，最後住進簃居青衖。", await _api.QueryAsync<string>("SELECT story_synopsis FROM story WHERE s_id = @storyId;", new { storyId }));
            Assert.Equal(22.9931, await _api.QueryAsync<double>("SELECT p_latitude FROM place WHERE p_name = '簃居青衖';"), 4);

            // place 表不再只有名稱和座標：Neo4j 查得到的分類、介紹、地址、營業時間一起寫進去
            var place = await _api.QueryAsync<(string region_id, string p_category, string p_type, string p_introduction, string p_address, string p_open_time)>(
                "SELECT region_id, p_category, p_type, p_introduction, p_address, p_open_time FROM place WHERE p_name = '簃居青衖';");
            Assert.Equal(("uid-hotel-1", "Hotel", "民宿", "老屋民宿", "神農街1號", "15:00-22:00"), place);
        }
        finally
        {
            _api.FakeNeo4j.Reset();   // 其他測試預設 Neo4j 連不上
        }
    }

    [Fact]
    public async Task 同一個景點已經在place表_不重複新增只補空欄位()
    {
        string uid = $"uid-hotel-{Guid.NewGuid():N}";
        string oldName = $"舊名民宿{Guid.NewGuid():N}"[..12], newName = $"新名民宿{Guid.NewGuid():N}"[..12];
        await _api.ExecuteAsync("INSERT INTO place (region_id, p_name, p_latitude, p_longitude, p_summary) VALUES (@uid, @oldName, 22.99, 120.2, '原本的介紹');",
            new { uid, oldName });

        var row = new Dictionary<string, object> { ["uid"] = uid, ["name"] = newName, ["lat"] = 23.5, ["lon"] = 121.0, ["description"] = "老屋民宿" };
        foreach (var (key, value) in HotelDetails("神農街2號"))
            row[key] = value;
        _api.FakeNeo4j.Rows = new List<Dictionary<string, object>> { row };
        try
        {
            _api.FakeAi.TextJobPolls.Enqueue(Completed(
                new Node(1, "uid-attr-1", $"文字測試景點{Guid.NewGuid():N}", "穹頂下的信", "開場"),
                new Node(2, uid, newName, "旅途之間", "開場"),
                synopsis: "最後住進老屋。"));

            ApiResult<GenerateByTextResult> result = await GenerateAsync(_api.ClientFor(_api.NewUserId()), "台南住一晚");

            Assert.Equal("done", result.Result.status);
            Assert.Equal(0, await _api.QueryAsync<int>("SELECT COUNT(*) FROM place WHERE p_name = @newName;", new { newName }));

            var place = await _api.QueryAsync<(string p_summary, string p_category, string p_address, double p_latitude)>(
                "SELECT p_summary, p_category, p_address, p_latitude FROM place WHERE p_name = @oldName;", new { oldName });
            Assert.Equal(("原本的介紹", "Hotel", "神農街2號"), (place.p_summary, place.p_category, place.p_address));   // 原本有值的不覆蓋
            Assert.Equal(22.99, place.p_latitude, 4);
        }
        finally
        {
            _api.FakeNeo4j.Reset();
        }
    }

    /// <summary>Neo4j 旅宿的 place 表欄位資料（每天 15:00-22:00）；不放縣市區，免得影響其他用到行政區的查詢</summary>
    private static Dictionary<string, object> HotelDetails(string address) => new()
    {
        ["label"] = "Hotel",
        ["categories"] = new List<object> { "民宿" },
        ["address"] = address,
        ["opening_hours"] = new[] { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday" }
            .Select(day => (object)new Dictionary<string, object> { ["day_of_week"] = day, ["open_time"] = "15:00", ["close_time"] = "22:00" })
            .ToList()
    };
}
