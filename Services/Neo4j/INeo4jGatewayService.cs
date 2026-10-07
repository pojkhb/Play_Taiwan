using System.Collections.Generic;
using System.Threading.Tasks;

namespace backend.Services.Neo4j
{
    /// <summary>
    /// 通用 Neo4j 存取介面。上層業務邏輯（景點搜尋、版本鏈讀寫）只依賴這個介面，
    /// 不需要知道底下怎麼連線；目前的實作是 LocalNeo4jDriverGatewayService（直連本地 Neo4j），測試時換成假的。
    /// </summary>
    public interface INeo4jGatewayService
    {
        /// <summary>
        /// 執行一段 Cypher 查詢／寫入，回傳每一列的欄位字典（key = RETURN 的別名）。
        /// </summary>
        Task<List<Dictionary<string, object>>> ExecuteCypherAsync(string query, object parameters = null);
    }
}
