using System.Threading.Tasks;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Configuration;
using MimeKit;
using MimeKit.Utils;

namespace backend.Services
{
    public class EmailService
    {
        private readonly IConfiguration _config;

        public EmailService(IConfiguration config)
        {
            _config = config;
        }

        /// <param name="htmlContent">信件內文（片段即可，會包成完整的 HTML 文件）</param>
        /// <param name="textContent">純文字版本：HTML 顯示不出來的信箱（部分手機 App）會改顯示這份，連結要寫成完整網址</param>
        public async Task SendEmailAsync(string toEmail, string toName, string subject, string htmlContent, string textContent)
        {
            var email = new MimeMessage();

            var senderName = _config["SmtpSettings:SenderName"];
            var senderEmail = _config["SmtpSettings:SenderEmail"];

            email.From.Add(new MailboxAddress(senderName, senderEmail));
            // 寄件信箱是 noreply 這類收不到信的地址時，使用者按回覆會寄到這裡
            var replyTo = _config["SmtpSettings:ReplyTo"];
            if (!string.IsNullOrWhiteSpace(replyTo))
                email.ReplyTo.Add(new MailboxAddress(senderName, replyTo));
            email.To.Add(new MailboxAddress(toName, toEmail));
            email.Subject = subject;
            // 預設會用本機電腦名稱當 Message-Id 網域，改用寄件信箱的網域，比較不會被當成可疑信件
            email.MessageId = MimeUtils.GenerateMessageId(senderEmail.Substring(senderEmail.IndexOf('@') + 1));

            // 純文字 + HTML 兩個版本（multipart/alternative），信箱挑自己能顯示的那份
            var body = new BodyBuilder
            {
                TextBody = textContent,
                HtmlBody = $@"<!DOCTYPE html>
<html lang='zh-Hant'>
<head>
<meta charset='utf-8' />
<meta name='viewport' content='width=device-width, initial-scale=1.0' />
</head>
<body style='margin: 0;'>
{htmlContent}
</body>
</html>"
            };
            email.Body = body.ToMessageBody();

            using var smtp = new SmtpClient();
            try
            {
                var server = _config["SmtpSettings:Server"];
                var port = int.Parse(_config["SmtpSettings:Port"]);
                var username = _config["SmtpSettings:Username"];
                var password = _config["SmtpSettings:Password"];

                // Gmail 需要 StartTLS 加密
                await smtp.ConnectAsync(server, port, SecureSocketOptions.StartTls);
                await smtp.AuthenticateAsync(username, password);
                await smtp.SendAsync(email);
            }
            finally
            {
                await smtp.DisconnectAsync(true);
            }
        }
    }
}
