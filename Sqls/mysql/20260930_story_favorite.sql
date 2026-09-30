-- 劇本加上「喜愛」欄位：使用者可以把喜歡的劇本標成喜愛，在「我喜愛的劇本」清單看到，也可以再加入 Google 行事曆
-- 已經建好的資料庫執行一次即可；新建的資料庫直接用 schema.sql，不用跑這支
--
--   mysql -u root -p play_taiwan_db < Sqls/mysql/20260930_story_favorite.sql

ALTER TABLE `story`
  ADD COLUMN `is_favorite` tinyint NOT NULL DEFAULT '0' COMMENT '是否為使用者喜愛的劇本：1＝喜愛、0＝否' AFTER `is_night_mode`,
  ADD KEY `idx_story_au_favorite` (`au_id`,`is_favorite`) USING BTREE;
