// 此檔案保留原本的 Fake 介面與實作，僅為了不破壞 Startup.cs 的依賴注入 (DI)。
// 我們已將任務流程簡化，這些介面目前無實際作用。
using System.Collections.Generic;

namespace backend.Services
{
    public interface IVisionApiClient { (string Label, double Confidence)[] AnnotateImage(string photoUrl); }
    public class FakeVisionApiClient : IVisionApiClient
    {
        public (string Label, double Confidence)[] AnnotateImage(string photoUrl) => new[] { ("placeholder", 0.99) };
    }

    public interface IPoseCompareClient { double CompareToReference(string videoUrl, object reference); }
    public class FakePoseCompareClient : IPoseCompareClient
    {
        public double CompareToReference(string videoUrl, object reference) => 0.75;
    }

    public interface ISpeechToTextClient { string Transcribe(string audioUrl); }
    public class FakeSpeechToTextClient : ISpeechToTextClient
    {
        public string Transcribe(string audioUrl) => "";
    }

    public interface IQrTokenStore { bool IsAlreadyUsed(string token, string epId); void MarkUsed(string token, string epId); }
    public class InMemoryQrTokenStore : IQrTokenStore
    {
        public bool IsAlreadyUsed(string token, string epId) => false;
        public void MarkUsed(string token, string epId) {}
    }
}