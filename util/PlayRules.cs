// 檔案路徑：System\util\PlayRules.cs
// 遊玩規則常數：地圖抵達與任務作答共用，避免兩邊標準不一致。
namespace backend.utils
{
    public static class PlayRules
    {
        /// <summary>
        /// 抵達節點、作答時，玩家與景點座標的最大距離（公尺）。
        /// 景點座標只是一個點，公園、博物館這類大景點可能橫跨數百公尺，所以不能太嚴。
        /// </summary>
        public const double ArrivalRadiusMeters = 200;
    }
}
