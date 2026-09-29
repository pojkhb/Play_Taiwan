-- 勳章記錄是從哪個劇本抽到的（POST /api/Badge/Draw）
-- 一個劇本只能抽一枚：uk_au_badge_story 擋掉同一位玩家在同一個劇本抽第二次。
-- s_id 為 NULL 的舊資料不受唯一鍵限制（MySQL 唯一鍵允許多個 NULL）。
ALTER TABLE au_badge
    ADD COLUMN s_id int DEFAULT NULL COMMENT '從哪個劇本抽到' AFTER b_id,
    ADD UNIQUE KEY uk_au_badge_story (au_id, s_id);
