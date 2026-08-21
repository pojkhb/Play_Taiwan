using System.Collections.Generic;
using backend.Models;

namespace backend.Services
{
    /// <summary>假的 Vision API 實作，先讓系統能編譯執行，之後接 Google Cloud Vision 再替換。</summary>
    public class FakeVisionApiClient : IVisionApiClient
    {
        public (string Label, double Confidence)[] AnnotateImage(string photoUrl)
        {
            // 暫時一律回傳「符合」，方便前端先串接測試
            return new[] { ("placeholder_label", 0.99) };
        }
    }

    /// <summary>假的姿勢比對實作，先讓系統能編譯執行，之後接 MediaPipe Pose 再替換。</summary>
    public class FakePoseCompareClient : IPoseCompareClient
    {
        public double CompareToReference(string videoUrl, PoseReference reference)
        {
            return 0.75; // 暫時固定回傳通過門檻的相似度
        }
    }

    /// <summary>假的語音轉文字實作，先讓系統能編譯執行，之後接 Speech-to-Text API 再替換。</summary>
    public class FakeSpeechToTextClient : ISpeechToTextClient
    {
        public string Transcribe(string audioUrl)
        {
            return ""; // 暫時回傳空字串，走 text_answer 手動輸入的驗證路徑
        }
    }

    /// <summary>假的 QR Code 一次性驗證實作，先用記憶體暫存，之後可換成資料庫或 Redis。</summary>
    public class InMemoryQrTokenStore : IQrTokenStore
    {
        private readonly HashSet<string> _usedTokens = new();

        public bool IsAlreadyUsed(string token, string epId)
        {
            return _usedTokens.Contains($"{token}:{epId}");
        }

        public void MarkUsed(string token, string epId)
        {
            _usedTokens.Add($"{token}:{epId}");
        }
    }
}