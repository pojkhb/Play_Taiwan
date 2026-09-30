// API 整合測試共用的後端：
// 1. 在 MySQL 建一個獨立的測試資料庫（Sqls/mysql/schema.sql + seed_reference.sql），跑完就刪掉，不會動到開發資料
// 2. 用 WebApplicationFactory 把整個後端在記憶體裡啟動，測試直接打真的 API
// MySQL 連線：有設環境變數 TEST_MYSQL 就用它（CI 用），沒有就用 appsettings.json 的 AppSettings:mydb（本機用）。
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using backend;
using Dapper;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.IdentityModel.Tokens;
using MySql.Data.MySqlClient;

namespace TrafficSystem.Tests.Api;

[CollectionDefinition(Name)]
public class ApiCollection : ICollectionFixture<ApiFactory>
{
    /// <summary>API 測試共用同一個後端與測試資料庫，同一個 collection 內依序執行</summary>
    public const string Name = "API";
}

public class ApiFactory : WebApplicationFactory<Startup>, IAsyncLifetime
{
    public const string JwtSecret = "PlayTaiwan_Integration_Test_JWT_Secret_2026_0123456789";

    public static readonly string RepoRoot = FindRepoRoot();

    private readonly string _database = $"play_taiwan_test_{Guid.NewGuid():N}";
    private readonly string _serverConnection;
    private int _nextUserId = 1000;

    /// <summary>後端使用的測試資料庫連線字串</summary>
    public string ConnectionString { get; }

    public ApiFactory()
    {
        var server = new MySqlConnectionStringBuilder(Environment.GetEnvironmentVariable("TEST_MYSQL") ?? LocalConnection())
        {
            Database = ""
        };
        _serverConnection = server.ConnectionString;
        ConnectionString = new MySqlConnectionStringBuilder(_serverConnection) { Database = _database }.ConnectionString;
    }

    private static string LocalConnection() =>
        new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(RepoRoot, "appsettings.json"))
            .Build()["AppSettings:mydb"];

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "TrafficSystem.csproj")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("找不到 TrafficSystem.csproj");
    }

    /// <summary>後端往外打的 HTTP（AI 服務等）全部由它回應，不會真的連網路</summary>
    public FakeAiService FakeAi { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string>
        {
            ["AppSettings:mydb"] = ConnectionString,
            ["AppSettings:jwt_secret"] = JwtSecret,
            ["BusSync:Enabled"] = "false",     // 測試時不要在背景同步公車、捷運資料
            ["MetroSync:Enabled"] = "false",
            ["SmtpSettings:Server"] = "127.0.0.1",   // 寄信一律失敗，測試不會寄出真的信（註冊、忘記密碼）
            ["SmtpSettings:Port"] = "1",
        }));
        builder.ConfigureTestServices(services =>
            services.ConfigureAll<HttpClientFactoryOptions>(options =>
                options.HttpMessageHandlerBuilderActions.Add(handler => handler.PrimaryHandler = FakeAi)));
    }

    #region 建立 / 刪除測試資料庫

    public async Task InitializeAsync()
    {
        using (var conn = new MySqlConnection(_serverConnection))
        {
            await conn.OpenAsync();
            await conn.ExecuteAsync($"CREATE DATABASE `{_database}` CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci;");
        }

        // 匯出的 SQL 會用到 @OLD_SQL_MODE 這類變數
        var setup = new MySqlConnectionStringBuilder(ConnectionString) { AllowUserVariables = true };
        using (var conn = new MySqlConnection(setup.ConnectionString))
        {
            await conn.OpenAsync();
            foreach (string file in new[] { "schema.sql", "seed_reference.sql" })
            {
                string sql = await File.ReadAllTextAsync(Path.Combine(RepoRoot, "Sqls", "mysql", file));
                await new MySqlScript(conn, sql).ExecuteAsync();
            }
        }
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();

        using var conn = new MySqlConnection(_serverConnection);
        await conn.OpenAsync();
        await conn.ExecuteAsync($"DROP DATABASE IF EXISTS `{_database}`;");
    }

    #endregion

    #region 登入身分

    /// <summary>每個測試用不同的玩家，彼此的資料不會互相影響</summary>
    public int NewUserId() => Interlocked.Increment(ref _nextUserId);

    public static string Token(int auId, DateTime? expires = null, string secret = JwtSecret)
    {
        DateTime exp = expires ?? DateTime.UtcNow.AddHours(1);
        var token = new JwtSecurityToken(
            claims: new[] { new Claim("au_id", auId.ToString()) },
            notBefore: exp.AddHours(-2),
            expires: exp,
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)), SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>帶登入身分的 HttpClient；auId 為 null 時不帶 Token</summary>
    public HttpClient ClientFor(int? auId)
    {
        HttpClient client = CreateClient();
        if (auId.HasValue)
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(auId.Value));
        return client;
    }

    #endregion

    #region 準備測試資料

    public async Task<T> QueryAsync<T>(string sql, object param = null)
    {
        using var conn = new MySqlConnection(ConnectionString);
        return await conn.QueryFirstOrDefaultAsync<T>(sql, param);
    }

    public async Task ExecuteAsync(string sql, object param = null)
    {
        using var conn = new MySqlConnection(ConnectionString);
        await conn.ExecuteAsync(sql, param);
    }

    /// <summary>給景點一張照片：由 FakeAi 提供圖片內容（天空 + 高塔），並寫進 place.p_image，回傳照片網址</summary>
    public async Task<string> GivePlacePhotoAsync(string placeName)
    {
        string url = $"https://img.test/{Guid.NewGuid():N}.png";
        using var photo = TrafficSystem.Tests.Silhouette.SilhouetteImageHelperTests.TowerPhoto(240, 200);
        using var ms = new MemoryStream();
        await SixLabors.ImageSharp.ImageExtensions.SaveAsPngAsync(photo, ms);
        FakeAi.Images[url] = ms.ToArray();
        await ExecuteAsync("UPDATE place SET p_image = @url WHERE p_name = @placeName;", new { url, placeName });
        return url;
    }

    /// <summary>
    /// 建立一個劇本：每個景點建一個節點，並寫入 place_type（每個景點 3 種任務類型，跟正式資料一樣一個景點多筆）與 place（座標）。
    /// 回傳 story_id。
    /// </summary>
    public async Task<int> CreateStoryAsync(int ownerId, string city, bool night, params string[] places)
    {
        using var conn = new MySqlConnection(ConnectionString);
        await conn.OpenAsync();

        int storyId = await conn.ExecuteScalarAsync<int>(@"
            INSERT INTO story (au_id, city_name, district_name, story_title, story_prologue, story_synopsis,
                               story_postcards, is_active, is_night_mode)
            VALUES (@ownerId, @city, '中正區', @title, '前情提要', '劇本簡介', @count, 1, @night);
            SELECT LAST_INSERT_ID();",
            new { ownerId, city, title = $"測試劇本 {string.Join("、", places)}", count = places.Length, night = night ? 1 : 2 });

        for (int i = 0; i < places.Length; i++)
        {
            string placeId = Guid.NewGuid().ToString();
            await conn.ExecuteAsync(@"
                INSERT INTO story_node (s_id, place_id, sn_order, sn_title, location_codename, sn_opening_text, sn_success_text)
                VALUES (@storyId, @placeId, @order, @title, @codename, '開場', '完成');",
                new { storyId, placeId, order = i + 1, title = $"第{i + 1}站", codename = $"代號{i + 1}" });

            int nextTypeRow = await conn.ExecuteScalarAsync<int>("SELECT COALESCE(MAX(place_type_id), 0) + 1 FROM place_type;");
            foreach (int typeId in new[] { 6, 7, 8 })
            {
                await conn.ExecuteAsync(@"
                    INSERT INTO place_type (place_type_id, place_id, place_name, place_category, type_id)
                    VALUES (@id, @placeId, @name, 'Attraction', @typeId);",
                    new { id = nextTypeRow++, placeId, name = places[i], typeId });
            }

            await conn.ExecuteAsync(
                "INSERT INTO place (p_name, p_latitude, p_longitude) VALUES (@name, @lat, 121.5);",
                new { name = places[i], lat = 25.03 + i * 0.001 });
        }

        return storyId;
    }

    #endregion
}

/// <summary>讀取後端共用的回傳格式 ResultViewModel</summary>
public class ApiResult<T>
{
    public bool isSuccess { get; set; }
    public string message { get; set; }
    public T Result { get; set; }
}

public static class HttpResponseExtensions
{
    public static async Task<ApiResult<T>> ReadResultAsync<T>(this HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<ApiResult<T>>();

    /// <summary>不需要特定型別時，直接用 JsonElement 讀 Result</summary>
    public static Task<ApiResult<JsonElement>> ReadResultAsync(this HttpResponseMessage response) =>
        response.ReadResultAsync<JsonElement>();
}
