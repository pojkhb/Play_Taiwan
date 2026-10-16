// 檔案路徑：System\Services\Story\BlueprintTextRepair.cs
// AI 用本機 LLM 寫劇本，「簃」「璿」這類罕用字常被拆成好幾個 byte 輸出、拼不回原字，變成「�」（U+FFFD），
// 例如 Neo4j 的「簃居青衖」在劇情裡變成「入住�居青衖」。存檔前用 Neo4j 查到的正確景點名稱換回來，剩下對不回去的 � 直接拿掉。
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using backend.ViewModels;

namespace backend.Services
{
    public static class BlueprintTextRepair
    {
        private const char Garbled = '�';

        /// <summary>
        /// placeNames：spot_uuid → Neo4j 的正確景點名稱。
        /// 1. 每站的 place_name 換成正確名稱。
        /// 2. 劇本所有文字裡，景點名稱有字變成 � 的（至少要留一個原字才算），換回正確名稱。
        /// 3. 最後拿掉剩下的 �。
        /// </summary>
        public static void Repair(ScriptBlueprintData data, IReadOnlyDictionary<string, string> placeNames)
        {
            if (data == null) return;
            List<ScriptBlueprintNode> nodes = data.nodes ?? new List<ScriptBlueprintNode>();

            string RealName(ScriptBlueprintNode node) =>
                node.spot_uuid != null && placeNames.TryGetValue(node.spot_uuid, out string name) && !string.IsNullOrWhiteSpace(name) ? name : null;

            // 只有一個字的名稱整個變成 � 時看不出是誰，不換
            List<(Regex pattern, string name)> names = nodes
                .Select(RealName)
                .Where(name => name != null && name.EnumerateRunes().Count() >= 2)
                .Distinct()
                .Select(name => (GarbledNamePattern(name), name))
                .ToList();

            string Fix(string text)
            {
                if (string.IsNullOrEmpty(text) || !text.Contains(Garbled)) return text;
                foreach ((Regex pattern, string name) in names)
                    text = pattern.Replace(text, m => m.Value.Any(c => c != Garbled) ? name : m.Value);
                return text.Replace(Garbled.ToString(), "");
            }

            data.title = Fix(data.title);
            data.preface = Fix(data.preface);
            data.synopsis = Fix(data.synopsis);
            if (data.npc != null)
            {
                data.npc.name = Fix(data.npc.name);
                data.npc.role = Fix(data.npc.role);
                data.npc.intro = Fix(data.npc.intro);
            }

            foreach (ScriptBlueprintNode node in nodes)
            {
                node.place_name = RealName(node) ?? Fix(node.place_name);
                node.node_title = Fix(node.node_title);
                node.location_codename = Fix(node.location_codename);
                node.task_type = Fix(node.task_type);
                node.task_description = Fix(node.task_description);
                if (node.dialogues != null)
                {
                    node.dialogues.opening = Fix(node.dialogues.opening);
                    node.dialogues.success = Fix(node.dialogues.success);
                }
            }
        }

        /// <summary>名稱裡任一字都可能變成一個或多個 �：「簃居青衖」對得到「�居青衖」「簃居��衖」</summary>
        private static Regex GarbledNamePattern(string name) =>
            new(string.Concat(name.EnumerateRunes().Select(r => $"(?:{Regex.Escape(r.ToString())}|{Garbled}+)")));
    }
}
