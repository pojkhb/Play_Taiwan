-- 地圖改用迷霧（不再用景點剪影）：剪影的兩張表換成一張迷霧圖表 `fog`
-- 未解鎖的站顯示景點照片的霧化版，後端在生成劇本（或第一次打開地圖）時自動產生
-- 已經建好的資料庫執行一次即可；新建的資料庫直接用 schema.sql + seed_reference.sql，不用跑這支
--
--   mysql -u root -p play_taiwan_db < Sqls/mysql/20260930_silhouette_to_fog.sql
--
-- 注意：會刪除 silhouette、story_node_silhouette 兩張表與裡面的剪影資料

DROP TABLE IF EXISTS `story_node_silhouette`;
DROP TABLE IF EXISTS `silhouette`;

CREATE TABLE IF NOT EXISTS `fog` (
  `fog_id` int NOT NULL AUTO_INCREMENT COMMENT '迷霧圖流水號',
  `fog_name` varchar(255) COLLATE utf8mb4_general_ci NOT NULL COMMENT '迷霧圖名稱',
  `fog_image` text COLLATE utf8mb4_general_ci NOT NULL COMMENT '迷霧圖網址：/ 開頭是後端 wwwroot 的檔案，也可以填完整網址',
  `fog_source_image` text COLLATE utf8mb4_general_ci COMMENT '做成這張迷霧圖的原始景點照片網址；NULL＝通用迷霧圖',
  `fog_source_key` char(40) COLLATE utf8mb4_general_ci DEFAULT NULL COMMENT '原始照片網址的 SHA-1，同一張照片只做一次迷霧圖',
  `is_default` tinyint NOT NULL DEFAULT '0' COMMENT '1＝景點沒有照片（或迷霧圖還沒做好）時用的通用迷霧圖',
  `created_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT '建立時間',
  `updated_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP COMMENT '最後更新時間',
  PRIMARY KEY (`fog_id`) USING BTREE,
  UNIQUE KEY `uk_fog_source_key` (`fog_source_key`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci ROW_FORMAT=DYNAMIC COMMENT='地圖迷霧圖：未解鎖的站顯示景點照片的霧化版（取代原本的剪影資料表）';

INSERT INTO `fog` (`fog_name`, `fog_image`, `is_default`)
SELECT '預設迷霧', '/images/fog/default_fog.png', 1 FROM DUAL
WHERE NOT EXISTS (SELECT 1 FROM `fog`);
