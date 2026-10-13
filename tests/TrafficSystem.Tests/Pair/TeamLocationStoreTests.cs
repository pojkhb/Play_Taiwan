// 隊友位置：每個人只留最後一筆位置，太久沒回報就當作沒有位置
using Allure.Net.Commons.Attributes;
using backend.Services;

namespace TrafficSystem.Tests.Pair;

[AllureSuiteHierarchy("協作解謎", "單元測試")]
[AllureBddHierarchy("協作解謎", "單元測試")]
public class TeamLocationStoreTests
{
    private static readonly DateTime Noon = new(2026, 10, 13, 4, 0, 0, DateTimeKind.Utc);

    private static TeamLocationStore.Entry At(double lat, DateTime time) => new() { lat = lat, lng = 120.68, updated_at = time };

    [Fact]
    public void 每個人只留最後回報的那一筆_不同劇本各自分開()
    {
        var store = new TeamLocationStore();

        store.Report(1, 100, At(24.10, Noon));
        store.Report(1, 100, At(24.20, Noon.AddSeconds(5)));
        store.Report(2, 100, At(25.00, Noon));

        Assert.Equal(24.20, store.Get(1, 100, Noon.AddSeconds(6)).lat);
        Assert.Equal(25.00, store.Get(2, 100, Noon.AddSeconds(6)).lat);
        Assert.Null(store.Get(1, 200, Noon));   // 沒回報過
    }

    [Fact]
    public void 超過三十分鐘沒回報_就當作沒有位置()
    {
        var store = new TeamLocationStore();
        store.Report(1, 100, At(24.10, Noon));

        Assert.NotNull(store.Get(1, 100, Noon.AddMinutes(29)));
        Assert.Null(store.Get(1, 100, Noon.AddMinutes(30)));
    }

    [Fact]
    public void 過期的位置會被清掉_新的位置不受影響()
    {
        var store = new TeamLocationStore();
        store.Report(1, 100, At(24.10, Noon));

        // 四十分鐘後另一個人回報：順便清掉過期的，再回報回來時也拿不到舊位置
        store.Report(1, 200, At(24.30, Noon.AddMinutes(40)));

        Assert.Null(store.Get(1, 100, Noon));
        Assert.Equal(24.30, store.Get(1, 200, Noon.AddMinutes(41)).lat);
    }
}
