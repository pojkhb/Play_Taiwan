using System.Collections.Generic;
using System.Linq;

using backend.Models;
using backend.dao;
using System.IO;
using System;

namespace backend.Services
{
	public class FrameFunctionService
	{
		private readonly FrameFunctionDao _FrameFunctionDao;

		public FrameFunctionService(FrameFunctionDao FrameFunctionDao)
		{
			 _FrameFunctionDao = FrameFunctionDao;
		}

		#region 前端動態樣式
		public List<FrontendStyleResponse> Get_FrontendStyle()
		{
			string imagePath = "./image/Logo.png";
        	byte[] imageBytes = File.ReadAllBytes(imagePath);
        	string base64String = Convert.ToBase64String(imageBytes);

			List<FrontendStyleResponse> result = _FrameFunctionDao.Get_FrontendStyle();

			var style = result[1].value = base64String;

			return result;
		}
		#endregion

		#region 跑馬燈
		public List<MarqueeResponse> Get_Marquee()
		{
			List<MarqueeResponse> result = _FrameFunctionDao.Get_Marquee();
			int year = result.Select(x => x.year).FirstOrDefault();
			int month = result.Select(x => x.month).FirstOrDefault();
			
			result = new List<MarqueeResponse>();
			result.Add(new MarqueeResponse{
				year = year,
				month = month,
				date = "事故資料最新數據：" + year.ToString() + "年" + month.ToString() + "月"
			});

			return result;
		}
		#endregion
	}
}