namespace backend.Services
{
    /// <summary>
    /// 「取得目前操作者身分」的抽象介面。本階段尚未接登入驗證，直接採信 request 中明確帶入的
    /// au_id / s_id；之後加上登入驗證時，只要換成從 HttpContext.User 的 Claim 取值的實作並重新
    /// 註冊 DI，Controller/Service 呼叫方式完全不用改。
    /// </summary>
    public interface ICurrentActorProvider
    {
        /// <summary>取得目前操作的使用者 au_id。本階段直接回傳呼叫端帶入的值。</summary>
        int? GetAuId(int? auIdFromRequest);

        /// <summary>取得目前操作的商家 s_id。本階段直接回傳呼叫端帶入的值。</summary>
        int? GetSId(int? sIdFromRequest);
    }

    /// <summary>
    /// 本階段實作：不做登入驗證，原樣採信 request 帶入的 au_id / s_id。
    /// </summary>
    public class RequestActorProvider : ICurrentActorProvider
    {
        public int? GetAuId(int? auIdFromRequest) => auIdFromRequest;

        public int? GetSId(int? sIdFromRequest) => sIdFromRequest;
    }
}
