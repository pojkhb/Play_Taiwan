using System.Text.Json.Serialization;

namespace backend.Models
{
    public class RegisterRequest
    {
        public string Username { get; set; }    // 欲註冊的顯示名稱。
        public string Email { get; set; }       // 欲註冊的電子信箱。
        public string Password { get; set; }    // 使用者輸入的明碼密碼 (後端會再進行加密)。

        // 相容 PascalCase 寫法：前端傳 { "AccountType": 2 } 會對到這裡
        [JsonPropertyName("AccountType")]
        public int? AccountType { get; set; }

        // 相容 snake_case 寫法：前端傳 { "account_type": 2 } 會對到這裡（跟你專案其他 API 命名習慣一致）
        [JsonPropertyName("account_type")]
        public int? account_type { get; set; }

        /// <summary>
        /// 真正要拿去用的帳號類型：不管前端傳哪一種命名都能正確抓到值，
        /// 兩個都沒傳到才預設為 1（一般遊客），避免像之前那樣因為命名對不上而永遠變成 1。
        /// </summary>
        [JsonIgnore]
        public int ResolvedAccountType => account_type ?? AccountType ?? 1;
    }
}