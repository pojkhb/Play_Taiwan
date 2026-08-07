using System.Reflection;
using System;
using System.Collections.Generic;
using ClosedXML.Excel;
using Spire.Xls;

namespace backend.util
{
  public class XlsHelper
  {
    #region 注入自定義儲存格
    public void FillCustomCellToWorkSheet(Worksheet Sheet, List<CellValue> cellList)
    {
      foreach (var item in cellList)
      {
        SetCellValue(Sheet, item);
      }
    }
    #endregion
    
    #region 注入 List
    public void FillListToWorkSheet<T> (Worksheet Sheet, List<T> DataList, ref int RowIndex, int ColIndex, bool Header = false, bool ShowEmpty = false, bool AutoIncrement = false, string NumFormat = null, int FontSize = 12){
      int initColIndex = ColIndex;
      int AI_count = 1;
      Type type = typeof(T);
      PropertyInfo[] properties = type.GetProperties();
      
      // 建立表頭
      if (Header)
      {
        //編號自增欄位
        if (AutoIncrement) {
          Sheet.Range[RowIndex, ColIndex].Value2 = "編號";
          Sheet.Range[RowIndex, ColIndex].Style.Font.Size = FontSize;
          Sheet.Range[RowIndex, ColIndex].BorderAround(LineStyleType.Thin);
          Sheet.Range[RowIndex, ColIndex].HorizontalAlignment = HorizontalAlignType.Center;
          ColIndex++;
        }
        foreach (PropertyInfo property in properties)
        {
            Sheet.Range[RowIndex, ColIndex].Text = property.Name;
            Sheet.Range[RowIndex, ColIndex].Style.Font.Size = FontSize;
            Sheet.Range[RowIndex, ColIndex].BorderAround(LineStyleType.Thin);
            Sheet.Range[RowIndex, ColIndex].HorizontalAlignment = HorizontalAlignType.Center;
            ColIndex++;
        }
        RowIndex++;
      }
      
      // 塞資料
      foreach (T row in DataList)
      {
          ColIndex = initColIndex;
          foreach (PropertyInfo property in properties)
          {
              // 編號自增欄位
              if (AutoIncrement && ColIndex == initColIndex) {
                Sheet.Range[RowIndex, ColIndex].Value2 = AI_count++;
                Sheet.Range[RowIndex, ColIndex].Style.Font.Size = FontSize;
                Sheet.Range[RowIndex, ColIndex].BorderAround(LineStyleType.Thin);
                Sheet.Range[RowIndex, ColIndex].HorizontalAlignment = HorizontalAlignType.Center;
                ColIndex++;
              }
              object value = property.GetValue(row);
              Sheet.Range[RowIndex, ColIndex].Value2 = (value == null) ? string.Empty : value;
              Sheet.Range[RowIndex, ColIndex].Style.Font.Size = FontSize;
              Sheet.Range[RowIndex, ColIndex].BorderAround(LineStyleType.Thin);
              
              // 自訂數字格式
              if (!string.IsNullOrEmpty(NumFormat))
              {
                  Sheet.Range[RowIndex, ColIndex].NumberFormat = NumFormat;
              }
              // 數字千分位設定(無小數點、有小數點)
              else if (long.TryParse(value.ToString(), out _))
              {
                  Sheet.Range[RowIndex, ColIndex].NumberFormat = "#,##0";
              }
              else if (decimal.TryParse(value.ToString(), out _))
              {
                  Sheet.Range[RowIndex, ColIndex].NumberFormat = "#,##0.0##";
              }

              // 空值 Check
              if (!ShowEmpty && string.IsNullOrEmpty(value.ToString())){
                  Sheet.Range[RowIndex, ColIndex].Value = "-";
                  Sheet.Range[RowIndex, ColIndex].HorizontalAlignment = HorizontalAlignType.Center;
              }
              ColIndex++;
          }
          RowIndex++;
      }
    }
    #endregion
    
    #region 轉換 Excel 座標
    public string ConvertToExcelFormat(int x1, int y1, int x2, int y2)
    {
        string excelFormat = GetExcelColumnName(x1) + y1 + ":" + GetExcelColumnName(x2) + y2;
        return excelFormat;
    }
    public string GetExcelColumnName(int columnNumber)
    {
        int dividend = columnNumber;
        string columnName = String.Empty;
        int modulo;

        while (dividend > 0)
        {
            modulo = (dividend - 1) % 26;
            columnName = Convert.ToChar(65 + modulo).ToString() + columnName;
            dividend = (int)((dividend - modulo) / 26);
        }
        return columnName;
    }
    #endregion
    
    public class CellValue
    {
      public CellValue()
      {
        // this.col_width = 10;
        this.font_size = 12;
        this.font_color = System.Drawing.ColorTranslator.FromHtml("#000000");
        this.isBorder = true;
        this.isMerge = false;
        this.horizontalAlignType = HorizontalAlignType.Left;
        this.verticalAlignType = VerticalAlignType.Center;
        this.lineStyleType = LineStyleType.Thin;
      }
      public Worksheet worksheet { get; set; }
      public object value { get; set; }
      public int x1 { get; set; }
      public int y1 { get; set; }
      public int? x2 { get; set; }
      public int? y2 { get; set; }
      public System.Drawing.Color? background_color { get; set; }
      public int? col_width { get; set; }
      public System.Drawing.Color font_color { get; set; }
      public int font_size { get; set; }
      public string format { get; set; }
      public bool isBorder { get; set; }
      public bool isMerge { get; set; }
      public HorizontalAlignType horizontalAlignType { get; set; }
      public VerticalAlignType verticalAlignType { get; set; }
      public LineStyleType lineStyleType { get; set; }
    }
    // 固定儲存格設定
    public void SetCellValue(Worksheet Sheet, CellValue cell)
    {
        Sheet.Range[cell.x1, cell.y1].HorizontalAlignment = cell.horizontalAlignType;
        Sheet.Range[cell.x1, cell.y1].VerticalAlignment = cell.verticalAlignType;
        Sheet.Range[cell.x1, cell.y1].Style.Font.Size = cell.font_size;        
        Sheet.Range[cell.x1, cell.y1].Style.Font.Color = cell.font_color;
        Sheet.Range[cell.x1, cell.y1].IgnoreErrorOptions = IgnoreErrorType.NumberAsText;
        cell.x2 = cell.x2 ?? cell.x1;
        cell.y2 = cell.y2 ?? cell.y1;

        // 數字格式設定(無小數點、有小數點)
        if (ulong.TryParse(cell.value.ToString(), out _))
        {
            Sheet.Range[cell.x1, cell.y1].NumberFormat = "#,##0";
        }
        else if (decimal.TryParse(cell.value.ToString(), out _))
        {
            Sheet.Range[cell.x1, cell.y1].NumberFormat = "#,##0.0##";
        }
		    // 自訂數字格式
        if (!string.IsNullOrEmpty(cell.format)) {
            Sheet.Range[cell.x1, cell.y1].NumberFormat = cell.format;
        }
        // 內容格式設定
        if (cell.value is DateTime || Double.TryParse(cell.value.ToString(), out _))
        {
            Sheet.Range[cell.x1, cell.y1].Value2 = cell.value;
        }
        else
        {
            Sheet.Range[cell.x1, cell.y1].Text = cell.value.ToString();
        }
        
        // 設定欄寬
        if (cell.col_width != null){
            Sheet.Range[cell.x1, cell.y1].ColumnWidth = (int)cell.col_width;
        }
        // 儲存格背景
        if(cell.background_color != null){
            Sheet.Range[cell.x1, cell.y1].Style.Color = (System.Drawing.Color)cell.background_color;
        }
        // 是否合併儲存格
        if (cell.isMerge)
        {
            Sheet.Range[cell.x1, cell.y1, (int)cell.x2, (int)cell.y2].Merge();
        }
        // 是否有欄位框
        if (cell.isBorder)
        {
            Sheet.Range[cell.x1, cell.y1, (int)cell.x2, (int)cell.y2].BorderAround(cell.lineStyleType);
        }
    }

    

    public XLWorkbook ExportXls<T>(List<T> lists, string sheetName)
    {
      XLWorkbook workbook = new XLWorkbook();
      var sheet = workbook.Worksheets.Add(sheetName);
      int colIndex = 2;
      // 標題內容
      foreach (var item in typeof(T).GetProperties())
      {
        sheet.Cell(1, colIndex).Value = item.Name;
        colIndex++;
      }
      // 資料起始列
      int rowIndex = 2;
      foreach (var list in lists)
      {
        // 資料起始位置
        int conlumnIndex = 1;
        foreach (var item in list.GetType().GetProperties())
        {
          sheet.Cell(rowIndex, conlumnIndex).Value = string.Concat($"'{Convert.ToString(item.GetValue(list, null))}");
          conlumnIndex++;
        }
        rowIndex++;
      }
      return workbook;
    }
  }
}