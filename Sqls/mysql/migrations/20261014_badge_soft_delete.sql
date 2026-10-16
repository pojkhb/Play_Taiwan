-- 勳章改成軟刪除：管理員後台刪除勳章（DELETE /api/Admin/Badges/{badgeId}）只把 is_active 設為 0
-- 已刪除的勳章不會出現在徽章圖鑑、首頁徽章數，也抽不到；玩家的收集紀錄（au_badge）保留，還原後就會回來
-- 已經建好的資料庫執行一次即可；新建的資料庫直接用 schema.sql + seed_reference.sql，不用跑這支
--
--   mysql -u root -p play_taiwan_db < Sqls/mysql/migrations/20261014_badge_soft_delete.sql

ALTER TABLE `badge`
  ADD COLUMN `is_active` tinyint NOT NULL DEFAULT '1' COMMENT '是否啟用：1＝啟用、0＝已刪除（管理員後台軟刪除）' AFTER `b_image`;
