// 路線規劃沒帶交通方式時，用生成劇本時使用者選的交通方式（story.sd_transport）
using Allure.Net.Commons.Attributes;
using backend.Services;

namespace TrafficSystem.Tests.Route;

[AllureSuiteHierarchy("交通路網", "單元測試")]
[AllureBddHierarchy("交通路網", "單元測試")]
public class RouteTransportTests
{
    [Theory]
    [InlineData("步行,機車", new[] { "步行", "機車" })]
    [InlineData("步行、公車、捷運", new[] { "步行", "公車", "捷運" })]
    [InlineData(" 自行車 , 開車 ", new[] { "腳踏車", "汽車" })]        // 前端的別名統一成畫面上的名稱
    [InlineData("步行,步行,自駕,汽車", new[] { "步行", "汽車" })]      // 重複的只留一個
    [InlineData("客製化交通", new string[0])]                          // 遊你說了算生成的劇本沒有選交通方式
    [InlineData("", new string[0])]
    [InlineData(null, new string[0])]
    public void 劇本存的交通方式轉成路線規劃用的清單(string stored, string[] expected)
    {
        Assert.Equal(expected, RoutePlanService.StoryTransports(stored));
    }
}
