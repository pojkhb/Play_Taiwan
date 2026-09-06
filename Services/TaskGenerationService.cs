using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using backend.dao;
using backend.Models;

namespace backend.Services
{
    public class TaskGenerationService
    {
        private readonly TaskDao _taskDao;

        public TaskGenerationService(TaskDao taskDao)
        {
            _taskDao = taskDao;
        }

        public async Task<List<TaskDetailResponse>> GenerateTasksForNodeAsync(TaskListReq req, string placeId, string storyId)
        {
            var placeTypes = _taskDao.GetPlaceTypes(placeId);
            if (placeTypes == null || placeTypes.Count == 0)
                throw new InvalidOperationException($"景點 {placeId} 在 md_place_type 找不到任何任務類型設定。");

            string placeCategory = placeTypes.FirstOrDefault()?.place_category;
            bool isLastNode = _taskDao.IsLastNodeInStory(storyId, req.node_id);

            List<int> tasksToGenerate = new List<int> { 3 }; 

            if (isLastNode) tasksToGenerate.Add(2);
            if (placeCategory == "2") tasksToGenerate.Add(4);
            if (req.player_count >= 2) tasksToGenerate.Add(5);

            var availableRandomTypes = placeTypes.Where(t => t.type_id >= 6 && t.type_id <= 10).ToList();
            if (availableRandomTypes.Any())
            {
                var pickedType = availableRandomTypes[Random.Shared.Next(availableRandomTypes.Count)];
                tasksToGenerate.Add(pickedType.type_id);
            }

            var results = new List<TaskDetailResponse>();

            foreach (var typeId in tasksToGenerate.Distinct())
            {
                string typeName = _taskDao.GetTypeName(typeId);
                var generatedTask = await RouteTaskGenerationAsync(req.node_id, placeId, storyId, typeId, typeName);

                int newTaskId = _taskDao.InsertTask(generatedTask, typeId);
                generatedTask.task_id = newTaskId;

                if (generatedTask.options != null && generatedTask.options.Count > 0)
                    _taskDao.InsertTaskOptions(newTaskId, generatedTask.options);

                results.Add(generatedTask);
            }

            return results;
        }

        private async Task<TaskDetailResponse> RouteTaskGenerationAsync(string nodeId, string placeId, string storyId, int typeId, string typeName)
        {
            return typeId switch
            {
                1 => await GenerateGpsAreaPositioningTask(nodeId, placeId, storyId, typeId, typeName),
                2 => await GenerateCrossLevelRallyTask(nodeId, placeId, storyId, typeId, typeName),
                3 => await GenerateCreativePhotoTask(nodeId, placeId, storyId, typeId, typeName),
                4 => await GenerateLocalFoodTask(nodeId, placeId, storyId, typeId, typeName),
                5 => await GenerateCoopPuzzleTask(nodeId, placeId, storyId, typeId, typeName),
                6 => await GenerateCulturalQaTask(nodeId, placeId, storyId, typeId, typeName),
                7 => await GenerateGuessAttractionTask(nodeId, placeId, storyId, typeId, typeName),
                8 => await GenerateExtrovertInterviewTask(nodeId, placeId, storyId, typeId, typeName),
                9 => await GenerateMerchantKnowledgeQaTask(nodeId, placeId, storyId, typeId, typeName),
                10 => await GenerateImageGeoGuessTask(nodeId, placeId, storyId, typeId, typeName),
                _ => await GenerateDefaultTask(nodeId, placeId, storyId, typeId, typeName)
            };
        }

        #region 各題型專屬生成邏輯 

        // 1: GPS  區域定位型，不會用到
        private async Task<TaskDetailResponse> GenerateGpsAreaPositioningTask(string nodeId, string placeId, string storyId, int typeId, string typeName)
        {
            var task = CreateBaseTask(nodeId, placeId, storyId, typeName);
            task.task_describe = "[待 AI 生成] (GPS區域定位型) 請前往指定區域完成定位打卡。";
            return await Task.FromResult(task);
        }

        // 2: 跨關集結型
        private async Task<TaskDetailResponse> GenerateCrossLevelRallyTask(string nodeId, string placeId, string storyId, int typeId, string typeName)
        {
            var task = CreateBaseTask(nodeId, placeId, storyId, typeName);
            task.task_describe = "[待 AI 生成] (跨關集結型) 請結合先前的線索，輸入最終的推理答案。";
            return await Task.FromResult(task);
        }

        // 3: 創意攝影型
        private async Task<TaskDetailResponse> GenerateCreativePhotoTask(string nodeId, string placeId, string storyId, int typeId, string typeName)
        {
            var task = CreateBaseTask(nodeId, placeId, storyId, typeName);
            task.task_describe = "[待 AI 生成] (創意攝影型) 請發揮創意，拍下符合情境的照片。";
            return await Task.FromResult(task);
        }

        // 4: 地方美食型
        private async Task<TaskDetailResponse> GenerateLocalFoodTask(string nodeId, string placeId, string storyId, int typeId, string typeName)
        {
            var task = CreateBaseTask(nodeId, placeId, storyId, typeName);
            task.task_describe = "[待 AI 生成] (地方美食型) 請尋找並拍下當地特色美食照片。";
            return await Task.FromResult(task);
        }

        // 5: 協作解謎型
        private async Task<TaskDetailResponse> GenerateCoopPuzzleTask(string nodeId, string placeId, string storyId, int typeId, string typeName)
        {
            var task = CreateBaseTask(nodeId, placeId, storyId, typeName);
            task.task_describe = "[待 AI 生成] (協作解謎型) 請與隊友合作解開這道謎題並輸入解答。";
            return await Task.FromResult(task);
        }

        // 6: 文化問答型
        private async Task<TaskDetailResponse> GenerateCulturalQaTask(string nodeId, string placeId, string storyId, int typeId, string typeName)
        {
            var task = CreateBaseTask(nodeId, placeId, storyId, typeName);
            task.task_describe = "[待 AI 生成] (文化問答型) 請問關於此景點的文化歷史，下列何者正確？";
            task.options.Add(new TaskOption { option_key = "A", option_text = "[待AI] 正確的文化歷史", option_url = null, is_correct = true });
            task.options.Add(new TaskOption { option_key = "B", option_text = "[待AI] 錯誤的文化歷史", option_url = null, is_correct = false });
            return await Task.FromResult(task);
        }

        // 7: 景點猜猜樂
        private async Task<TaskDetailResponse> GenerateGuessAttractionTask(string nodeId, string placeId, string storyId, int typeId, string typeName)
        {
            var task = CreateBaseTask(nodeId, placeId, storyId, typeName);
            task.task_describe = "[待 AI 生成] (景點猜猜樂) 根據下方圖片，猜猜看這是景點的哪個角落？";
            task.options.Add(new TaskOption { option_key = "A", option_text = "[待AI] 猜測角落 A", option_url = "https://example.com/mock_image_A.jpg", is_correct = true });
            task.options.Add(new TaskOption { option_key = "B", option_text = "[待AI] 猜測角落 B", option_url = null, is_correct = false });
            return await Task.FromResult(task);
        }

        // 8: e 人訪談型
        private async Task<TaskDetailResponse> GenerateExtrovertInterviewTask(string nodeId, string placeId, string storyId, int typeId, string typeName)
        {
            var task = CreateBaseTask(nodeId, placeId, storyId, typeName);
            task.task_describe = "[待 AI 生成] (e 人訪談型) 請找一位路人或店家進行簡短訪談，並錄製上傳影音檔。";
            return await Task.FromResult(task);
        }

        // 9: 商家知識問答
        private async Task<TaskDetailResponse> GenerateMerchantKnowledgeQaTask(string nodeId, string placeId, string storyId, int typeId, string typeName)
        {
            var task = CreateBaseTask(nodeId, placeId, storyId, typeName);
            task.task_describe = "[待 AI 生成] (商家知識問答) 請問關於這間特色商家的知識，下列何者正確？";
            task.options.Add(new TaskOption { option_key = "A", option_text = "[待AI] 正確商家知識", option_url = null, is_correct = true });
            task.options.Add(new TaskOption { option_key = "B", option_text = "[待AI] 錯誤商家知識", option_url = null, is_correct = false });
            return await Task.FromResult(task);
        }

        // 10: 圖像地理猜謎型
        private async Task<TaskDetailResponse> GenerateImageGeoGuessTask(string nodeId, string placeId, string storyId, int typeId, string typeName)
        {
            var task = CreateBaseTask(nodeId, placeId, storyId, typeName);
            task.task_describe = "[待 AI 生成] (圖像地理猜謎型) 根據這張地理圖像，選出正確的位置。";
            task.options.Add(new TaskOption { option_key = "A", option_text = "[待AI] 正確地理位置", option_url = "https://example.com/mock_geo_A.jpg", is_correct = true });
            task.options.Add(new TaskOption { option_key = "B", option_text = "[待AI] 錯誤地理位置", option_url = null, is_correct = false });
            return await Task.FromResult(task);
        }

        // 預設型
        private async Task<TaskDetailResponse> GenerateDefaultTask(string nodeId, string placeId, string storyId, int typeId, string typeName)
        {
            var task = CreateBaseTask(nodeId, placeId, storyId, typeName);
            task.task_describe = "[待 AI 生成] 這是類型 {typeId} ({typeName}) 的任務。";
            return await Task.FromResult(task);
        }

        #endregion

        private TaskDetailResponse CreateBaseTask(string nodeId, string placeId, string storyId, string typeName)
        {
            return new TaskDetailResponse
            {
                story_id = storyId,
                node_id = nodeId,
                task_place_id = placeId,
                task_type = typeName,
                options = new List<TaskOption>(),
                media_urls = new List<string>()
            };
        }
    }
}