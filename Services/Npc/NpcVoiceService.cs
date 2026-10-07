// 檔案路徑：System\Services\Npc\NpcVoiceService.cs
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using backend.dao;
using backend.Models;
using backend.utils;
using backend.ViewModels;

namespace backend.Services
{
    /// <summary>NPC 語音：把文字（劇情前傳、對話）交給外部 AI 服務轉成 mp3</summary>
    public class NpcVoiceService
    {
        private const string SpeakPath = "/api/npc/speak";
        public const string DefaultVoice = "zh-TW-HsiaoChenNeural";
        public const int MaxTextLength = 1000;

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly StoryDao _storyDao;

        public NpcVoiceService(IHttpClientFactory httpClientFactory, StoryDao storyDao)
        {
            _httpClientFactory = httpClientFactory;
            _storyDao = storyDao;
        }

        public async Task<NpcSpeakResponse> SpeakAsync(string text, string voice)
        {
            text = text?.Trim();
            if (string.IsNullOrEmpty(text))
                throw new Exception("請提供要唸的文字 (text)");
            if (text.Length > MaxTextLength)
                throw new Exception($"文字最多 {MaxTextLength} 字");
            voice = string.IsNullOrWhiteSpace(voice) ? DefaultVoice : voice.Trim();

            HttpClient client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromMinutes(2);
            using var form = new FormUrlEncodedContent(new Dictionary<string, string> { ["text"] = text, ["voice"] = voice });
            using HttpResponseMessage response = await client.PostAsync(AiServiceConfig.Url(SpeakPath), form);
            NpcSpeakApiResponse result = await VlogAiGateway.ReadJsonAsync<NpcSpeakApiResponse>(response, "產生 NPC 語音");

            if (string.IsNullOrWhiteSpace(result?.download_url))
                throw new Exception($"AI 服務沒有回傳語音檔（status={result?.status}，{result?.message}）");

            return new NpcSpeakResponse { text = text, voice = voice, audio_url = VlogAiGateway.ResolveUrl(result.download_url) };
        }

        /// <summary>唸劇本的劇情前傳（story_prologue，沒有時用簡介）；沒指定聲音時用劇本 NPC 的聲線</summary>
        public async Task<NpcSpeakResponse> SpeakPrologueAsync(int storyId, string voice)
        {
            StoryDetailResponse story = _storyDao.GetDetail(storyId);   // 找不到劇本會丟出錯誤
            if (string.IsNullOrWhiteSpace(story.preface))
                throw new Exception("這個劇本沒有劇情前傳");
            return await SpeakAsync(story.preface, string.IsNullOrWhiteSpace(voice) ? story.npc?.voice : voice);
        }
    }
}
