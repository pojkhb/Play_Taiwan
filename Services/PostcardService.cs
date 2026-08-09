using System.Collections.Generic;
using backend.dao;
using backend.Models;

namespace backend.Services
{
    public class PostcardService
    {
        private readonly PostcardDao _dao;
        public PostcardService(PostcardDao dao) { _dao = dao; }

        #region 取得明信片詳情
        public PostcardResponse GetPostcard(string postcard_id)
        {
            return _dao.GetPostcard(postcard_id);
        }
        #endregion

        #region 取得劇本所有明信片
        public List<PostcardResponse> GetPostcardsByStory(string story_id)
        {
            return _dao.GetPostcardsByStory(story_id);
        }
        #endregion

        #region 實體列印 (iBON)
        public PostcardPrintResponse PrintPostcard(PostcardPrintRequest req)
        {
            // TODO: 呼叫 ibonPrinter API (https://github.com/ThanatosDi/ibonPrinter)
            // 1. 產生明信片 PDF
            // 2. 上傳至 iBON 雲端取得取件編號
            return _dao.PrintPostcard(req);
        }
        #endregion

        #region 分享
        public void SharePostcard(PostcardShareRequest req)
        {
            _dao.SharePostcard(req);
        }
        #endregion
    }
}