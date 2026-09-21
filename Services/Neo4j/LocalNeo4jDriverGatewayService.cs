using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using backend.utils;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Neo4j.Driver;

namespace backend.Services.Neo4j
{
    /// <summary>
    /// 開發/測試用實作：用官方 Neo4j.Driver 直接連本地測試 instance 執行 Cypher。
    /// 正式環境上線、外部 /api/neo4j/cypher 開放寫入後，改用 <see cref="RemoteNeo4jApiGatewayService"/>，
    /// 只要改 appsettings 的 Neo4j:Mode，上層 Service 完全不用改。
    /// </summary>
    public class LocalNeo4jDriverGatewayService : INeo4jGatewayService
    {
        private readonly IDriver _driver;
        private readonly string _database;
        private readonly ILogger<LocalNeo4jDriverGatewayService> _logger;

        /// <summary>
        /// IDriver 由 Startup 註冊為 Singleton（一個 process 共用一個連線池），
        /// 這裡只負責用它開 Session 執行查詢，不自己管理 Driver 生命週期。
        /// </summary>
        public LocalNeo4jDriverGatewayService(IDriver driver, IOptions<Neo4jSettings> settings, ILogger<LocalNeo4jDriverGatewayService> logger)
        {
            _driver = driver;
            _database = string.IsNullOrWhiteSpace(settings.Value.Database) ? "neo4j" : settings.Value.Database;
            _logger = logger;
        }

        public async Task<List<Dictionary<string, object>>> ExecuteCypherAsync(string query, object parameters = null)
        {
            var cypherParameters = ToParameterDictionary(parameters);

            await using IAsyncSession session = _driver.AsyncSession(o => o.WithDatabase(_database));
            try
            {
                List<IRecord> records = await session.ExecuteReadOrWriteAsync(query, cypherParameters);
                return records.Select(RecordToDictionary).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "本地 Neo4j 查詢失敗：{Query}", query);
                throw;
            }
        }

        private static Dictionary<string, object> RecordToDictionary(IRecord record)
        {
            var dict = new Dictionary<string, object>();
            foreach (string key in record.Keys)
            {
                dict[key] = record[key];
            }
            return dict;
        }

        /// <summary>
        /// 呼叫端習慣傳匿名物件（比照現有 Neo4jService.ExecuteCypherAsync 的用法），
        /// 這裡用反射轉成 Neo4j.Driver 需要的 IDictionary&lt;string, object&gt;。
        /// </summary>
        private static Dictionary<string, object> ToParameterDictionary(object parameters)
        {
            var dict = new Dictionary<string, object>();
            if (parameters == null) return dict;

            if (parameters is IDictionary<string, object> existing)
            {
                return new Dictionary<string, object>(existing);
            }

            foreach (var prop in parameters.GetType().GetProperties())
            {
                dict[prop.Name] = prop.GetValue(parameters);
            }
            return dict;
        }
    }

    internal static class Neo4jSessionExtensions
    {
        /// <summary>
        /// query 是 MATCH/RETURN 還是 CREATE/MERGE/SET，呼叫端自己決定 Cypher 內容即可，
        /// AsyncSession 本身沒有區分讀寫的必要（本地測試 instance 用同一個 session 執行）。
        /// </summary>
        public static async Task<List<IRecord>> ExecuteReadOrWriteAsync(
            this IAsyncSession session, string query, Dictionary<string, object> parameters)
        {
            IResultCursor cursor = await session.RunAsync(query, parameters);
            return await cursor.ToListAsync();
        }
    }
}
