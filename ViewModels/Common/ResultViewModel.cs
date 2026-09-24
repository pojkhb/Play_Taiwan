using System.Collections.Generic;

namespace backend.ViewModels
{
  /// <summary>所有 API 共用的回傳外層格式</summary>
  public class ResultViewModel<T>
  {

    /// <summary>是否成功：true = 成功、false = 失敗（失敗原因看 message）</summary>
    public bool isSuccess { get; set; }

    /// <summary>給使用者看的訊息，例如「查詢成功」或錯誤原因</summary>
    public string message { get; set; }

    /// <summary>實際回傳資料，失敗時通常為 null</summary>
    public T Result { get; set; }

    /// <summary>狀態碼（目前大多沒有使用，為 null）</summary>
    public string Status{get;set;}

    public ResultViewModel()
    {

    }

    public ResultViewModel(bool isSuccess, string message, T Result)
    {
      this.isSuccess = isSuccess;
      this.message = message;
      this.Result = Result;
    }

    public ResultViewModel(bool isSuccess, string message, T Resul, string status)
    {
      this.isSuccess = isSuccess;
      this.message = message;
      this.Result = Result;
      this.Status = status;
    }
  }
}
