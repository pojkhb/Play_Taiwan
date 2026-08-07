using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;

using backend.Models;
using backend.dao;
using backend.util;

using Spire.Xls;
namespace backend.Services
{
	public class OperatorSettingService
	{
		private readonly OperatorSettingDao _operatorSettingDao;

		public OperatorSettingService(OperatorSettingDao operatorSettingDao)
		{
			_operatorSettingDao = operatorSettingDao;
		}
		#region 帳號管理-檢查帳號是否存在

		#endregion

		#region 個人管理-個人資料
		public OperatorResponse GetUserInfo_OperatorSetting(string op_id)
		{
			return _operatorSettingDao.GetUserInfo_OperatorSetting(op_id);
		}
		#endregion

		#region 帳號管理-列表資料(報表下載)
		public Dictionary<string, object> OperatorSettingExport()
		{
			var data = _operatorSettingDao.Get_OperatorSetting();
			if (data == null || data.Count == 0) return null;

			Dictionary<string, object> Result = new Dictionary<string, object>();

			string Name = $"帳號清單_{DateTime.Now:yyyyMMdd_HHmmss}";
			Result.Add("Name", Name);

			Workbook Workbook = new Workbook();
			Workbook.Worksheets.Clear();
			Worksheet Worksheet = Workbook.Worksheets.Add("帳號資料");

			XlsHelper xls = new XlsHelper();
			List<XlsHelper.CellValue> CellList = new List<XlsHelper.CellValue>();
			int ColumnNum = 1;
			int RowNum = 1;

			#region 資料處理
			var DataList = data.Select(item => new OperatorExportRes
			{
				OpId = item.op_id,
				OpName = item.op_name,
				Email = item.email,
				UnitName = item.unit_name,
				RoleName = item.role_name,
				Status = item.useable ? "啟用" : "停用",
			}).ToList();
			#endregion

			#region 表頭
			List<string> ResultData = new List<string>() {
			"帳號",
			"使用者名稱",
			"信箱",
			"所屬單位",
			"角色",
			"狀態"
			};

			foreach (var item in ResultData)
			{
				CellList.Add(new XlsHelper.CellValue()
				{
					value = item,
					x1 = RowNum,
					y1 = ColumnNum++,
					horizontalAlignType = HorizontalAlignType.Center,
				});
			}
			#endregion

			xls.FillCustomCellToWorkSheet(Worksheet, CellList);

			#region 表身
			ColumnNum = 1;
			RowNum++;
			xls.FillListToWorkSheet(Worksheet, DataList, ref RowNum, ColumnNum, Header: false);
 			#endregion
			
			byte[] fileContent;
			using (var ms = new MemoryStream())
			{
				Workbook.SaveToStream(ms, FileFormat.Version2016);
				fileContent = ms.ToArray();
			}

			Result.Add("FileContent", fileContent);
			Result.Add("worksheet", Worksheet);

			return Result;
		}
		#endregion

		#region 帳號管理-列表資料
		public List<OperatorResponse> Get_OperatorSetting()
		{
			return _operatorSettingDao.Get_OperatorSetting();
		}
		#endregion

		#region 帳號管理-新增功能
		public bool Insert_OperatorSetting(OperatorRequest req)
		{
			return _operatorSettingDao.Insert_OperatorSetting(req);
		}
		#endregion

		#region 帳號管理-修改功能
		public bool Update_OperatorSetting(OperatorRequest req)
		{
			return _operatorSettingDao.Update_OperatorSetting(req);
		}
		#endregion

		#region 個人管理-修改密碼
		public string Update_OperatorPswd(OperatorRequest req)
		{
			return _operatorSettingDao.Update_OperatorPswd(req);
		}
		#endregion

		#region 帳號管理-軟刪除功能
		public bool Delete_OperatorSetting(string op_id)
		{
			return _operatorSettingDao.Delete_OperatorSetting(op_id);
		}
		#endregion

		#region 帳號管理-啟用停用功能
		public bool Useable_OperatorSetting(OperatorRequest req)
		{
			return _operatorSettingDao.Useable_OperatorSetting(req);
		}
		#endregion
	}
}