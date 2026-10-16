-- 現在揪出發（POST /api/Story/GenerateGameStory）改成先回 job_id，前端再用 GET /api/Story/GenerateGameStory/{job_id} 輪詢到完成
-- 這張表記下每個生成工作是誰送的、送給 AI 的每份劇本條件與 AI 任務代號、目前狀態、存好的劇本：只有本人查得到，生成完成後重複輪詢也只會存一次
-- 已經建好的資料庫執行一次即可；新建的資料庫直接用 schema.sql + seed_reference.sql，不用跑這支
--
--   mysql -u root -p play_taiwan_db < Sqls/mysql/migrations/20261016_story_game_job.sql

CREATE TABLE IF NOT EXISTS `story_game_job` (
  `job_id` varchar(64) COLLATE utf8mb4_general_ci NOT NULL COMMENT '生成工作代號（後端產生）',
  `au_id` int NOT NULL COMMENT '送出工作的使用者',
  `status` tinyint NOT NULL DEFAULT '1' COMMENT '1＝生成中、2＝存檔中、3＝完成、4＝失敗',
  `plan_json` mediumtext COLLATE utf8mb4_general_ci NOT NULL COMMENT '送給 AI 的每份劇本條件、AI 任務代號、每站預計抵達時間、商家題目等存檔要用的資料',
  `result_json` mediumtext COLLATE utf8mb4_general_ci COMMENT '存好的劇本（含 story_id、sn_id、task_id），完成後重複輪詢時原樣回傳',
  `error_message` varchar(500) COLLATE utf8mb4_general_ci DEFAULT NULL COMMENT '失敗原因',
  `created_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT '送出時間',
  `updated_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP COMMENT '最後更新時間',
  PRIMARY KEY (`job_id`) USING BTREE,
  KEY `idx_sgj_au_id` (`au_id`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci ROW_FORMAT=DYNAMIC COMMENT='現在揪出發的劇本生成工作';
