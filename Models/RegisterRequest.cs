namespace backend.Models
{
    public class RegisterRequest
    {
        public string Username { get; set; }    // 欲註冊的顯示名稱。
        public string Email { get; set; }       // 欲註冊的電子信箱。
        public string Password { get; set; }    // 使用者輸入的明碼密碼 (後端會再進行加密)。
        public int AccountType { get; set; }    // 前端傳入的帳號身分：1 = 遊客，2 = 商家。
    }
}