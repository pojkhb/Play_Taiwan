-- =====================================================================
-- 整體測試資料：一份可以從頭玩到尾的劇本「【整體測試】舊城鐘聲尋寶記」
--
-- 內容：臺中舊城區 4 站、11 題任務（10 種任務類型都有）、一間商家（題庫＋優惠券）、4 個周邊好去地點。
-- 用來把「確認劇本 → 地圖 → 路線 → 抵達 → 作答 → 破關 → 完成 → 勳章 → 過往紀錄」整個流程走一遍。
--
-- 用法：
--   1. 預設掛在第一個遊客帳號（auth_type = 1）底下。要指定帳號，把下面 @au_id 那一行等號右邊改成你的 au_id
--   2. mysql -u root -p 資料庫名稱 < TestData_FullFlow.sql
--   3. 可以重複執行，每次都會把這份劇本的進度、作答紀錄清掉重建，回到還沒開始玩的狀態
--
-- 每一題的答案在最下面的「答案對照表」。
-- 不需要 Neo4j：景點用測試代號（fullflow-place-0X、fullflow-store-01）對應到 MySQL 的 place 表。
-- =====================================================================

-- 資料表用的是 utf8mb4_general_ci，連線也指定成一樣，字串比對才不會出現「Illegal mix of collations」
SET NAMES utf8mb4 COLLATE utf8mb4_general_ci;

-- 測試程式會先設定 @fullflow_au_id；手動執行時用第一個遊客帳號
SET @au_id = COALESCE(@fullflow_au_id, (SELECT MIN(au_id) FROM auth WHERE auth_type = 1 AND is_active = 1));

SET @title        = '【整體測試】舊城鐘聲尋寶記';
SET @merchant_mail = 'fullflow-merchant@example.com';
SET @store_name   = '【測試】第二市場茶棧';

-- ───────────────────────── 1. 清掉上一次的資料 ─────────────────────────

DROP TEMPORARY TABLE IF EXISTS tmp_ff_story;
CREATE TEMPORARY TABLE tmp_ff_story AS SELECT s_id FROM story WHERE story_title = @title AND au_id = @au_id;
DROP TEMPORARY TABLE IF EXISTS tmp_ff_node;
CREATE TEMPORARY TABLE tmp_ff_node AS SELECT sn_id FROM story_node WHERE s_id IN (SELECT s_id FROM tmp_ff_story);
DROP TEMPORARY TABLE IF EXISTS tmp_ff_task;
CREATE TEMPORARY TABLE tmp_ff_task AS SELECT task_id FROM task WHERE story_id IN (SELECT s_id FROM tmp_ff_story);
DROP TEMPORARY TABLE IF EXISTS tmp_ff_record;
CREATE TEMPORARY TABLE tmp_ff_record AS SELECT record_id FROM user_task_record WHERE task_id IN (SELECT task_id FROM tmp_ff_task);

DELETE FROM record_media       WHERE record_id IN (SELECT record_id FROM tmp_ff_record);
DELETE FROM user_task_record   WHERE record_id IN (SELECT record_id FROM tmp_ff_record);
DELETE FROM task_option        WHERE task_id IN (SELECT task_id FROM tmp_ff_task);
DELETE FROM task_clue          WHERE task_id IN (SELECT task_id FROM tmp_ff_task);
DELETE FROM task               WHERE task_id IN (SELECT task_id FROM tmp_ff_task);
-- 暫存表在同一句裡不能用兩次，分兩句刪
DELETE FROM story_node_transit WHERE from_sn_id IN (SELECT sn_id FROM tmp_ff_node);
DELETE FROM story_node_transit WHERE to_sn_id IN (SELECT sn_id FROM tmp_ff_node);
DELETE FROM story_node         WHERE sn_id IN (SELECT sn_id FROM tmp_ff_node);
DELETE FROM story_session      WHERE s_id IN (SELECT s_id FROM tmp_ff_story);
DELETE FROM story_tag          WHERE s_id IN (SELECT s_id FROM tmp_ff_story);
DELETE FROM postcard           WHERE s_id IN (SELECT s_id FROM tmp_ff_story);
DELETE FROM au_badge           WHERE s_id IN (SELECT s_id FROM tmp_ff_story);
DELETE FROM au_vlog            WHERE s_id IN (SELECT s_id FROM tmp_ff_story);
DELETE FROM story_pair_member  WHERE pair_id IN (SELECT pair_id FROM story_pair_session WHERE s_id IN (SELECT s_id FROM tmp_ff_story));
DELETE FROM story_pair_session WHERE s_id IN (SELECT s_id FROM tmp_ff_story);
DELETE FROM story              WHERE s_id IN (SELECT s_id FROM tmp_ff_story);

-- 商家題庫、優惠券（整組重建）
DROP TEMPORARY TABLE IF EXISTS tmp_ff_store;
CREATE TEMPORARY TABLE tmp_ff_store AS SELECT s_id FROM store WHERE store_uid = 'fullflow-store-01';
DELETE FROM question_option WHERE question_id IN (SELECT question_id FROM store_question WHERE store_id IN (SELECT s_id FROM tmp_ff_store));
DELETE FROM store_question  WHERE store_id IN (SELECT s_id FROM tmp_ff_store);
DELETE FROM coupon          WHERE s_id IN (SELECT s_id FROM tmp_ff_store);
DELETE FROM store           WHERE s_id IN (SELECT s_id FROM tmp_ff_store);

DELETE FROM place_type WHERE place_id LIKE 'fullflow-place-%' OR place_id = 'fullflow-store-01';
DELETE FROM place      WHERE p_name IN ('【測試】第二市場茶棧', '【測試】中山路豆花', '【測試】綠川水岸廊道', '【測試】中山綠橋');

-- ───────────────────────── 2. 景點（MySQL place）─────────────────────────
-- 四站用真實景點；資料庫已經有同名景點就沿用（不覆蓋），沒有才新增。

DROP TEMPORARY TABLE IF EXISTS tmp_ff_place;
CREATE TEMPORARY TABLE tmp_ff_place (
  uid varchar(64), p_name varchar(255), p_category varchar(255), p_type varchar(255), p_summary text, p_introduction text,
  p_address varchar(255), lat decimal(10,7), lng decimal(10,7), p_open_time varchar(255), p_image text
) DEFAULT CHARSET = utf8mb4 COLLATE = utf8mb4_general_ci;

INSERT INTO tmp_ff_place VALUES
('fullflow-place-01', '宮原眼科', 'Attraction', '古蹟', '日治時期的眼科診所，現在是知名的冰淇淋與糕餅店',
 '宮原眼科建於 1927 年，是日籍眼科博士宮原武熊開設的診所，曾是臺中規模最大的眼科。紅磚拱廊與挑高的書牆是最大的特色。',
 '臺中市中區中山路20號', 24.1378509, 120.6835697, '10:00-21:00',
 'https://upload.wikimedia.org/wikipedia/commons/thumb/2/21/Miyahara_Eye_Hospital_in_March_2026.jpg/960px-Miyahara_Eye_Hospital_in_March_2026.jpg'),
('fullflow-place-02', '第二市場', 'Attraction', '市場', '百年市場，六角樓是它的招牌',
 '第二市場建於 1917 年，日治時期稱為「新富町市場」，以中央的六角樓為中心向外放射出三條通道，至今仍是臺中人的早餐廚房。',
 '臺中市中區三民路二段87號', 24.1423646, 120.6787413, '07:00-18:00',
 'https://upload.wikimedia.org/wikipedia/commons/thumb/1/1b/%E8%87%BA%E4%B8%AD%E7%AC%AC%E4%BA%8C%E5%B8%82%E5%A0%B4%E5%85%AD%E8%A7%92%E6%A8%93.JPG/960px-%E8%87%BA%E4%B8%AD%E7%AC%AC%E4%BA%8C%E5%B8%82%E5%A0%B4%E5%85%AD%E8%A7%92%E6%A8%93.JPG'),
('fullflow-place-03', '臺中州廳', 'Attraction', '古蹟', '日治時期的臺中州行政中心，國定古蹟',
 '臺中州廳由森山松之助設計，1913 年起分期興建，白色的馬薩式屋頂與正面的圓拱門廊是它最醒目的樣子。',
 '臺中市西區民權路99號', 24.1385594, 120.6784293, '09:00-17:00',
 'https://upload.wikimedia.org/wikipedia/commons/thumb/2/26/Taichung_Prefectural_Hall_2022.jpg/960px-Taichung_Prefectural_Hall_2022.jpg'),
('fullflow-place-04', '臺中文學館', 'Attraction', '古蹟', '日治時期的警察宿舍群，現在是文學主題園區',
 '臺中文學館原是 1932 年興建的警察宿舍，園區裡有一棵百年老榕樹，六棟日式木造建築改成常設展館與主題餐廳。',
 '臺中市西區樂群街38號', 24.1395787, 120.6729394, '10:00-17:00',
 'https://upload.wikimedia.org/wikipedia/commons/thumb/b/b2/Taichung_Literature_Museum-01.2024-12-06.jpg/960px-Taichung_Literature_Museum-01.2024-12-06.jpg');

INSERT INTO place (p_name, p_category, p_type, p_summary, p_introduction, p_address, p_latitude, p_longitude, p_open_time, p_image)
SELECT t.p_name, t.p_category, t.p_type, t.p_summary, t.p_introduction, t.p_address, t.lat, t.lng, t.p_open_time, t.p_image
FROM tmp_ff_place t
WHERE NOT EXISTS (SELECT 1 FROM place p WHERE p.p_name = t.p_name);

-- 已經有同名景點時，只補上空的欄位（介紹、營業時間、照片）
UPDATE place p
INNER JOIN tmp_ff_place t ON t.p_name = p.p_name
SET p.p_summary      = COALESCE(NULLIF(p.p_summary, ''), t.p_summary),
    p.p_introduction = COALESCE(NULLIF(p.p_introduction, ''), t.p_introduction),
    p.p_open_time    = COALESCE(NULLIF(p.p_open_time, ''), t.p_open_time),
    p.p_image        = COALESCE(NULLIF(p.p_image, ''), t.p_image),
    p.p_latitude     = COALESCE(p.p_latitude, t.lat),
    p.p_longitude    = COALESCE(p.p_longitude, t.lng);

-- 周邊好去用的地點（名稱都是「【測試】」開頭，重跑時整批重建）
INSERT INTO place (p_name, p_category, p_type, p_summary, p_address, p_latitude, p_longitude, p_open_time) VALUES
('【測試】第二市場茶棧', '飲食', '茶飲', '市場裡的老茶攤，有優惠券可以領', '臺中市中區三民路二段87號', 24.1421500, 120.6789000, '00:00-23:59'),
('【測試】中山路豆花',   '飲食', '甜品', '宮原眼科旁邊的手工豆花',         '臺中市中區中山路32號',     24.1381000, 120.6831000, '00:00-23:59'),
('【測試】綠川水岸廊道', '其他', '河岸', '沿著綠川散步的步道',             '臺中市中區綠川東街',       24.1384000, 120.6842000, NULL),
('【測試】中山綠橋',     '其他', '橋梁', '日治時期留下的老橋，舊名新盛橋', '臺中市中區中山路',         24.1376000, 120.6839500, NULL);

-- ───────────────────────── 3. 景點可以出的任務類型（place_type）─────────────────────────
-- story_node.place_id（景點代號）→ place_type.place_name → place.p_name，地圖、路線、任務都靠這個對應找到座標與照片。

SET @pt := (SELECT COALESCE(MAX(place_type_id), 0) FROM place_type);

INSERT INTO place_type (place_type_id, place_id, place_name, place_category, type_id)
SELECT @pt := @pt + 1, t.uid, t.p_name, 'Attraction', ty.type_id
FROM tmp_ff_place t
CROSS JOIN (SELECT 6 AS type_id UNION ALL SELECT 7 UNION ALL SELECT 8) ty
ORDER BY t.uid, ty.type_id;

-- 市場裡的茶棧是商家自己的地點（餐廳，可以出地方美食型任務），優惠券靠這個代號對應到店家
INSERT INTO place_type (place_type_id, place_id, place_name, place_category, type_id)
VALUES (@pt := @pt + 1, 'fullflow-store-01', '【測試】第二市場茶棧', 'Restaurant', 4);

-- ───────────────────────── 4. 商家、題庫、優惠券 ─────────────────────────
-- 商家帳號只是用來掛店家資料，密碼是亂碼，不能登入。

INSERT INTO auth (auth_name, auth_type, auth_email, auth_pswd, is_active, is_email_verified)
SELECT '整體測試商家', 2, @merchant_mail, 'not-a-real-password', 1, 1
WHERE NOT EXISTS (SELECT 1 FROM auth WHERE auth_email = @merchant_mail);

SET @merchant_au = (SELECT MIN(au_id) FROM auth WHERE auth_email = @merchant_mail);

INSERT INTO store (au_id, store_name, store_dec, store_address, store_city, store_town, store_uid)
VALUES (@merchant_au, @store_name, '第二市場裡的老茶攤，古早味紅茶一喝就是六十年。', '三民路二段87號', '臺中市', '中區', 'fullflow-store-01');
SET @store_id = LAST_INSERT_ID();

INSERT INTO store_question (store_id, question_describe) VALUES (@store_id, '茶棧的招牌古早味紅茶，是用哪一種方式煮的？');
SET @question_id = LAST_INSERT_ID();

INSERT INTO question_option (question_id, option_context, is_correct, option_key) VALUES
(@question_id, '冷泡一整夜',     0, 'A'),
(@question_id, '炭火大鍋慢煮',   1, 'B'),
(@question_id, '義式咖啡機萃取', 0, 'C');

INSERT INTO coupon (s_id, coupon_code, coupon_name, discount_commodity, discount_type, discount_value, valid_from, valid_to, status)
VALUES (@store_id, 'FULLFLOW10', '闖關紅茶九折', '古早味紅茶', 'percent', 10, NOW() - INTERVAL 1 DAY, NOW() + INTERVAL 365 DAY, 'active');

-- ───────────────────────── 5. 劇本與四站 ─────────────────────────

INSERT INTO story (au_id, npc_id, city_name, district_name, party_size, sd_transport, story_title, story_prologue, story_synopsis,
                   story_badge, story_postcards, is_active, is_night_mode)
VALUES (@au_id, NULL, '臺中市', '中區', 2, '步行,公車',
        @title,
        '舊城的鐘聲已經停了很久。有人在車站前留下一張泛黃的藏寶圖，上面只有四個代號——據說把它們走完，就能讓鐘聲再響一次。',
        '從車站前的醫生館出發，穿過百年市場與白色廳舍，最後在老榕樹下找到讓鐘聲重新響起的方法。',
        '', 4, 1, 0);
SET @story_id = LAST_INSERT_ID();

INSERT INTO story_tag (s_id, s_tag) VALUES (@story_id, '歷史人文'), (@story_id, '美食');

INSERT INTO story_node (s_id, npc_id, place_id, sn_order, sn_title, is_hidden, is_night_only, is_active,
                        location_codename, sn_opening_text, sn_success_text, sn_task_type) VALUES
(@story_id, NULL, 'fullflow-place-01', 1, '醫生館的第一道線索', 2, 0, 2, '紅磚醫生館',
 '你到了！這棟紅磚樓以前可是一間眼科診所，醫生把第一道線索藏在拱廊下。',
 '線索到手了：下一張紙條指向一棟有六個角的樓。', '文化問答型'),
(@story_id, NULL, 'fullflow-place-02', 2, '六角樓下的早餐', 2, 0, 2, '六角樓',
 '聞到香味了嗎？這座市場已經熱鬧了一百多年，線索就藏在最老的茶攤裡。',
 '茶攤老闆笑著遞來一張紙條：去找那座白色的廳舍。', '景點猜猜樂'),
(@story_id, NULL, 'fullflow-place-03', 3, '白色廳舍的密碼', 2, 0, 2, '白色廳舍',
 '好氣派的白色建築！以前整個臺中州的大事都在這裡決定，門廊上藏著一組年份密碼。',
 '密碼解開了，最後一站在一棵百年老榕樹下。', '圖像地理猜謎型'),
(@story_id, NULL, 'fullflow-place-04', 4, '老榕樹下的鐘聲', 2, 0, 2, '老榕樹宿舍',
 '就是這裡了，老榕樹旁邊的日式宿舍。把一路上的線索拼起來，鐘聲就會再響。',
 '噹——舊城的鐘聲又響了。你把整張藏寶圖都走完了！', '跨關集結型');

SET @n1 = (SELECT sn_id FROM story_node WHERE s_id = @story_id AND sn_order = 1);
SET @n2 = (SELECT sn_id FROM story_node WHERE s_id = @story_id AND sn_order = 2);
SET @n3 = (SELECT sn_id FROM story_node WHERE s_id = @story_id AND sn_order = 3);
SET @n4 = (SELECT sn_id FROM story_node WHERE s_id = @story_id AND sn_order = 4);

-- ───────────────────────── 6. 任務（10 種類型都有）─────────────────────────
-- task_option.option_id 不是自動編號，接著目前最大的號碼編

SET @opt := (SELECT COALESCE(MAX(option_id), 0) FROM task_option);

-- 第 1 站：GPS 定位（1）、文化問答（6）、創意攝影（3）
INSERT INTO task (story_id, node_id, task_type, task_describe, correct_answer, task_hint)
VALUES (@story_id, @n1, 1, '走到紅磚拱廊下，按下定位確認你到了。', NULL, '在現場按送出就可以了。');

INSERT INTO task (story_id, node_id, task_type, task_describe, correct_answer, task_hint)
VALUES (@story_id, @n1, 6, '這棟建築在日治時期原本是什麼？', NULL, '看看建築的名字，最後兩個字就是答案。');
SET @t := LAST_INSERT_ID();
INSERT INTO task_option (option_id, task_id, option_context, is_correct, option_key) VALUES
(@opt := @opt + 1, @t, '銀行',     0, 'A'),
(@opt := @opt + 1, @t, '眼科診所', 1, 'B'),
(@opt := @opt + 1, @t, '郵局',     0, 'C');

INSERT INTO task (story_id, node_id, task_type, task_describe, correct_answer, task_hint)
VALUES (@story_id, @n1, 3, '跟紅磚拱廊拍一張有創意的合照。', NULL, '任何一張照片都可以，重點是拱廊要入鏡。');

-- 第 2 站：景點猜猜樂（7）、地方美食（4）、商家知識問答（9，題目來自商家題庫）
INSERT INTO task (story_id, node_id, task_type, task_describe, correct_answer, task_hint)
VALUES (@story_id, @n2, 7, '市場正中央那棟樓，從上面看是什麼形狀？', NULL, '代號就告訴你了。');
SET @t := LAST_INSERT_ID();
INSERT INTO task_option (option_id, task_id, option_context, is_correct, option_key) VALUES
(@opt := @opt + 1, @t, '六角形', 1, 'A'),
(@opt := @opt + 1, @t, '圓形',   0, 'B'),
(@opt := @opt + 1, @t, '正方形', 0, 'C');

INSERT INTO task (story_id, node_id, task_type, task_describe, correct_answer, task_hint)
VALUES (@story_id, @n2, 4, '在市場裡找一樣你想吃的小吃，拍下來。', NULL, '魯肉飯、菜頭粿、紅茶都可以。');

INSERT INTO task (story_id, node_id, task_type, question_id, task_describe, correct_answer, task_hint)
VALUES (@story_id, @n2, 9, @question_id, NULL, NULL, NULL);

-- 第 3 站：圖像地理猜謎（10）、e 人訪談（8）、協作解謎（5，兩個座位各一條線索）
INSERT INTO task (story_id, node_id, task_type, task_describe, correct_answer, task_hint)
VALUES (@story_id, @n3, 10, '照片裡的白色圓拱門廊，是這棟建築的哪一面？', NULL, '站在大門口抬頭看。');
SET @t := LAST_INSERT_ID();
INSERT INTO task_option (option_id, task_id, option_context, is_correct, option_key) VALUES
(@opt := @opt + 1, @t, '背面的倉庫', 0, 'A'),
(@opt := @opt + 1, @t, '側面的車庫', 0, 'B'),
(@opt := @opt + 1, @t, '正面的入口', 1, 'C');

INSERT INTO task (story_id, node_id, task_type, task_describe, correct_answer, task_hint)
VALUES (@story_id, @n3, 8, '找一位路過的人，問他對這棟建築的印象，錄下來。', NULL, '錄音或錄影都可以。');

INSERT INTO task (story_id, node_id, task_type, task_describe, correct_answer, task_hint)
VALUES (@story_id, @n3, 5, '兩個人把各自的線索合起來，輸入這棟建築開始興建的西元年。', '1913', '一個人知道前兩碼，另一個人知道後兩碼。');
SET @t := LAST_INSERT_ID();
INSERT INTO task_clue (task_id, seat_no, clue_text) VALUES
(@t, 1, '你的線索：年份的前兩碼是 19。'),
(@t, 2, '你的線索：年份的後兩碼是 13。');

-- 第 4 站（最後一站）：跨關集結（2）、文化問答（6）
INSERT INTO task (story_id, node_id, task_type, task_describe, correct_answer, task_hint)
VALUES (@story_id, @n4, 2, '把四站的線索串起來，寫下你覺得鐘聲為什麼會停。', NULL, '有寫就會過，沒有標準答案。');

INSERT INTO task (story_id, node_id, task_type, task_describe, correct_answer, task_hint)
VALUES (@story_id, @n4, 6, '這個園區在日治時期原本是誰住的宿舍？', NULL, '跟維持治安有關。');
SET @t := LAST_INSERT_ID();
INSERT INTO task_option (option_id, task_id, option_context, is_correct, option_key) VALUES
(@opt := @opt + 1, @t, '警察', 1, 'A'),
(@opt := @opt + 1, @t, '教師', 0, 'B'),
(@opt := @opt + 1, @t, '醫生', 0, 'C');

DROP TEMPORARY TABLE IF EXISTS tmp_ff_story, tmp_ff_node, tmp_ff_task, tmp_ff_record, tmp_ff_store, tmp_ff_place;

-- ───────────────────────── 7. 建好了什麼 ─────────────────────────

SELECT @story_id AS story_id, @au_id AS au_id, @title AS story_title,
       (SELECT COUNT(*) FROM story_node WHERE s_id = @story_id) AS 站數,
       (SELECT COUNT(*) FROM task WHERE story_id = @story_id) AS 任務數,
       (SELECT COUNT(DISTINCT task_type) FROM task WHERE story_id = @story_id) AS 任務類型數;

-- =====================================================================
-- 答案對照表（作答時 gps_lat、gps_lon 帶那一站的座標，要在 200 公尺內）
--
-- 站  景點（代號）              座標                     任務類型            怎麼答
-- 1   宮原眼科（紅磚醫生館）    24.1378509, 120.6835697  1 GPS 區域定位型    不用帶作答欄位
--                                                        6 文化問答型        selected_option_key = B
--                                                        3 創意攝影型        photo_url = 上傳後的網址
-- 2   第二市場（六角樓）        24.1423646, 120.6787413  7 景點猜猜樂        selected_option_key = A
--                                                        4 地方美食型        photo_url = 上傳後的網址
--                                                        9 商家知識問答      selected_option_key = B
-- 3   臺中州廳（白色廳舍）      24.1385594, 120.6784293  10 圖像地理猜謎型   selected_option_key = C
--                                                        8 e 人訪談型        audio_url 或 video_url = 上傳後的網址
--                                                        5 協作解謎型        text_answer = 1913
-- 4   臺中文學館（老榕樹宿舍）  24.1395787, 120.6729394  2 跨關集結型        text_answer = 任何文字
--                                                        6 文化問答型        selected_option_key = A
-- =====================================================================
