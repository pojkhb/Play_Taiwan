using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using backend.dao;
using backend.Models;
using backend.Services;

namespace backend.Services
{
    public class TaskGenerationService
    {
        private readonly TaskDao _taskDao;
        private readonly IAiTaskClient _aiTaskClient;

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

            [5] = @"你是一個台灣文化探索遊戲的任務設計師。

景點資訊：
{story_content}

請根據以上景點資訊，設計一道「協作解謎型」的任務描述。此題型設計為 2 人以上隊伍共同完成。

要求：
- 設計一個需要「分工合作」才能解開的情境，例如：一人觀察A處，另一人觀察B處，合力拼出答案
- 謎題應與景點的歷史、建築或文化特色相關（如：碑文、年份、對聯文字等）
- 語氣充滿挑戰感與趣味性，強調「一個人做不到，但兩個人就能解開」
- 需說明玩家最終要輸入什麼格式的答案（如：四位數字、一個人名）
- 繁體中文，字數 60–100 字",

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

        // 需要呼叫 generate/choice 的題型代碼（有文字選項）
        private static readonly HashSet<int> _choiceTypeIds = new() { 6 };

        // 不需 AI 生成的題型（GPS 定位型 / 商家後台維護）
        private static readonly HashSet<int> _skipAiTypeIds = new() { 1, 9, 10 };

        public TaskGenerationService(TaskDao taskDao, IAiTaskClient aiTaskClient)
        {
            _taskDao = taskDao;
            _aiTaskClient = aiTaskClient;
        }

        public async Task<List<TaskDetailResponse>> GenerateTasksForNodeAsync(TaskListReq req, string placeId, string storyId)
        {
            var placeTypes = _taskDao.GetPlaceTypes(placeId);
            if (placeTypes == null || placeTypes.Count == 0)
                throw new InvalidOperationException($"景點 {placeId} 在 md_place_type 找不到任何任務類型設定。");

            string placeCategory = placeTypes.FirstOrDefault()?.place_category;
            bool isLastNode = _taskDao.IsLastNodeInStory(storyId, req.node_id);

            // 決定此節點要生成哪幾種題型
            List<int> tasksToGenerate = new List<int> { 3 }; // 創意攝影型為基本題型

            if (isLastNode) tasksToGenerate.Add(2);              // 跨關集結型：最後節點
            if (placeCategory == "2") tasksToGenerate.Add(4);   // 地方美食型：餐飲類景點
            if (req.player_count >= 2) tasksToGenerate.Add(5);  // 協作解謎型：多人同行

            // 從景點支援的隨機題型中抽一個（6~10 類）
            var availableRandomTypes = placeTypes.Where(t => t.type_id >= 6 && t.type_id <= 10).ToList();
            if (availableRandomTypes.Any())
            {
                var pickedType = availableRandomTypes[Random.Shared.Next(availableRandomTypes.Count)];
                tasksToGenerate.Add(pickedType.type_id);
            }

            // 取得劇本內容（供 AI 生成）
            string storyContent = _taskDao.GetStoryContent(req.node_id);

            var results = new List<TaskDetailResponse>();

            foreach (var typeId in tasksToGenerate.Distinct())
            {
                string typeName = _taskDao.GetTypeName(typeId);

                TaskDetailResponse generatedTask;

                if (_skipAiTypeIds.Contains(typeId))
                {
                    // 不需要 AI 生成的題型，直接建立預設任務
                    generatedTask = BuildSkipTask(req.node_id, placeId, storyId, typeId, typeName);
                }
                else
                {
                    // 組裝 AI 請求參數
                    var aiRequest = BuildAiRequest(typeId, typeName, placeId, storyContent);

                    // 依題型選擇呼叫哪支 API
                    generatedTask = _choiceTypeIds.Contains(typeId)
                        ? await CallChoiceApiAsync(aiRequest, req.node_id, placeId, storyId, typeName)
                        : await CallDescriptionApiAsync(aiRequest, req.node_id, placeId, storyId, typeName);
                }

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
            string promptTemplate = _promptTemplates.TryGetValue(typeId, out var tmpl)
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
            AiTaskRequest aiRequest, string nodeId, string placeId, string storyId, string typeName)
        {
            var task = CreateBaseTask(nodeId, placeId, storyId, typeName);

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
                Console.WriteLine($"[TaskGenerationService] generate/description 失敗: {ex.Message}");
            }

            return task;
        }

        /// <summary>
        /// 呼叫 generate/choice API（選擇題型）並轉換為 TaskDetailResponse（含選項）。
        /// </summary>
        private async Task<TaskDetailResponse> CallChoiceApiAsync(
            AiTaskRequest aiRequest, string nodeId, string placeId, string storyId, string typeName)
        {
            var task = CreateBaseTask(nodeId, placeId, storyId, typeName);

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
                Console.WriteLine($"[TaskGenerationService] generate/choice 失敗: {ex.Message}");
            }

            return task;
        }

        // =====================================================================
        // 不需 AI 生成的題型（直接返回預設文字）
        // =====================================================================

        private TaskDetailResponse BuildSkipTask(string nodeId, string placeId, string storyId, int typeId, string typeName)
        {
            var task = CreateBaseTask(nodeId, placeId, storyId, typeName);

            task.task_describe = typeId switch
            {
                1  => "請前往指定區域完成 GPS 定位打卡。",
                9  => "(商家知識問答) 請依現場商家資訊作答。",
                10 => "(圖像地理猜謎型) 請根據圖像選出正確位置。",
                _  => $"({typeName}) 請依現場情況完成任務。"
            };

            return task;
        }

        // =====================================================================
        // 共用基礎任務建立
        // =====================================================================

        private TaskDetailResponse CreateBaseTask(string nodeId, string placeId, string storyId, string typeName)
        {
            return new TaskDetailResponse
            {
                story_id     = storyId,
                node_id      = nodeId,
                task_place_id = placeId,
                task_type    = typeName,
                options      = new List<TaskOption>(),
                media_urls   = new List<string>()
            };
        }
    }
}