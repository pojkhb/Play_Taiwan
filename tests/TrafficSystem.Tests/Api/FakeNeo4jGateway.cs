// 模擬 Neo4j：取代直連本地 Neo4j 的 LocalNeo4jDriverGatewayService，測試不會連到開發者自己的 Neo4j。
// 預設跟 CI 一樣「連不上」（丟出例外），後端會改用 MySQL 的景點資料；測試要模擬查詢結果時設定 Rows。
using backend.Services.Neo4j;

namespace TrafficSystem.Tests.Api;

public class FakeNeo4jGateway : INeo4jGatewayService
{
    /// <summary>每次查詢要回的資料列；null 代表 Neo4j 連不上</summary>
    public List<Dictionary<string, object>> Rows { get; set; }

    /// <summary>收到的 Cypher 查詢</summary>
    public System.Collections.Concurrent.ConcurrentQueue<string> Queries { get; } = new();

    public void Reset()
    {
        Rows = null;
        Queries.Clear();
    }

    public Task<List<Dictionary<string, object>>> ExecuteCypherAsync(string query, object parameters = null)
    {
        Queries.Enqueue(query);
        if (Rows == null)
            throw new InvalidOperationException("Couldn't connect to localhost:7687 (fake neo4j)");
        return Task.FromResult(Rows.Select(r => new Dictionary<string, object>(r)).ToList());
    }
}
