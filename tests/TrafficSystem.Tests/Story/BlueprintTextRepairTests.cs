// 遊你說算存檔前的亂碼修復（BlueprintTextRepair）：LLM 把罕用字寫成 �，用 Neo4j 的正確景點名稱換回來
using backend.Services;
using backend.ViewModels;
using Allure.Net.Commons.Attributes;

namespace TrafficSystem.Tests.Story;

[AllureSuiteHierarchy("劇本", "單元測試")]
[AllureBddHierarchy("劇本", "單元測試")]
public class BlueprintTextRepairTests
{
    private static ScriptBlueprintData Blueprint(string placeName, string opening, string synopsis = "") => new()
    {
        title = "城南的失落卷軸",
        synopsis = synopsis,
        nodes = new List<ScriptBlueprintNode>
        {
            new() { node_order = 1, spot_uuid = "uid-1", place_name = placeName, node_title = "民宿的故事", dialogues = new ScriptBlueprintDialogues { opening = opening, success = "完成" } }
        }
    };

    [Fact]
    public void 景點名稱裡變成亂碼的字換回正確名稱()
    {
        ScriptBlueprintData data = Blueprint("�居青衖", "入住�居青衖，聽從古老的故事...", "最後住進簃居�衖。");

        BlueprintTextRepair.Repair(data, new Dictionary<string, string> { ["uid-1"] = "簃居青衖" });

        Assert.Equal("簃居青衖", data.nodes[0].place_name);
        Assert.Equal("入住簃居青衖，聽從古老的故事...", data.nodes[0].dialogues.opening);
        Assert.Equal("最後住進簃居青衖。", data.synopsis);
    }

    [Fact]
    public void 一個字被拆成好幾個亂碼也對得回去()
    {
        ScriptBlueprintData data = Blueprint("璿旅", "今晚住在��旅。");

        BlueprintTextRepair.Repair(data, new Dictionary<string, string> { ["uid-1"] = "璿旅" });

        Assert.Equal("今晚住在璿旅。", data.nodes[0].dialogues.opening);
    }

    [Fact]
    public void 對不回景點名稱的亂碼直接拿掉()
    {
        ScriptBlueprintData data = Blueprint("璿旅", "古老的��故事。");

        BlueprintTextRepair.Repair(data, new Dictionary<string, string> { ["uid-1"] = "璿旅" });

        Assert.Equal("古老的故事。", data.nodes[0].dialogues.opening);
    }

    [Fact]
    public void Neo4j查不到景點時保留AI的名稱_只拿掉亂碼()
    {
        ScriptBlueprintData data = Blueprint("�居青衖", "入住�居青衖。");

        BlueprintTextRepair.Repair(data, new Dictionary<string, string>());

        Assert.Equal("居青衖", data.nodes[0].place_name);
        Assert.Equal("入住居青衖。", data.nodes[0].dialogues.opening);
    }

    [Fact]
    public void 沒有亂碼的文字不變()
    {
        ScriptBlueprintData data = Blueprint("簃居青衖", "入住簃居青衖，聽從古老的故事...");

        BlueprintTextRepair.Repair(data, new Dictionary<string, string> { ["uid-1"] = "簃居青衖" });

        Assert.Equal("入住簃居青衖，聽從古老的故事...", data.nodes[0].dialogues.opening);
        Assert.Equal("城南的失落卷軸", data.title);
    }
}
