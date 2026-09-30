// 劇本回顧旁白（StoryRecapService.BuildStoryNarration）：依各站劇情組成，超過語音上限時縮短
using backend.Services;

namespace TrafficSystem.Tests.Story;

public class RecapNarrationTests
{
    [Fact]
    public void 旁白依序唸出每一站的景點與劇情()
    {
        var stops = new List<(string, string)> { ("臺中州廳", "家書的第一頁找到了。"), ("臺中公園", "湖心亭的倒影藏著線索。") };

        string text = StoryRecapService.BuildStoryNarration("州廳鐘聲裡的家書", stops);

        Assert.Equal("「州廳鐘聲裡的家書」旅程回顧。第1站，臺中州廳。家書的第一頁找到了。第2站，臺中公園。湖心亭的倒影藏著線索。"
                     + "2站的冒險到這裡告一段落，謝謝你走完這趟旅程。", text);
    }

    [Fact]
    public void 太長時每站只留第一句()
    {
        var stops = Enumerable.Range(1, 8)
            .Select(i => ($"景點{i}", $"第一句重點。{new string('長', 150)}。"))
            .ToList<(string, string)>();

        string text = StoryRecapService.BuildStoryNarration("很長的劇本", stops);

        Assert.True(text.Length <= 1000, $"{text.Length} 字");
        Assert.Contains("第8站，景點8。第一句重點。", text);
        Assert.DoesNotContain("長長長", text);
    }

    [Fact]
    public void 第一句還是太長就截斷在上限內()
    {
        var stops = Enumerable.Range(1, 10).Select(i => ($"景點{i}", new string('字', 300))).ToList<(string, string)>();

        string text = StoryRecapService.BuildStoryNarration("超長劇本", stops);

        Assert.Equal(1000, text.Length);
        Assert.EndsWith("…", text);
    }
}
