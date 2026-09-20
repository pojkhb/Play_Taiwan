using System.Collections.Generic;

namespace backend.Models
{
    /// <summary>
    /// 商業邏輯上的「操作衝突」，例如刪除已被其他資料引用的資料列。
    /// 由 ExceptionHandlingMiddleware 統一轉成 HTTP 409 回應。
    /// </summary>
    public class ConflictException : System.Exception
    {
        /// <summary>造成衝突的關聯資料清單（例如仍在引用此題目的 task_id 清單）。</summary>
        public IEnumerable<object> Conflicts { get; }

        public ConflictException(string message, IEnumerable<object> conflicts = null) : base(message)
        {
            Conflicts = conflicts;
        }
    }

    /// <summary>
    /// 商業邏輯上的「找不到資料」，由 ExceptionHandlingMiddleware 統一轉成 HTTP 404 回應。
    /// </summary>
    public class NotFoundException : System.Exception
    {
        public NotFoundException(string message) : base(message) { }
    }
}
