// 檔案路徑：System\dao\Npc\NpcDao.cs
// NPC 名單（npc 表）：NPC 語音的下拉選單、檢查前端帶的 NPC 名字、找出劇本的 NPC
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using backend.utils;
using Microsoft.Extensions.Options;
using MySql.Data.MySqlClient;

namespace backend.dao
{
    public class NpcDao
    {
        private readonly AppSettings _appSettings;

        public NpcDao(IOptions<AppSettings> appSettings)
        {
            _appSettings = appSettings.Value;
        }

        private MySqlConnection Open() => new MySqlConnection(_appSettings.mydb);

        public class NpcRow
        {
            public int npc_id { get; set; }
            public string npc_name { get; set; }
            public string npc_role { get; set; }
            public string npc_avatar { get; set; }      // 站內路徑，Controller 組成完整網址
            public bool is_default { get; set; }
        }

        /// <summary>NPC 名單，預設 NPC（薯光）排第一個</summary>
        public async Task<List<NpcRow>> GetAllAsync()
        {
            using var conn = Open();
            return (await conn.QueryAsync<NpcRow>(@"
                SELECT npc_id, npc_name, npc_role, npc_avatar, is_default = 1 AS is_default
                FROM npc
                ORDER BY is_default DESC, npc_id;")).ToList();
        }

        public class StoryNpcRow
        {
            public string story_prologue { get; set; }
            public string story_synopsis { get; set; }
            public string npc_name { get; set; }        // 劇本的 NPC；劇本沒有指定時是預設 NPC，名單是空的時為 null
        }

        /// <summary>劇本的前傳、簡介與 NPC；找不到劇本回傳 null</summary>
        public async Task<StoryNpcRow> GetStoryAsync(int storyId)
        {
            using var conn = Open();
            return await conn.QueryFirstOrDefaultAsync<StoryNpcRow>($@"
                SELECT s.story_prologue, s.story_synopsis, n.npc_name
                FROM story s
                LEFT JOIN npc n ON n.npc_id = COALESCE(s.npc_id, {MapDao.DefaultNpcIdSql})
                WHERE s.s_id = @storyId AND s.is_active = 1;", new { storyId });
        }
    }
}
