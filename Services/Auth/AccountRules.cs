// 檔案路徑：System\Services\Auth\AccountRules.cs
// 帳號欄位的檢查規則：遊客註冊、商家註冊、修改帳號名稱、管理員建立／修改帳號、改密碼與重設密碼共用同一份，
// 不合規則一律丟 BadRequestException（回 400），訊息可以直接顯示給使用者。
using System;
using System.Text.RegularExpressions;
using backend.Models;

namespace backend.Services
{
    public static class AccountRules
    {
        public const int NameMaxLength = 50;
        public const int EmailMaxLength = 255;
        public const int PasswordMinLength = 6;
        public const int PasswordMaxLength = 100;
        public const int GenderMaxLength = 20;   // auth.au_gender 是 varchar(20)

        private static readonly Regex EmailPattern = new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled);

        /// <summary>帳號名稱：去掉前後空白後不能是空的，最多 50 字，不能有換行等控制字元</summary>
        public static string Name(string name)
        {
            name = name?.Trim();
            if (string.IsNullOrEmpty(name)) throw new BadRequestException("請填寫帳號名稱");
            if (name.Length > NameMaxLength) throw new BadRequestException($"帳號名稱最多 {NameMaxLength} 個字");
            foreach (char c in name)
            {
                if (char.IsControl(c)) throw new BadRequestException("帳號名稱不能包含換行或特殊控制字元");
            }
            return name;
        }

        /// <summary>Email：去掉前後空白後要符合 xxx@xxx.xxx 的格式</summary>
        public static string Email(string email)
        {
            email = email?.Trim();
            if (string.IsNullOrEmpty(email)) throw new BadRequestException("請填寫 Email");
            if (email.Length > EmailMaxLength || !EmailPattern.IsMatch(email))
                throw new BadRequestException("Email 格式不正確，請確認後再試一次");
            return email;
        }

        /// <summary>密碼：6～100 字，不能前後有空白（避免使用者以為打對卻多了空白而登不進去）</summary>
        public static void Password(string password)
        {
            if (string.IsNullOrEmpty(password)) throw new BadRequestException("請填寫密碼");
            if (password.Length < PasswordMinLength || password.Length > PasswordMaxLength)
                throw new BadRequestException($"密碼長度要在 {PasswordMinLength}～{PasswordMaxLength} 字之間");
            if (password != password.Trim()) throw new BadRequestException("密碼前後不能有空白");
        }

        /// <summary>生日（選填）：不能是未來的日期，也不能早於 1900 年</summary>
        public static DateTime? Birthday(DateTime? birthday)
        {
            if (birthday == null) return null;
            DateTime date = birthday.Value.Date;
            if (date > DateTime.UtcNow.AddHours(8).Date) throw new BadRequestException("生日不能是未來的日期");
            if (date.Year < 1900) throw new BadRequestException("生日格式不正確");
            return date;
        }

        /// <summary>性別（選填）：去掉前後空白，空的當作沒填，最多 20 字</summary>
        public static string Gender(string gender)
        {
            gender = gender?.Trim();
            if (string.IsNullOrEmpty(gender)) return null;
            if (gender.Length > GenderMaxLength) throw new BadRequestException($"性別最多 {GenderMaxLength} 個字");
            return gender;
        }
    }
}
