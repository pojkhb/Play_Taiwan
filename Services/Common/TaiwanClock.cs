// 檔案路徑：System\Services\Common\TaiwanClock.cs
using System;

namespace backend.Services
{
    /// <summary>現在的台灣時間。包成一個服務，測試「到了用餐時間才提醒」這類跟時間有關的功能時可以換成固定的時間</summary>
    public class TaiwanClock
    {
        public virtual DateTime Now => DateTime.UtcNow.AddHours(8);
    }
}
