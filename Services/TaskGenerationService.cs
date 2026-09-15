using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using backend.dao;
using backend.Models;
using backend.Services;
using Microsoft.Extensions.Logging;

namespace backend.Services
{
    public class TaskGenerationService
    {
        private readonly TaskDao _taskDao;
        private readonly IAiTaskClient _aiTaskClient;
        private readonly ILogger<TaskGenerationService> _logger;

        // =====================================================================
        // 各題型的 task_prompt 範本
        // 對應 AI_Task_API_Spec.md 第五節（初步設計，僅供參考）
        // =====================================================================
        private static readonly Dictionary<int, string> _promptTemplates = new()
        {
            [2] = @"你是一個台灣文化探索遊戲的任務設計師。

景點資訊：
{story_content}

請根據以上景點資訊，設計一道「跨關集結型」的最終任務描述。
此任務情境為：玩家已完成整段故事旅程，來到終點景點，需綜合沿途蒐集到的線索，推理並輸入最終答案。

要求：
- 引導玩家回想旅途中的某個關鍵元素（如一段文字、一個數字、一個圖案）
- 語氣充滿懸疑與儀式感，如同謎底即將揭曉的一刻
- 不可直接給出答案，只描述「玩家需要做什麼」
- 繁體中文，字數 60–100 字",

            [3] = @"你是一個台灣文化探索遊戲的任務設計師。

景點資訊：
{story_content}

請根據以上景點資訊，設計一道「創意攝影型」的任務描述。

要求：
- 要求玩家在景點中找到特定主題（如某個建築細節、光影角落、顏色特徵）進行拍攝
- 需融入景點的文化或歷史意象，讓玩家透過鏡頭感受景點精神
- 語氣活潑親切，讓玩家清楚知道「要拍什麼」以及「怎麼拍」
- 繁體中文，字數 50–100 字",

            [4] = @"你是一個台灣文化探索遊戲的任務設計師。

景點資訊：
{story_content}

請根據以上景點資訊，設計一道「地方美食型」的任務描述。

要求：
- 引導玩家在景點附近找到一道具代表性的在地美食或伴手禮
- 可點出該地區的知名食材、料理方式或歷史典故，增添在地知識感
- 要求玩家拍下美食或與美食的合照後上傳
- 語氣輕鬆，帶有食物香氣感，讓人想去品嚐
- 繁體中文，字數 50–80 字",

            [6] = @"你是一個台灣文化探索遊戲的任務設計師。

景點資訊：
{story_content}

請根據以上景點資訊，生成一道「文化問答型」的單選題。

要求：
- 題幹：一個與景點文化、歷史或建築直接相關的問題，30–50 字
- 選項：固定四個選項（A、B、C、D），每個選項 10–20 字
- 必須恰好只有 1 個正確答案，其餘 3 個為合理但具迷惑性的錯誤選項
- 繁體中文輸出",

            [7] = @"你是一個台灣文化探索遊戲的任務設計師。

景點資訊：
{story_content}

請根據以上景點資訊，生成一道「景點猜猜樂」的題目描述。
此題型的答題方式是：玩家從 4 張圖片中，選出一張屬於當前景點的圖片（圖片由系統自動提供）。

要求：
- 題幹需引導玩家「從四張圖片中辨識哪一張是當前景點」
- 描述帶有懸疑或趣味性，讓玩家需要認真觀察才能辨別
- 不需要說明圖片內容，圖片由系統提供
- 繁體中文，字數 30–60 字",

            [8] = @"你是一個台灣文化探索遊戲的任務設計師。

景點資訊：
{story_content}

請根據以上景點資訊，設計一道「e 人訪談型」的任務描述。

要求：
- 引導玩家在景點附近找到一位路人、攤販或店家進行簡短訪談（約 1–2 分鐘）
- 需提供 1–2 個具體的訪談問題建議（與景點文化或在地生活相關）
- 語氣輕鬆幽默，讓玩家感覺這是一項有趣的「採訪任務」而不是尷尬的事
- 需說明玩家要錄製影音後上傳作答
- 繁體中文，字數 60–100 字",
        };

        // 協作解謎型（type_id=5）專用 prompt 範本：一次生成兩位玩家各自的線索與共同的最終答案，
        // 跟其他題型的 task_prompt 分開維護，因為呼叫的是專屬的 generate/coop API，回傳結構也不同。
        private const string CoopPromptTemplate = @"你是一個台灣文化探索遊戲的任務設計師。

景點資訊：
{story_content}

請根據以上景點資訊，設計一道「協作解謎型」的任務。此題型由兩位玩家在各自手機上看到不同線索，
需要口頭交流、互相補足資訊，才能合力推出同一組正確答案。

要求：
- 分別設計「玩家 A 線索」與「玩家 B 線索」兩段文字，各自只包含部分資訊，缺一不可
- 謎題應與景點的歷史、建築或文化特色相關（如：碑文、年份、對聯文字等）
- 語氣充滿挑戰感與趣味性，強調「一個人做不到，但兩個人就能解開」
- 明確定義一個簡短、格式固定的正確答案（如：四位數字、一個地名），答案本身不可出現在任一段線索文字中
- 繁體中文，兩段線索各 40–80 字";

        // 需要呼叫 generate/choice 的題型代碼（有文字選項，選項由 AI 生成）
        private static readonly HashSet<int> _choiceTypeIds = new() { 6 };

        // 不需 AI 生成的題型（商家後台維護）
        private static readonly HashSet<int> _skipAiTypeIds = new() { 9, 10 };

        // 選項為圖片、不經 AI 篩選的題型（景點猜猜樂：題幹仍由 AI 生成，但選項圖片直接從 Neo4j 抓）
        private static readonly HashSet<int> _imageOptionTypeIds = new() { 7 };

        // 需要呼叫 generate/coop 的題型代碼（雙人分別線索 + 共同答案）
        private static readonly HashSet<int> _coopTypeIds = new() { 5 };

        // 景點猜猜樂：圖片選項總數（1 張正確 + N 張錯誤）
        private const int ImageOptionCount = 4;

        public TaskGenerationService(
            TaskDao taskDao,
            IAiTaskClient aiTaskClient,
            ILogger<TaskGenerationService> logger)
        {
            _taskDao = taskDao;
            _aiTaskClient = aiTaskClient;
            _logger = logger;
        }

        // =====================================================================
        // 對外入口
        // 三個入口一律不對外丟例外，回傳實際成功生成的任務清單（可能為空）。
        // 呼叫端（劇本生成）不需要 try/catch，任務生成失敗不影響劇本生成的結果。
        // =====================================================================

        /// <summary>
        /// 劇本生成完畢後呼叫，為多份劇本的所有節點產生任務。
        /// </summary>
        public async Task<List<TaskDetailResponse>> GenerateTasksForStoriesAsync(IEnumerable<string> storyIds, int playerCount)
        {
            var results = new List<TaskDetailResponse>();
            if (storyIds == null) return results;

            foreach (var storyId in storyIds)
                results.AddRange(await GenerateTasksForStoryAsync(storyId, playerCount));

            return results;
        }

        /// <summary>
        /// 為單一劇本底下的所有節點產生任務。單一節點失敗會跳過並繼續處理下一個節點。
        /// </summary>
        public async Task<List<TaskDetailResponse>> GenerateTasksForStoryAsync(string storyId, int playerCount)
        {
            var results = new List<TaskDetailResponse>();
            if (string.IsNullOrWhiteSpace(storyId)) return results;

            List<TaskDao.StoryNodeRef> nodes;
            try
            {
                nodes = _taskDao.GetNodesByStoryId(storyId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "劇本 {StoryId} 查詢節點失敗，略過任務生成。", storyId);
                return results;
            }

            foreach (var node in nodes)
                results.AddRange(await GenerateForNodeSafeAsync(node, playerCount));

            _logger.LogInformation("劇本 {StoryId} 任務生成完成，共 {Count} 筆。", storyId, results.Count);
            return results;
        }

        /// <summary>
        /// 為單一節點產生任務，place_id 與 story_id 由 node_id 反查。
        /// </summary>
        public async Task<List<TaskDetailResponse>> GenerateTasksForNodeAsync(string nodeId, int playerCount)
        {
            var results = new List<TaskDetailResponse>();
            if (string.IsNullOrWhiteSpace(nodeId)) return results;

            TaskDao.StoryNodeRef node;
            try
            {
                node = _taskDao.GetNodeRef(nodeId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "節點 {NodeId} 查詢失敗，略過任務生成。", nodeId);
                return results;
            }

            if (node == null)
            {
                _logger.LogWarning("找不到節點 {NodeId}，略過任務生成。", nodeId);
                return results;
            }

            return await GenerateForNodeSafeAsync(node, playerCount);
        }

        /// <summary>
        /// 單一節點生成的容錯包裝：缺 place_id 或生成過程出錯都只寫 log，不中斷整體流程。
        /// </summary>
        private async Task<List<TaskDetailResponse>> GenerateForNodeSafeAsync(TaskDao.StoryNodeRef node, int playerCount)
        {
            if (string.IsNullOrWhiteSpace(node.place_id))
            {
                _logger.LogWarning("節點 {NodeId} 的 place_id 為空，略過任務生成。", node.node_id);
                return new List<TaskDetailResponse>();
            }

            try
            {
                var tasks = await GenerateForNodeCoreAsync(node.node_id, node.place_id, node.story_id, playerCount);
                return tasks ?? new List<TaskDetailResponse>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "節點 {NodeId} 任務生成失敗，略過此節點。", node.node_id);
                return new List<TaskDetailResponse>();
            }
        }

        // =====================================================================
        // 生成核心邏輯
        // =====================================================================

        private async Task<List<TaskDetailResponse>> GenerateForNodeCoreAsync(
            string nodeId, string placeId, string storyId, int playerCount)
        {
            var placeTypes = _taskDao.GetPlaceTypes(placeId);
            if (placeTypes == null || placeTypes.Count == 0)
                throw new InvalidOperationException($"景點 {placeId} 在 md_place_type 找不到任何任務類型設定。");

            string placeCategory = placeTypes.FirstOrDefault()?.place_category;
            bool isLastNode = _taskDao.IsLastNodeInStory(storyId, nodeId);

            // 決定此節點要生成哪幾種題型
            List<int> tasksToGenerate = new List<int> { 3 }; // 創意攝影型為基本題型

            if (isLastNode) tasksToGenerate.Add(2);              // 跨關集結型：最後節點
            if (placeCategory == "2") tasksToGenerate.Add(4);   // 地方美食型：餐飲類景點
            if (playerCount >= 2) tasksToGenerate.Add(5);       // 協作解謎型：多人同行

            // 從景點支援的隨機題型中抽一個（6~10 類）
            var availableRandomTypes = placeTypes.Where(t => t.type_id >= 6 && t.type_id <= 10).ToList();
            if (availableRandomTypes.Any())
            {
                var pickedType = availableRandomTypes[Random.Shared.Next(availableRandomTypes.Count)];
                tasksToGenerate.Add(pickedType.type_id);
            }

            // 取得劇本內容（供 AI 生成）
            string storyContent = _taskDao.GetStoryContent(nodeId);

            var results = new List<TaskDetailResponse>();

            foreach (var typeId in tasksToGenerate.Distinct())
            {
                string typeName = _taskDao.GetTypeName(typeId);

                TaskDetailResponse generatedTask;

                if (_skipAiTypeIds.Contains(typeId))
                {
                    // 不需要 AI 生成的題型，直接建立預設任務
                    generatedTask = BuildSkipTask(nodeId, placeId, storyId, typeId, typeName);
                }
                else if (_coopTypeIds.Contains(typeId))
                {
                    // 協作解謎型：獨立的請求/回應結構（兩位玩家各自線索 + 共同答案），走專屬 API
                    var coopRequest = BuildAiRequest(typeId, typeName, placeId, storyContent);
                    generatedTask = await CallCoopApiAsync(coopRequest, nodeId, placeId, storyId, typeId, typeName);
                }
                else
                {
                    // 組裝 AI 請求參數
                    var aiRequest = BuildAiRequest(typeId, typeName, placeId, storyContent);

                    // 依題型選擇呼叫哪支 API
                    generatedTask = _choiceTypeIds.Contains(typeId)
                        ? await CallChoiceApiAsync(aiRequest, nodeId, placeId, storyId, typeId, typeName)
                        : await CallDescriptionApiAsync(aiRequest, nodeId, placeId, storyId, typeId, typeName);
                }

                // 景點猜猜樂：選項不經 AI，直接從 Neo4j 抓景點圖片組成選項
                if (_imageOptionTypeIds.Contains(typeId))
                    await PopulateImageOptionsAsync(generatedTask, placeId);

                // 寫入資料庫
                int newTaskId = _taskDao.InsertTask(generatedTask, typeId);
                generatedTask.task_id = newTaskId;

                if (generatedTask.options != null && generatedTask.options.Count > 0)
                    _taskDao.InsertTaskOptions(newTaskId, generatedTask.options);

                results.Add(generatedTask);
            }

            return results;
        }

        // =====================================================================
        // 組裝 AiTaskRequest
        // =====================================================================

        /// <summary>
        /// 組裝傳給 AI 的 Request，將 story_content 帶入 task_prompt 範本中。
        /// </summary>
        private AiTaskRequest BuildAiRequest(int typeId, string typeName, string placeUid, string storyContent)
        {
            string promptTemplate = _coopTypeIds.Contains(typeId)
                ? CoopPromptTemplate
                : _promptTemplates.TryGetValue(typeId, out var tmpl)
                    ? tmpl
                    : $"你是台灣文化探索遊戲的任務設計師，請根據以下景點資訊生成一道適合玩家的任務描述：\n\n{{story_content}}";

            string taskPrompt = promptTemplate.Replace("{story_content}", storyContent);

            return new AiTaskRequest
            {
                type_id      = typeId,
                task_type    = typeName,
                place_uid    = placeUid,
                story_content = storyContent,
                task_prompt  = taskPrompt
            };
        }

        // =====================================================================
        // 呼叫 AI API 並將結果轉為 TaskDetailResponse
        // =====================================================================

        /// <summary>
        /// 呼叫 generate/description API（無選項型）並轉換為 TaskDetailResponse。
        /// </summary>
        private async Task<TaskDetailResponse> CallDescriptionApiAsync(
            AiTaskRequest aiRequest, string nodeId, string placeId, string storyId, int typeId, string typeName)
        {
            var task = CreateBaseTask(nodeId, placeId, storyId, typeId, typeName);

            try
            {
                var aiResponse = await _aiTaskClient.GenerateDescriptionAsync(aiRequest);

                task.task_describe = aiResponse.success && !string.IsNullOrWhiteSpace(aiResponse.task_describe)
                    ? aiResponse.task_describe
                    : $"[AI 生成失敗] ({typeName}) 請直接前往景點探索並依現場情況完成任務。";
            }
            catch (Exception ex)
            {
                // 生成失敗時用 fallback 文字，不中斷流程
                task.task_describe = $"[AI 生成失敗] ({typeName}) 請直接前往景點探索並依現場情況完成任務。";
                _logger.LogError(ex, "generate/description 失敗（題型 {TypeName}），改用 fallback 文字。", typeName);
            }

            return task;
        }

        /// <summary>
        /// 呼叫 generate/choice API（選擇題型）並轉換為 TaskDetailResponse（含選項）。
        /// </summary>
        private async Task<TaskDetailResponse> CallChoiceApiAsync(
            AiTaskRequest aiRequest, string nodeId, string placeId, string storyId, int typeId, string typeName)
        {
            var task = CreateBaseTask(nodeId, placeId, storyId, typeId, typeName);

            try
            {
                var aiResponse = await _aiTaskClient.GenerateChoiceAsync(aiRequest);

                if (aiResponse.success && !string.IsNullOrWhiteSpace(aiResponse.task_describe))
                {
                    task.task_describe = aiResponse.task_describe;

                    // 將 AI 回傳的選項轉為 TaskOption
                    if (aiResponse.options != null)
                    {
                        foreach (var opt in aiResponse.options)
                        {
                            task.options.Add(new TaskOption
                            {
                                option_key  = opt.option_key,
                                option_text = opt.option_text,
                                option_url  = null,
                                is_correct  = opt.is_correct
                            });
                        }
                    }
                }
                else
                {
                    task.task_describe = $"[AI 生成失敗] ({typeName}) 請問關於此景點，下列何者正確？";
                }
            }
            catch (Exception ex)
            {
                task.task_describe = $"[AI 生成失敗] ({typeName}) 請問關於此景點，下列何者正確？";
                _logger.LogError(ex, "generate/choice 失敗（題型 {TypeName}），改用 fallback 文字。", typeName);
            }

            return task;
        }

        /// <summary>
        /// 呼叫 generate/coop API（協作解謎型）並轉換為 TaskDetailResponse。
        /// task_describe 存玩家 A 的線索、task_describe_b 存玩家 B 的線索，
        /// TaskService.GetTask 會依 player_index 決定要把哪一段換到 task_describe 回傳給前端。
        /// correct_answer 供 SubmitAnswer 核對玩家提交的 text_answer，不回傳給前端。
        /// </summary>
        private async Task<TaskDetailResponse> CallCoopApiAsync(
            AiTaskRequest aiRequest, string nodeId, string placeId, string storyId, int typeId, string typeName)
        {
            var task = CreateBaseTask(nodeId, placeId, storyId, typeId, typeName);

            const string fallbackA = "[AI 生成失敗] (協作解謎型) 請與隊友互相描述你所在位置看到的細節，合力找出答案。";
            const string fallbackB = "[AI 生成失敗] (協作解謎型) 請與隊友互相描述你所在位置看到的細節，合力找出答案。";

            try
            {
                var aiResponse = await _aiTaskClient.GenerateCoopAsync(aiRequest);

                bool ok = aiResponse.success
                    && !string.IsNullOrWhiteSpace(aiResponse.task_describe_a)
                    && !string.IsNullOrWhiteSpace(aiResponse.task_describe_b)
                    && !string.IsNullOrWhiteSpace(aiResponse.correct_answer);

                task.task_describe   = ok ? aiResponse.task_describe_a : fallbackA;
                task.task_describe_b = ok ? aiResponse.task_describe_b : fallbackB;
                // 生成失敗時沒有可信的正確答案，correct_answer 留空；
                // TaskVerificationService 會把「沒有 correct_answer」視為此題暫時無法核對答案。
                task.correct_answer  = ok ? aiResponse.correct_answer.Trim() : null;
            }
            catch (Exception ex)
            {
                task.task_describe   = fallbackA;
                task.task_describe_b = fallbackB;
                task.correct_answer  = null;
                _logger.LogError(ex, "generate/coop 失敗（題型 {TypeName}），改用 fallback 文字。", typeName);
            }

            return task;
        }

        // =====================================================================
        // 景點猜猜樂：選項圖片（不經 AI，直接從 Neo4j 抓景點照片）
        // =====================================================================

        /// <summary>
        /// 為「景點猜猜樂」題目組出圖片選項：1 張正確景點照片（依 place_id 從 Neo4j 隨機挑一張）
        /// + N 張隨機錯誤景點照片，洗牌後依序標上 A/B/C/D。
        /// 找不到圖片時只記 log、不丟例外，此題會沒有選項（前端沿用既有 fallback 顯示）。
        /// </summary>
        private async Task PopulateImageOptionsAsync(TaskDetailResponse task, string placeId)
        {
            try
            {
                var correctImages = await _taskDao.GetPlaceImagesAsync(placeId);
                if (correctImages == null || correctImages.Count == 0)
                {
                    _logger.LogWarning("景點 {PlaceId} 在 Neo4j 找不到任何圖片，景點猜猜樂題目略過選項。", placeId);
                    return;
                }

                string correctUrl = correctImages[Random.Shared.Next(correctImages.Count)];

                var decoyUrls = await _taskDao.GetRandomDecoyImagesAsync(placeId, ImageOptionCount - 1);

                var shuffledUrls = new List<string> { correctUrl };
                shuffledUrls.AddRange(decoyUrls);
                shuffledUrls = shuffledUrls.OrderBy(_ => Random.Shared.Next()).ToList();

                string[] keys = { "A", "B", "C", "D" };
                for (int i = 0; i < shuffledUrls.Count && i < keys.Length; i++)
                {
                    task.options.Add(new TaskOption
                    {
                        option_key  = keys[i],
                        option_text = $"照片 {keys[i]}",
                        option_url  = shuffledUrls[i],
                        is_correct  = shuffledUrls[i] == correctUrl
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "景點 {PlaceId} 抓取景點猜猜樂圖片選項失敗，此題將沒有選項。", placeId);
            }
        }

        // =====================================================================
        // 不需 AI 生成的題型（直接返回預設文字）
        // =====================================================================

        private TaskDetailResponse BuildSkipTask(string nodeId, string placeId, string storyId, int typeId, string typeName)
        {
            var task = CreateBaseTask(nodeId, placeId, storyId, typeId, typeName);

            task.task_describe = typeId switch
            {
                9  => "(商家知識問答) 請依現場商家資訊作答。",
                10 => "(圖像地理猜謎型) 請根據圖像選出正確位置。",
                _  => $"({typeName}) 請依現場情況完成任務。"
            };

            return task;
        }

        // =====================================================================
        // 共用基礎任務建立
        // =====================================================================

        private TaskDetailResponse CreateBaseTask(string nodeId, string placeId, string storyId, int typeId, string typeName)
        {
            return new TaskDetailResponse
            {
                story_id     = storyId,
                node_id      = nodeId,
                task_place_id = placeId,
                type_id      = typeId,
                task_type    = typeName,
                options      = new List<TaskOption>(),
                media_urls   = new List<string>()
            };
        }
    }
}