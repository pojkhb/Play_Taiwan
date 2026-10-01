-- 劇本 NPC：AI 生成劇本時一起產生的角色（名稱、身分、自我介紹），每份劇本一個
-- story.npc_id、story_node.npc_id 原本就有，只是沒有可以對應的表；這支補上 npc 表
-- 已經建好的資料庫執行一次即可；新建的資料庫直接用 schema.sql，不用跑這支
--
--   mysql -u root -p play_taiwan_db < Sqls/mysql/20261001_story_npc.sql

CREATE TABLE IF NOT EXISTS `npc` (
  `npc_id` int NOT NULL AUTO_INCREMENT COMMENT 'NPC 流水號（story.npc_id、story_node.npc_id 對應這裡）',
  `npc_name` varchar(100) COLLATE utf8mb4_general_ci NOT NULL COMMENT 'NPC 名稱',
  `npc_role` varchar(255) COLLATE utf8mb4_general_ci DEFAULT NULL COMMENT 'NPC 身分／角色設定',
  `npc_intro` text COLLATE utf8mb4_general_ci COMMENT 'NPC 自我介紹',
  `created_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT '建立時間',
  PRIMARY KEY (`npc_id`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci ROW_FORMAT=DYNAMIC COMMENT='劇本 NPC（AI 生成劇本時一起產生，每份劇本一個）';
