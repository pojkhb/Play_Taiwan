using System;
using System.Collections.Generic;
using backend.utils;
using backend.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using MySql.Data.MySqlClient;

namespace backend.dao
{
    public class PostcardDao
    {
        private readonly AppSettings _appSettings;
        private readonly MySqlConnection _MysqlConnect;
        private readonly HttpContext _ipContext;

        public PostcardDao(IOptions<AppSettings> appSettings, IHttpContextAccessor httpContextAccessor)
        {
            _appSettings = appSettings.Value;
            _MysqlConnect = new MySqlConnection(_appSettings.mydb);
            _ipContext = httpContextAccessor.HttpContext;
        }

        #region 取得明信片詳情
        public PostcardResponse GetPostcard(string postcard_id)
        {
            // TODO: 資料表尚未建置，預計欄位:
            // postcard(postcard_id, story_id, ep_id, title, subtitle, front_image_url, back_photo_url, culture_note, found_date, is_night_edition)
            return new PostcardResponse
            {
                postcard_id = postcard_id,
                title = "臺南孔廟",
                subtitle = "全台首學的書香",
                front_image_url = null,
                back_photo_url = null,
                culture_note = "康熙年間建立，紅牆內藏著百年的朗朗讀書聲。",
                found_date = new DateTime(2026, 8, 6),
                is_night_edition = false
            };
        }
        #endregion

        #region 取得劇本所有明信片
        public List<PostcardResponse> GetPostcardsByStory(string story_id)
        {
            // TODO: SELECT * FROM postcard WHERE story_id = @story_id AND ep_id = @ep_id
            return new List<PostcardResponse>();
        }
        #endregion

        #region 實體列印 (iBON)
        public PostcardPrintResponse PrintPostcard(PostcardPrintRequest req)
        {
            // TODO: 呼叫 ibonPrinter API 上傳 PDF，取得真實取件編號
            return new PostcardPrintResponse
            {
                ibon_pickup_code = "MOCK123456",
                pdf_url = null
            };
        }
        #endregion

        #region 分享
        public void SharePostcard(PostcardShareRequest req)
        {
            // TODO: INSERT INTO share_log(ep_id, postcard_id, platform, shared_at) ...
        }
        #endregion
    }
}