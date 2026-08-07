using System;
using System.Security.Cryptography;
using System.Text;

namespace backend.utils
{
    class sha256Hash
    {
        /// <summary>
        /// 密碼加密
        /// </summary>
        /// <param name="Password"></param>
        /// <param name="Key"></param>
        /// <returns></returns>
        public string getSha256(string Password, string Key)
        {
            var encoding = new System.Text.UTF8Encoding();
            byte[] KeyByte = encoding.GetBytes(Key);
            byte[] PasswordBytes = encoding.GetBytes(Password);
            using (var hmacSHA256 = new HMACSHA256(KeyByte))
            {
                byte[] hashMessage = hmacSHA256.ComputeHash(PasswordBytes);
                return BitConverter.ToString(hashMessage).Replace("-", "").ToLower();
            }
        }
    }
}