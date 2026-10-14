using System;
using System.Collections.Generic;

namespace backend.ViewModels
{
    /// <summary>管理員列表共用的分頁結果</summary>
    public class AdminPagedResponse<T>
    {
        public List<T> items { get; set; }
        public int total { get; set; }
        public int page { get; set; }
        public int page_size { get; set; }
    }

    #region 用戶管理

    /// <summary>用戶列表查詢條件</summary>
    public class AdminUserListQuery
    {
        /// <summary>比對使用者名稱或 Email（模糊搜尋）</summary>
        public string keyword { get; set; }

        /// <summary>使用者身分 1=遊客、2=商家、3=管理員；不帶就是全部</summary>
        public int? auth_type { get; set; }

        /// <summary>true=只看啟用、false=只看停用；不帶就是全部</summary>
        public bool? is_active { get; set; }

        public int page { get; set; } = 1;
        public int page_size { get; set; } = 20;
    }

    /// <summary>用戶資料（不含密碼與各種 Token）</summary>
    public class AdminUserResponse
    {
        public int au_id { get; set; }
        public string auth_name { get; set; }
        public string auth_email { get; set; }

        /// <summary>1=遊客、2=商家、3=管理員</summary>
        public int auth_type { get; set; }

        public bool is_active { get; set; }
        public bool is_email_verified { get; set; }
        public DateTime? au_birthday { get; set; }
        public string au_gender { get; set; }
        public DateTime? last_login_at { get; set; }
        public DateTime created_at { get; set; }
        public DateTime updated_at { get; set; }

        /// <summary>這位用戶的劇本數（含已停用）</summary>
        public int story_count { get; set; }

        /// <summary>這位用戶已收集的勳章數</summary>
        public int badge_count { get; set; }
    }

    /// <summary>新增用戶。管理員建立的帳號直接視為已驗證信箱，可以馬上登入。</summary>
    public class AdminUserCreateRequest
    {
        public string auth_name { get; set; }
        public string auth_email { get; set; }

        /// <summary>登入密碼（明碼，後端雜湊後存入）</summary>
        public string auth_pswd { get; set; }

        /// <summary>1=遊客、2=商家、3=管理員，預設 1</summary>
        public int auth_type { get; set; } = 1;

        public DateTime? au_birthday { get; set; }
        public string au_gender { get; set; }
    }

    /// <summary>修改用戶資料（整筆覆蓋；密碼另外用重設密碼 API）</summary>
    public class AdminUserUpdateRequest
    {
        public string auth_name { get; set; }
        public string auth_email { get; set; }

        /// <summary>1=遊客、2=商家、3=管理員</summary>
        public int auth_type { get; set; }

        /// <summary>false＝停用（軟刪除）；沒帶時視為 true</summary>
        public bool is_active { get; set; } = true;

        public bool is_email_verified { get; set; }
        public DateTime? au_birthday { get; set; }
        public string au_gender { get; set; }
    }

    /// <summary>管理員重設用戶密碼</summary>
    public class AdminUserPasswordRequest
    {
        public string new_password { get; set; }
    }

    #endregion

    #region 劇本管理

    /// <summary>劇本列表查詢條件</summary>
    public class AdminStoryListQuery
    {
        /// <summary>比對劇本標題、縣市、鄉鎮區（模糊搜尋）</summary>
        public string keyword { get; set; }

        /// <summary>只看某位用戶的劇本</summary>
        public int? au_id { get; set; }

        /// <summary>true=只看啟用、false=只看已刪除（停用）；不帶就是全部</summary>
        public bool? is_active { get; set; }

        public int page { get; set; } = 1;
        public int page_size { get; set; } = 20;
    }

    /// <summary>劇本列表的一筆</summary>
    public class AdminStoryListItem
    {
        public int s_id { get; set; }
        public int au_id { get; set; }

        /// <summary>劇本擁有者名稱</summary>
        public string auth_name { get; set; }

        public string story_title { get; set; }
        public string city_name { get; set; }
        public string district_name { get; set; }
        public int? npc_id { get; set; }
        public string npc_name { get; set; }
        public int? party_size { get; set; }
        public string sd_transport { get; set; }
        public bool is_active { get; set; }
        public bool is_night_mode { get; set; }
        public bool is_favorite { get; set; }

        /// <summary>節點數</summary>
        public int node_count { get; set; }

        public DateTime created_at { get; set; }
        public DateTime updated_at { get; set; }
    }

    /// <summary>劇本詳細資料：列表欄位加上前傳、簡介、偏好標籤與節點</summary>
    public class AdminStoryDetailResponse : AdminStoryListItem
    {
        public string story_prologue { get; set; }
        public string story_synopsis { get; set; }

        /// <summary>預計可抽的勳章類別（story.story_badge 原始文字）</summary>
        public string story_badge { get; set; }

        public int? story_postcards { get; set; }
        public List<string> tags { get; set; }
        public List<AdminStoryNodeItem> nodes { get; set; }
    }

    /// <summary>劇本節點（唯讀）</summary>
    public class AdminStoryNodeItem
    {
        public int sn_id { get; set; }
        public int sn_order { get; set; }
        public string sn_title { get; set; }

        /// <summary>對應景點（Neo4j uid）</summary>
        public string place_id { get; set; }

        public int? npc_id { get; set; }
        public string location_codename { get; set; }
        public string sn_task_type { get; set; }

        /// <summary>是否為隱藏節點（1=是、2=否）</summary>
        public int is_hidden { get; set; }

        /// <summary>是否僅限夜間解鎖（1=夜間、0=白天）</summary>
        public int is_night_only { get; set; }

        /// <summary>是否已解鎖（1=是、2=否）</summary>
        public int is_active { get; set; }

        public string sn_opening_text { get; set; }
        public string sn_success_text { get; set; }
        public DateTime? last_time { get; set; }
    }

    /// <summary>新增劇本（只建主檔，節點仍由 AI 生成流程寫入）</summary>
    public class AdminStoryCreateRequest
    {
        /// <summary>劇本擁有者</summary>
        public int au_id { get; set; }

        public string story_title { get; set; }
        public string story_prologue { get; set; }
        public string story_synopsis { get; set; }
        public string city_name { get; set; }
        public string district_name { get; set; }
        public int? party_size { get; set; }
        public string sd_transport { get; set; }

        /// <summary>主要 NPC（npc.npc_id），不帶就不指定</summary>
        public int? npc_id { get; set; }

        public bool is_night_mode { get; set; }
    }

    /// <summary>修改劇本主檔（整筆覆蓋）；is_active 改回 true 可以還原已刪除的劇本</summary>
    public class AdminStoryUpdateRequest
    {
        public string story_title { get; set; }
        public string story_prologue { get; set; }
        public string story_synopsis { get; set; }
        public string city_name { get; set; }
        public string district_name { get; set; }
        public int? party_size { get; set; }
        public string sd_transport { get; set; }
        public int? npc_id { get; set; }
        public bool is_night_mode { get; set; }

        /// <summary>false＝停用（軟刪除）；沒帶時視為 true</summary>
        public bool is_active { get; set; } = true;
    }

    #endregion

    #region 徽章管理

    /// <summary>徽章列表查詢條件</summary>
    public class AdminBadgeListQuery
    {
        /// <summary>比對勳章名字或對應物件（模糊搜尋）</summary>
        public string keyword { get; set; }

        /// <summary>勳章分類，例如「島嶼城市」（不含圖示）</summary>
        public string b_fication { get; set; }

        /// <summary>true=只看啟用、false=只看已刪除；不帶就是全部</summary>
        public bool? is_active { get; set; }

        public int page { get; set; } = 1;
        public int page_size { get; set; } = 20;
    }

    public class AdminBadgeResponse
    {
        public int b_id { get; set; }

        /// <summary>勳章分類（不含圖示），例如「島嶼城市」</summary>
        public string b_fication { get; set; }

        /// <summary>勳章對應物件：島嶼城市是縣市名、台灣印記是地標名…抽勳章時會拿來比對</summary>
        public string b_thing { get; set; }

        public string b_name { get; set; }

        /// <summary>勳章圖片完整網址</summary>
        public string b_image { get; set; }

        /// <summary>false＝已刪除（軟刪除），玩家端看不到也抽不到</summary>
        public bool is_active { get; set; }

        /// <summary>已收集這枚勳章的用戶數（含已刪除的勳章，紀錄都保留）</summary>
        public int owned_count { get; set; }

        public DateTime created_at { get; set; }
        public DateTime updated_at { get; set; }
    }

    /// <summary>新增勳章</summary>
    public class AdminBadgeSaveRequest
    {
        /// <summary>勳章分類，只能是 GET api/Admin/Badges/Categories 列出的其中一個</summary>
        public string b_fication { get; set; }

        public string b_thing { get; set; }
        public string b_name { get; set; }

        /// <summary>勳章圖片：先用 POST api/Upload 上傳拿到網址，或填站內路徑（例如 /images/badges/Taipei.png）</summary>
        public string b_image { get; set; }
    }

    /// <summary>修改勳章（整筆覆蓋）；is_active 改回 true 可以還原已刪除的勳章</summary>
    public class AdminBadgeUpdateRequest : AdminBadgeSaveRequest
    {
        /// <summary>false＝停用（軟刪除）；沒帶時視為 true</summary>
        public bool is_active { get; set; } = true;
    }

    #endregion
}
