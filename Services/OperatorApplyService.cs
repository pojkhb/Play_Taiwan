using System.Text;
using System.Net;
using System;
using System.Collections.Generic;
using System.Net.Mail;

using backend.Models;
using backend.dao;
using backend.utils;

using Microsoft.Extensions.Options;


namespace backend.Services
{
	public class OperatorApplyService
	{
		private readonly OperatorApplyDao _OperatorApplyDao;
		private readonly AppSettings _appSettings;

		public OperatorApplyService(OperatorApplyDao OperatorApplyDao, IOptions<AppSettings> appSettings)
		{
			 _OperatorApplyDao = OperatorApplyDao;
			 _appSettings = appSettings.Value;
		}

		#region 帳號申請-審核清單
		public List<ListRes> Get_OperatorApply(int is_checked)
		{
			return _OperatorApplyDao.Get_OperatorApply(is_checked);
		}
		#endregion  

		#region 帳號申請-申請功能
		public string Insert_OperatorApply(ApplyReq req)
		{
			string is_success = _OperatorApplyDao.Insert_OperatorApply(req);

			/* 判斷用戶帳號申請資訊是否寫入DB */
			if(is_success == "已成功申請，請等待審核")
			{
				try
				{
					/* 信件主旨 */
					string subject = "信件主旨~~"; 
					
					/* 信件內容樣式(使用HTML) */
					string content = $@"
					<html>
						<body>
							<h2 style='color: {_appSettings.background_color};'>{_appSettings.header}</h2>
							<p>有新的用戶申請</p>
							<p>請至平台上進行審核動作。</p>
							<p>
								平台連結: 
								<a href='{_appSettings.domain_name}/Auth/login' style='color: {_appSettings.background_color};'>點擊這裡</a>
							</p>
						</body>
					</html>";

					/* 收件人(可復數) */
					List<string> receive_mails = _OperatorApplyDao.Get_RoleCheckMail();

					// Send_Email(subject, content, receive_mails);
				}
				catch (Exception ex)
				{
					/* Other ERROR */
					return $"General Error: {ex.Message}";
				}
			}

			return is_success;
		}
		#endregion

		#region 帳號申請-確認審核
		public string Update_OperatorApply(CheckRep req)
		{
			string is_success = _OperatorApplyDao.Update_OperatorApply(req);

			/* 判斷用戶帳號申請資訊是否寫入DB */
			if(is_success == "success")
			{
				try
				{
					CheckRes res = _OperatorApplyDao.Get_RoleCheckMail(req.op_id);
					string message = string.Empty;
					string reason = string.Empty;

					/* 通過 */
					if(req.is_checked == 1)
					{
						message = "您的帳號申請已通過審核，請重新登入。";
						is_success = "已發送審核通過訊息！";
					}
					/* 拒絕 */
					else
					{
						message = $"由於您的帳號({res.reason})，";
						is_success = "已發送審核未通過訊息！";
						reason = "<p>故申請無法通過</p>";
					}
					
					/* 信件主旨 */
					string subject = "信件主旨~~";

					/* 信件內容樣式(使用HTML) */
					string content = $@"
					<html>
						<body>
							<table width='500px' border='0' cellpadding='0' cellspacing='0' style='background-color: #155571; color: white;'>
							<tr>
								<td align='center'>
								<table border='0' cellpadding='0' cellspacing='0' style='width: 500px; text-align: center; border: 2px solid black; font-size: 14px; font-weight: 500;'>
									<tr>
									<td style='padding: 0.5em;'>
										<p>{res.op_name} 先生/小姐，您好</p>
										<p>感謝您申請{_appSettings.header}</p>
										<p>{message}</p>
										{reason}
										<p>
										平台連結: 
										<a href='{_appSettings.domain_name}/Auth/login'>點擊這裡</a>
										</p>
										<p>若有問題歡迎聯繫交通工務局交通管理科</p>
										<p>謝謝您</p>
									</td>
									</tr>
								</table>
								</td>
							</tr>
							</table>
						</body>
					</html>";

					/* 收件人(可復數) */
					List<string> receive_mails = new List<string>{res.email};

					// Send_Email(subject, content, receive_mails);
				}
				catch (Exception ex)
				{
					/* Other ERROR */
					return $"General Error: {ex.Message}";
				}
			}

			return is_success;
		}
		#endregion 

		#region Mail寄信
		public void Send_Email(string subject, string content, List<string> receive_mails)
        {	
			/* 寄件人(系統) Google發信帳號 */
			string google_id = "youngleaders.mis@gmail.com";
			string temp_pwd = "fykvabtpvcdtfqcw"; /* Google應用程式密碼 */

			/* Google SMTP Server */
			string smtp_server = "smtp.gmail.com";
			/* Google SMTP Server Port */
			int smtp_port = 587;

			MailMessage mms = new MailMessage
			{
				From = new MailAddress(google_id),
				Subject = subject,
				Body = content,
				IsBodyHtml = true,
				SubjectEncoding = Encoding.UTF8
			};

			/* 添加多個收件人 */
			foreach (var mail in receive_mails)
			{
				mms.To.Add(new MailAddress(mail));
			}

			using (SmtpClient client = new SmtpClient(smtp_server, smtp_port))
			{
				client.EnableSsl = true;
				client.Credentials = new NetworkCredential(google_id, temp_pwd); /* 寄信帳密 */
				client.Send(mms); /* 寄出信件 */
			}
		}
		#endregion
	}
} 