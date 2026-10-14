// 檔案路徑：System\Services\Pair\TeamLocationStore.cs
using System;
using System.Collections.Concurrent;
using System.Linq;

namespace backend.Services
{
    /// <summary>
    /// 一起玩同一份劇本的人各自最後回報的位置（地圖上顯示隊友在哪，POST api/Pair/Story/{story_id}/Location）。
    /// 只放在記憶體、不寫進資料庫：位置幾秒就更新一次，舊的沒有用，也不留下玩家的移動軌跡。
    /// 後端重啟後清空，隊員下一次回報（前端每幾秒一次）就會補回來。
    /// </summary>
    public class TeamLocationStore
    {
        /// <summary>幾秒內有回報才算在線上</summary>
        public const int OnlineSeconds = 60;

        /// <summary>最後一次回報超過這麼久，就當作沒有位置（不再顯示在地圖上）</summary>
        public static readonly TimeSpan KeepFor = TimeSpan.FromMinutes(30);

        public class Entry
        {
            public double lat { get; set; }
            public double lng { get; set; }

            /// <summary>定位精準度（公尺），前端有帶才有</summary>
            public double? accuracy { get; set; }

            /// <summary>面向的方向（度，0＝正北，順時針），前端有帶才有</summary>
            public double? heading { get; set; }

            /// <summary>後端收到這筆位置的時間（UTC）</summary>
            public DateTime updated_at { get; set; }
        }

        private readonly ConcurrentDictionary<(int storyId, int auId), Entry> _entries = new();
        private DateTime _lastSweep = DateTime.MinValue;

        public void Report(int storyId, int auId, Entry entry)
        {
            _entries[(storyId, auId)] = entry;
            Sweep(entry.updated_at);
        }

        /// <summary>這個人在這份劇本最後回報的位置；沒回報過或太久以前回報的回傳 null</summary>
        public Entry Get(int storyId, int auId, DateTime utcNow)
        {
            return _entries.TryGetValue((storyId, auId), out Entry entry) && utcNow - entry.updated_at < KeepFor ? entry : null;
        }

        /// <summary>每隔一段時間清掉過期的位置，避免越存越多</summary>
        private void Sweep(DateTime utcNow)
        {
            if (utcNow - _lastSweep < KeepFor) return;
            _lastSweep = utcNow;

            foreach (var old in _entries.Where(e => utcNow - e.Value.updated_at >= KeepFor).ToList())
                _entries.TryRemove(old.Key, out _);
        }
    }
}
