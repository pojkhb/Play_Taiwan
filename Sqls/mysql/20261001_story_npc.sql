-- NPC 名單：薯光、珍奶奶、阿達力、墨先生、霓霓、阿吉伯六位固定角色（名稱、原型、身分、自我介紹、圖片、語音聲線）
-- AI 生成劇本時從名單挑一位，story.npc_id、story_node.npc_id 記錄用了哪一位；沒有指定時用預設的薯光
-- 已經建好的資料庫執行一次即可（重複執行會更新成最新的 NPC 資料）；新建的資料庫直接用 schema.sql + seed_reference.sql
--
--   mysql -u root -p play_taiwan_db < Sqls/mysql/20261001_story_npc.sql

CREATE TABLE IF NOT EXISTS `npc` (
  `npc_id` int NOT NULL AUTO_INCREMENT COMMENT 'NPC 流水號（story.npc_id、story_node.npc_id 對應這裡）',
  `npc_name` varchar(50) COLLATE utf8mb4_general_ci NOT NULL COMMENT 'NPC 名稱（AI 生成劇本時回傳這個名字來挑 NPC）',
  `npc_origin` varchar(100) COLLATE utf8mb4_general_ci DEFAULT NULL COMMENT '角色原型（名字的諧音梗），例如「黑糖珍珠奶茶」',
  `npc_role` varchar(255) COLLATE utf8mb4_general_ci DEFAULT NULL COMMENT 'NPC 身分／角色設定',
  `npc_intro` text COLLATE utf8mb4_general_ci COMMENT 'NPC 自我介紹',
  `npc_avatar` varchar(255) COLLATE utf8mb4_general_ci DEFAULT NULL COMMENT 'NPC 圖片路徑（wwwroot 底下），例如 /images/npc/shuguang.png',
  `npc_voice` varchar(50) COLLATE utf8mb4_general_ci DEFAULT NULL COMMENT 'NPC 語音的聲線（AI 文字轉語音用），例如 zh-TW-HsiaoChenNeural',
  `is_default` tinyint NOT NULL DEFAULT '0' COMMENT '是否為預設 NPC：劇本沒有指定 NPC 時用這個（1＝是、0＝否）',
  `created_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT '建立時間',
  PRIMARY KEY (`npc_id`) USING BTREE,
  UNIQUE KEY `uk_npc_name` (`npc_name`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci ROW_FORMAT=DYNAMIC COMMENT='NPC 名單（固定角色，AI 生成劇本時從這裡挑一位）';

INSERT INTO `npc` (`npc_id`, `npc_name`, `npc_origin`, `npc_role`, `npc_intro`, `npc_avatar`, `npc_voice`, `is_default`) VALUES
  (1,'薯光','番薯（曙光）','帶領旅人探索台灣的嚮導','嗨！我是薯光，曙光一出來我就出發啦！跟著我，一起把台灣的故事一站一站挖出來吧！','/images/npc/shuguang.png','zh-TW-HsiaoChenNeural',1),
  (2,'珍奶奶','黑糖珍珠奶茶','掌管地方數十年的記憶與失落古老配方的守密人','乖孫，來來來，奶奶這杯裡的每一顆珍珠，都藏著一段老故事和一道快失傳的古早味。','/images/npc/zhen-nainai.png','zh-TW-HsiaoChenNeural',0),
  (3,'阿達力','新竹貢丸（大粒）','機靈靈通的在地走透透達人，熟悉大街小巷與美食情報','欸你來對人了！哪條巷子有好吃的、哪家要排隊，問我阿達力就對啦！','/images/npc/a-da-li.png','zh-TW-YunJheNeural',0),
  (4,'墨先生','墨魚','博學嚴謹的文史工作者，擅長解讀古地圖與歷史檔案','在下墨先生。每張古地圖都留著前人的筆跡，且讓我們循著墨痕，找出被遺忘的歷史。','/images/npc/mo-xian-sheng.png','zh-TW-YunJheNeural',0),
  (5,'霓霓','芋泥（霓虹）','對美感與光影極度敏銳的街頭藝術家，專門引導光影觀察與夜遊探索','天黑了才是城市最美的時候！跟著我的光，一起把街角的影子畫下來吧～','/images/npc/ni-ni.png','zh-TW-HsiaoYuNeural',0),
  (6,'阿吉伯','淡水阿給','外冷內熱的傳統工藝老師傅，重視手作與傳承','哼，手作的東西急不來……既然來了，阿伯就教你兩手吧。','/images/npc/a-ji-bo.png','zh-TW-YunJheNeural',0)
ON DUPLICATE KEY UPDATE
  `npc_origin` = VALUES(`npc_origin`), `npc_role` = VALUES(`npc_role`), `npc_intro` = VALUES(`npc_intro`),
  `npc_avatar` = VALUES(`npc_avatar`), `npc_voice` = VALUES(`npc_voice`), `is_default` = VALUES(`is_default`);
