-- 遊你說了算（POST /api/Story/GenerateByText）改成先回 job_id，前端再用 GET /api/Story/GenerateByText/{job_id} 輪詢到完成
-- 這張表記下每個 AI 工作是誰送的、目前狀態、存成哪個劇本：只有本人查得到，生成完成後重複輪詢也只會存一份劇本
-- 已經建好的資料庫執行一次即可；新建的資料庫直接用 schema.sql + seed_reference.sql，不用跑這支
--
--   mysql -u root -p play_taiwan_db < Sqls/mysql/migrations/20261016_story_text_job.sql

CREATE TABLE IF NOT EXISTS `story_text_job` (
  `job_id` varchar(64) COLLATE utf8mb4_general_ci NOT NULL COMMENT 'AI 背景工作代號',
  `au_id` int NOT NULL COMMENT '送出工作的使用者',
  `user_prompt` text COLLATE utf8mb4_general_ci NOT NULL COMMENT '使用者輸入的一句話',
  `status` tinyint NOT NULL DEFAULT '1' COMMENT '1＝生成中、2＝存檔中、3＝完成、4＝失敗',
  `s_id` int DEFAULT NULL COMMENT '存成的劇本（完成後才有）',
  `result_json` mediumtext COLLATE utf8mb4_general_ci COMMENT '完成時 AI 回傳的解析條件與劇本內容，重複輪詢時原樣回傳',
  `error_message` varchar(500) COLLATE utf8mb4_general_ci DEFAULT NULL COMMENT '失敗原因',
  `created_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT '送出時間',
  `updated_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP COMMENT '最後更新時間',
  PRIMARY KEY (`job_id`) USING BTREE,
  KEY `idx_stj_au_id` (`au_id`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci ROW_FORMAT=DYNAMIC COMMENT='遊你說了算的 AI 生成工作';
