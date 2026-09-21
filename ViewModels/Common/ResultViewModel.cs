using System.Collections.Generic;

namespace backend.ViewModels
{
  public class ResultViewModel<T>
  {

    public bool isSuccess { get; set; }
    public string message { get; set; }
    public T Result { get; set; }
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