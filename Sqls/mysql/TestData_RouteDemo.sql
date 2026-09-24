-- ============================================================================
-- 劇本交通路線展示用測試資料（Play Taiwan）
--
-- 匯入順序：
--   1. CreateMetroTables.sql    捷運資料表
--   2. MetroData.sql            全台捷運 / 輕軌資料（TDX）
--   3. BusData_Taichung.sql     臺中市公車 + 台灣好行（已經用後端同步過的可以不匯）
--   4. 本檔                     景點座標 + 3 個測試劇本（含節點、任務、選項、線索）
--
-- 3 個劇本剛好展示三種情況：
--   州廳鐘聲裡的家書（臺中舊城區）：有公車、沒有捷運 → 捷運反灰
--   綠線上的時光膠囊（臺中七期/南屯）：捷運綠線 + 公車都能用
--   捷運迷城的懸案（臺北）：捷運可轉乘；沒有匯入臺北公車時公車反灰
--
-- 可以重複匯入：景點同名就不重複新增；同名的測試劇本會先刪掉再重建。
-- ============================================================================

-- 劇本擁有者：改成你的 au_id（登入後 /api/Route/Stories 會先列自己的劇本）
SET @au_id = 7;

SET NAMES utf8mb4;
START TRANSACTION;

-- ── 1. 景點座標（place）──
-- story_node.place_id 是 Neo4j uuid，後端用 place_type（uuid → 名稱）→ place（名稱 → 座標）找座標；
-- Neo4j 連不上時，交通等時圈也會改用這張表的景點。
INSERT INTO place (p_name, p_category, p_type, p_summary, p_address, p_latitude, p_longitude)
SELECT '宮原眼科', 'Attraction', '古蹟', '日治時期宮原武熊醫師開設的眼科診所，紅磚建築現為甜點與冰淇淋名店。', '臺中市中區中山路20號', 24.1378509, 120.6835697 FROM DUAL
WHERE NOT EXISTS (SELECT 1 FROM place WHERE p_name = '宮原眼科');
INSERT INTO place (p_name, p_category, p_type, p_summary, p_address, p_latitude, p_longitude)
SELECT '臺中公園', 'Attraction', '公園', '臺中最具代表性的百年公園，湖心亭是城市的經典地標。', NULL, 24.1441303, 120.6841243 FROM DUAL
WHERE NOT EXISTS (SELECT 1 FROM place WHERE p_name = '臺中公園');
INSERT INTO place (p_name, p_category, p_type, p_summary, p_address, p_latitude, p_longitude)
SELECT '第二市場', 'Attraction', '市場', '日治時期的新富町市場，以六角樓與眾多老字號小吃聞名。', NULL, 24.1423646, 120.6787413 FROM DUAL
WHERE NOT EXISTS (SELECT 1 FROM place WHERE p_name = '第二市場');
INSERT INTO place (p_name, p_category, p_type, p_summary, p_address, p_latitude, p_longitude)
SELECT '臺中市役所', 'Attraction', '古蹟', '1911 年落成的市役所，白色圓頂建築，與州廳隔街相望。', '臺中市西區民權路97號', 24.1382915, 120.6790302 FROM DUAL
WHERE NOT EXISTS (SELECT 1 FROM place WHERE p_name = '臺中市役所');
INSERT INTO place (p_name, p_category, p_type, p_summary, p_address, p_latitude, p_longitude)
SELECT '臺中州廳', 'Attraction', '古蹟', '森山松之助設計的日治時期官署，擁有馬薩式屋頂與長迴廊。', '臺中市西區民權路99號', 24.1385594, 120.6784293 FROM DUAL
WHERE NOT EXISTS (SELECT 1 FROM place WHERE p_name = '臺中州廳');
INSERT INTO place (p_name, p_category, p_type, p_summary, p_address, p_latitude, p_longitude)
SELECT '臺中文學館', 'Attraction', '古蹟', '由日治時期警察宿舍群改建，園區內的老榕樹是招牌風景。', '臺中市西區樂群街38號', 24.1395787, 120.6729394 FROM DUAL
WHERE NOT EXISTS (SELECT 1 FROM place WHERE p_name = '臺中文學館');
INSERT INTO place (p_name, p_category, p_type, p_summary, p_address, p_latitude, p_longitude)
SELECT '審計新村', 'Attraction', '文創園區', '原省政府審計處宿舍，改造成年輕創作者聚集的文創聚落。', NULL, 24.144587, 120.6626651 FROM DUAL
WHERE NOT EXISTS (SELECT 1 FROM place WHERE p_name = '審計新村');
INSERT INTO place (p_name, p_category, p_type, p_summary, p_address, p_latitude, p_longitude)
SELECT '國立台灣美術館', 'Attraction', '美術館', '國家級美術館，戶外雕塑公園與綠園道相連。', '臺中市西區五權西路一段2號', 24.1414905, 120.663268 FROM DUAL
WHERE NOT EXISTS (SELECT 1 FROM place WHERE p_name = '國立台灣美術館');
INSERT INTO place (p_name, p_category, p_type, p_summary, p_address, p_latitude, p_longitude)
SELECT '忠信市場', 'Attraction', '市場', '老市場裡進駐藝術空間，是國美館旁的祕密角落。', NULL, 24.1396547, 120.6634817 FROM DUAL
WHERE NOT EXISTS (SELECT 1 FROM place WHERE p_name = '忠信市場');
INSERT INTO place (p_name, p_category, p_type, p_summary, p_address, p_latitude, p_longitude)
SELECT '勤美誠品綠園道', 'Attraction', '商圈', '以植生牆聞名的綠色商場，連接草悟道綠帶。', NULL, 24.151409, 120.6638828 FROM DUAL
WHERE NOT EXISTS (SELECT 1 FROM place WHERE p_name = '勤美誠品綠園道');
INSERT INTO place (p_name, p_category, p_type, p_summary, p_address, p_latitude, p_longitude)
SELECT '草悟道', 'Attraction', '綠園道', '從科博館延伸到國美館的城市綠帶，適合散步。', NULL, 24.1554912, 120.6652669 FROM DUAL
WHERE NOT EXISTS (SELECT 1 FROM place WHERE p_name = '草悟道');
INSERT INTO place (p_name, p_category, p_type, p_summary, p_address, p_latitude, p_longitude)
SELECT '國立自然科學博物館', 'Attraction', '博物館', '臺灣第一座科學博物館，恐龍廳與植物園都很受歡迎。', '臺中市北區館前路1號', 24.1573158, 120.6667607 FROM DUAL
WHERE NOT EXISTS (SELECT 1 FROM place WHERE p_name = '國立自然科學博物館');
INSERT INTO place (p_name, p_category, p_type, p_summary, p_address, p_latitude, p_longitude)
SELECT '臺中市政府ˍ新市政大樓', 'Attraction', '政府機關', '臺中新市政中心，前方廣場連接市政公園。', '臺中市西屯區臺灣大道三段99號', 24.1635462, 120.647363 FROM DUAL
WHERE NOT EXISTS (SELECT 1 FROM place WHERE p_name = '臺中市政府ˍ新市政大樓');
INSERT INTO place (p_name, p_category, p_type, p_summary, p_address, p_latitude, p_longitude)
SELECT '臺中國家歌劇院', 'Attraction', '表演場館', '伊東豊雄設計，以曲牆構成「會呼吸的洞穴」。', '臺中市西屯區惠來路二段101號', 24.1629259, 120.6405172 FROM DUAL
WHERE NOT EXISTS (SELECT 1 FROM place WHERE p_name = '臺中國家歌劇院');
INSERT INTO place (p_name, p_category, p_type, p_summary, p_address, p_latitude, p_longitude)
SELECT '秋紅谷景觀生態公園', 'Attraction', '公園', '低於路面的下凹式公園，湖面倒映七期高樓。', NULL, 24.1666317, 120.6384252 FROM DUAL
WHERE NOT EXISTS (SELECT 1 FROM place WHERE p_name = '秋紅谷景觀生態公園');
INSERT INTO place (p_name, p_category, p_type, p_summary, p_address, p_latitude, p_longitude)
SELECT '圓滿戶外劇場', 'Attraction', '表演場館', '位於文心森林公園內的戶外劇場，白色天幕是地標。', NULL, 24.1460294, 120.6456911 FROM DUAL
WHERE NOT EXISTS (SELECT 1 FROM place WHERE p_name = '圓滿戶外劇場');
INSERT INTO place (p_name, p_category, p_type, p_summary, p_address, p_latitude, p_longitude)
SELECT '萬和宮', 'Attraction', '廟宇', '南屯老街上的媽祖廟，端午節有穿木屐躦鯪鯉的傳統。', '臺中市南屯區萬和路一段51號', 24.13794, 120.6390255 FROM DUAL
WHERE NOT EXISTS (SELECT 1 FROM place WHERE p_name = '萬和宮');
INSERT INTO place (p_name, p_category, p_type, p_summary, p_address, p_latitude, p_longitude)
SELECT '豐樂雕塑公園', 'Attraction', '公園', '以雕塑為主題的大型公園，草地上散布許多藝術作品。', NULL, 24.1305303, 120.6426535 FROM DUAL
WHERE NOT EXISTS (SELECT 1 FROM place WHERE p_name = '豐樂雕塑公園');
INSERT INTO place (p_name, p_category, p_type, p_summary, p_address, p_latitude, p_longitude)
SELECT '高鐵臺中站', 'Attraction', '交通場站', '高鐵、臺鐵新烏日站與捷運綠線交會的交通樞紐。', NULL, 24.1116768, 120.6158411 FROM DUAL
WHERE NOT EXISTS (SELECT 1 FROM place WHERE p_name = '高鐵臺中站');
INSERT INTO place (p_name, p_category, p_type, p_summary, p_address, p_latitude, p_longitude)
SELECT '國立臺灣博物館', 'Attraction', '博物館', '1908 年創立，臺灣歷史最悠久的博物館，位於二二八和平紀念公園內。', '臺北市中正區襄陽路2號', 25.0427647, 121.5150029 FROM DUAL
WHERE NOT EXISTS (SELECT 1 FROM place WHERE p_name = '國立臺灣博物館');
INSERT INTO place (p_name, p_category, p_type, p_summary, p_address, p_latitude, p_longitude)
SELECT '國立中正紀念堂', 'Attraction', '紀念館', '白牆藍瓦的紀念堂，自由廣場牌樓是臺北經典畫面。', '臺北市中正區中山南路21號', 25.0345759, 121.5217812 FROM DUAL
WHERE NOT EXISTS (SELECT 1 FROM place WHERE p_name = '國立中正紀念堂');
INSERT INTO place (p_name, p_category, p_type, p_summary, p_address, p_latitude, p_longitude)
SELECT '艋舺龍山寺', 'Attraction', '廟宇', '清乾隆年間創建的古剎，以精緻的木雕與銅鑄龍柱聞名。', '臺北市萬華區廣州街211號', 25.0372498, 121.4998803 FROM DUAL
WHERE NOT EXISTS (SELECT 1 FROM place WHERE p_name = '艋舺龍山寺');
INSERT INTO place (p_name, p_category, p_type, p_summary, p_address, p_latitude, p_longitude)
SELECT '西門町', 'Attraction', '商圈', '臺北年輕人的潮流聖地，西門紅樓的八角樓是地標。', '臺北市萬華區成都路10號', 25.0421376, 121.5062818 FROM DUAL
WHERE NOT EXISTS (SELECT 1 FROM place WHERE p_name = '西門町');
INSERT INTO place (p_name, p_category, p_type, p_summary, p_address, p_latitude, p_longitude)
SELECT '華山1914文化創意產業園區', 'Attraction', '文創園區', '由日治時期酒廠改建的文創園區，展演活動不斷。', '臺北市中正區八德路一段1號', 25.0446089, 121.5291829 FROM DUAL
WHERE NOT EXISTS (SELECT 1 FROM place WHERE p_name = '華山1914文化創意產業園區');
INSERT INTO place (p_name, p_category, p_type, p_summary, p_address, p_latitude, p_longitude)
SELECT '松山文創園區', 'Attraction', '文創園區', '前身為松山菸廠，保留巴洛克式辦公廳與鍋爐房。', '臺北市信義區光復南路133號', 25.0437228, 121.560797 FROM DUAL
WHERE NOT EXISTS (SELECT 1 FROM place WHERE p_name = '松山文創園區');
INSERT INTO place (p_name, p_category, p_type, p_summary, p_address, p_latitude, p_longitude)
SELECT '國立國父紀念館', 'Attraction', '紀念館', '紀念孫中山先生的紀念館，可觀賞衛兵交接。', '臺北市信義區仁愛路四段505號', 25.0399868, 121.5602911 FROM DUAL
WHERE NOT EXISTS (SELECT 1 FROM place WHERE p_name = '國立國父紀念館');
INSERT INTO place (p_name, p_category, p_type, p_summary, p_address, p_latitude, p_longitude)
SELECT '台北101', 'Attraction', '地標', '高 508 公尺的摩天大樓，內部有巨大的風阻尼器。', '臺北市信義區信義路五段7號', 25.0338352, 121.5644995 FROM DUAL
WHERE NOT EXISTS (SELECT 1 FROM place WHERE p_name = '台北101');

-- ── 2. 刪除舊的測試劇本（同名）──
DROP TEMPORARY TABLE IF EXISTS tmp_demo_story;
CREATE TEMPORARY TABLE tmp_demo_story AS SELECT s_id FROM story WHERE story_title IN ('州廳鐘聲裡的家書', '綠線上的時光膠囊', '捷運迷城的懸案');
DELETE o FROM task_option o INNER JOIN task t ON t.task_id = o.task_id INNER JOIN tmp_demo_story d ON d.s_id = t.story_id;
DELETE c FROM task_clue c INNER JOIN task t ON t.task_id = c.task_id INNER JOIN tmp_demo_story d ON d.s_id = t.story_id;
DELETE t FROM task t INNER JOIN tmp_demo_story d ON d.s_id = t.story_id;
DELETE x FROM story_node_transit x INNER JOIN story_node n ON n.sn_id = x.to_sn_id INNER JOIN tmp_demo_story d ON d.s_id = n.s_id;
DELETE ss FROM story_session ss INNER JOIN tmp_demo_story d ON d.s_id = ss.s_id;
DELETE g FROM story_tag g INNER JOIN tmp_demo_story d ON d.s_id = g.s_id;
DELETE n FROM story_node n INNER JOIN tmp_demo_story d ON d.s_id = n.s_id;
DELETE s FROM story s INNER JOIN tmp_demo_story d ON d.s_id = s.s_id;
DROP TEMPORARY TABLE IF EXISTS tmp_demo_story;

-- task_option.option_id 沒有自動遞增，從目前最大值往後編
SELECT COALESCE(MAX(option_id), 0) INTO @opt FROM task_option;

-- ── 3.1 劇本：州廳鐘聲裡的家書（臺中市中區）──
INSERT INTO story (au_id, city_name, district_name, party_size, sd_transport, story_title, story_prologue, story_synopsis, story_badge, story_postcards, is_active, is_night_mode)
VALUES (@au_id, '臺中市', '中區', 2, '步行,公車', '州廳鐘聲裡的家書', '一封寄不出去的家書，被夾在老州廳的公文之間，收信人的名字早已模糊。', '從臺中車站出發，沿著綠川與舊城區的百年建築，一站一站替家書找到回家的路。', '初心探員,舊城守護者', 8, 1, 2);
SET @s = LAST_INSERT_ID();
INSERT IGNORE INTO story_tag (s_id, s_tag) VALUES (@s, '歷史人文');
INSERT IGNORE INTO story_tag (s_id, s_tag) VALUES (@s, '美食');
INSERT INTO story_node (s_id, npc_id, place_id, sn_order, sn_title, is_hidden, is_night_only, is_active, location_codename, sn_opening_text, sn_success_text, sn_task_type)
VALUES (@s, NULL, '580db39b-8aa2-4f16-ad83-16f1ba7bbf8f', 1, '眼科醫生留下的處方箋', 2, 0, 2, '紅磚眼科', '綠川旁的紅磚建築曾是日治時期的眼科診所，家書的第一行字，就寫著這裡的舊地址。', '你讀懂了處方箋上的暗語，家書上模糊的第一個字漸漸清楚。', '文化問答型');
SET @n = LAST_INSERT_ID();
INSERT INTO task (story_id, node_id, task_type, task_describe, correct_answer, task_hint) VALUES (@s, @n, 6, '宮原眼科最早是由哪一國籍的醫生開設的診所？', NULL, '它的名字就是那位醫生的姓氏。');
SET @t = LAST_INSERT_ID();
SET @opt = @opt + 1; INSERT INTO task_option (option_id, task_id, option_context, is_correct, option_key) VALUES (@opt, @t, '日本', 1, 'A');
SET @opt = @opt + 1; INSERT INTO task_option (option_id, task_id, option_context, is_correct, option_key) VALUES (@opt, @t, '荷蘭', 0, 'B');
SET @opt = @opt + 1; INSERT INTO task_option (option_id, task_id, option_context, is_correct, option_key) VALUES (@opt, @t, '英國', 0, 'C');
SET @opt = @opt + 1; INSERT INTO task_option (option_id, task_id, option_context, is_correct, option_key) VALUES (@opt, @t, '美國', 0, 'D');
INSERT INTO story_node (s_id, npc_id, place_id, sn_order, sn_title, is_hidden, is_night_only, is_active, location_codename, sn_opening_text, sn_success_text, sn_task_type)
VALUES (@s, NULL, '099e7ea9-f07e-4a97-a61f-f3a4d44aecc4', 2, '湖心亭的倒影', 2, 0, 2, '雙亭湖畔', '湖面上的雙亭已經在這裡站了一百多年，寄信人說，他們曾在亭邊約定再見。', '倒影裡浮現出一個門牌號碼，家書的地址又清楚了一些。', '景點猜猜樂,創意攝影型');
SET @n = LAST_INSERT_ID();
INSERT INTO task (story_id, node_id, task_type, task_describe, correct_answer, task_hint) VALUES (@s, @n, 7, '臺中公園的湖心亭，當年是為了慶祝哪一件大事而興建？', NULL, '那是一條把臺灣南北連起來的交通建設。');
SET @t = LAST_INSERT_ID();
SET @opt = @opt + 1; INSERT INTO task_option (option_id, task_id, option_context, is_correct, option_key) VALUES (@opt, @t, '縱貫鐵路全線通車', 1, 'A');
SET @opt = @opt + 1; INSERT INTO task_option (option_id, task_id, option_context, is_correct, option_key) VALUES (@opt, @t, '臺中州廳落成', 0, 'B');
SET @opt = @opt + 1; INSERT INTO task_option (option_id, task_id, option_context, is_correct, option_key) VALUES (@opt, @t, '臺中火車站啟用', 0, 'C');
SET @opt = @opt + 1; INSERT INTO task_option (option_id, task_id, option_context, is_correct, option_key) VALUES (@opt, @t, '第一座機場開航', 0, 'D');
INSERT INTO task (story_id, node_id, task_type, task_describe, correct_answer, task_hint) VALUES (@s, @n, 3, '以湖心亭為背景，拍一張有倒影的照片。', NULL, '蹲低一點，靠近水面比較容易拍到完整的倒影。');
INSERT INTO story_node (s_id, npc_id, place_id, sn_order, sn_title, is_hidden, is_night_only, is_active, location_codename, sn_opening_text, sn_success_text, sn_task_type)
VALUES (@s, NULL, '62cb7ab7-ae26-4f2f-934d-90f68cf69da7', 3, '六角樓下的早餐', 2, 0, 2, '六角市場', '寄信人每天清晨都在這座市場吃同一碗早餐，攤主也許還記得他。', '老攤主看了家書上的字跡，笑著說：這是隔壁巷那戶人家的筆跡。', '文化問答型,地方美食型');
SET @n = LAST_INSERT_ID();
INSERT INTO task (story_id, node_id, task_type, task_describe, correct_answer, task_hint) VALUES (@s, @n, 6, '第二市場中央那座特別的建築是什麼形狀？', NULL, '抬頭數數看它有幾個邊。');
SET @t = LAST_INSERT_ID();
SET @opt = @opt + 1; INSERT INTO task_option (option_id, task_id, option_context, is_correct, option_key) VALUES (@opt, @t, '六角形', 1, 'A');
SET @opt = @opt + 1; INSERT INTO task_option (option_id, task_id, option_context, is_correct, option_key) VALUES (@opt, @t, '八角形', 0, 'B');
SET @opt = @opt + 1; INSERT INTO task_option (option_id, task_id, option_context, is_correct, option_key) VALUES (@opt, @t, '圓形', 0, 'C');
SET @opt = @opt + 1; INSERT INTO task_option (option_id, task_id, option_context, is_correct, option_key) VALUES (@opt, @t, '三角形', 0, 'D');
INSERT INTO task (story_id, node_id, task_type, task_describe, correct_answer, task_hint) VALUES (@s, @n, 4, '在市場裡找一攤營業超過三十年的老攤小吃，拍下來並說出它的招牌。', NULL, '問問排隊的人，哪一攤最老。');
INSERT INTO story_node (s_id, npc_id, place_id, sn_order, sn_title, is_hidden, is_night_only, is_active, location_codename, sn_opening_text, sn_success_text, sn_task_type)
VALUES (@s, NULL, '28c43c9f-789f-44c6-9656-c73c1c200e4a', 4, '圓頂下的市民大會', 2, 0, 2, '白色圓頂', '白色圓頂的市役所裡，曾經保存著全市的戶籍資料。', '工作人員替你查到了舊戶籍的線索，收信人的姓氏終於出現。', 'e 人訪談型');
SET @n = LAST_INSERT_ID();
INSERT INTO task (story_id, node_id, task_type, task_describe, correct_answer, task_hint) VALUES (@s, @n, 8, '向市役所裡的一位工作人員或店員打聽：這棟建築現在是做什麼用的？把答案記下來。', NULL, '櫃台或咖啡吧的人最清楚。');
INSERT INTO story_node (s_id, npc_id, place_id, sn_order, sn_title, is_hidden, is_night_only, is_active, location_codename, sn_opening_text, sn_success_text, sn_task_type)
VALUES (@s, NULL, '42eccf3e-4e7a-41b8-a58a-ce3d7368b4f6', 5, '紅磚廊下的公文', 2, 0, 2, '紅磚迴廊', '你走進長長的迴廊，陽光從拱窗灑下，家書就是在這裡的公文堆裡被發現的。', '你認出了屋頂的樣式，也拼出了設計者的名字，家書上的地址完整了。', '文化問答型,協作解謎型');
SET @n = LAST_INSERT_ID();
INSERT INTO task (story_id, node_id, task_type, task_describe, correct_answer, task_hint) VALUES (@s, @n, 6, '抬頭看看州廳的屋頂，它是哪一種樣式？', NULL, '這種屋頂源自法國，斜面很陡，上面還開了小窗。');
SET @t = LAST_INSERT_ID();
SET @opt = @opt + 1; INSERT INTO task_option (option_id, task_id, option_context, is_correct, option_key) VALUES (@opt, @t, '馬薩式屋頂', 1, 'A');
SET @opt = @opt + 1; INSERT INTO task_option (option_id, task_id, option_context, is_correct, option_key) VALUES (@opt, @t, '燕尾脊', 0, 'B');
SET @opt = @opt + 1; INSERT INTO task_option (option_id, task_id, option_context, is_correct, option_key) VALUES (@opt, @t, '歇山頂', 0, 'C');
SET @opt = @opt + 1; INSERT INTO task_option (option_id, task_id, option_context, is_correct, option_key) VALUES (@opt, @t, '攢尖頂', 0, 'D');
INSERT INTO task (story_id, node_id, task_type, task_describe, correct_answer, task_hint) VALUES (@s, @n, 5, '州廳的設計者是誰？兩人各拿到一半線索，交換後拼出他的名字。', '森山松之助', '日治時期的知名建築師，名字共有五個字。');
SET @t = LAST_INSERT_ID();
INSERT INTO task_clue (task_id, seat_no, clue_text) VALUES (@t, 1, '你拿到的是姓氏：「森＿」，第二個字是一座高高的「山」。');
INSERT INTO task_clue (task_id, seat_no, clue_text) VALUES (@t, 2, '你拿到的是名字：「＿之助」，第一個字是一種四季常青的樹。');
INSERT INTO story_node (s_id, npc_id, place_id, sn_order, sn_title, is_hidden, is_night_only, is_active, location_codename, sn_opening_text, sn_success_text, sn_task_type)
VALUES (@s, NULL, '76fa442a-b4e4-4ade-be29-d15d850d22e2', 6, '老榕樹下的讀信人', 2, 0, 2, '日式宿舍', '日式宿舍前的老榕樹下，常有人坐著讀信，也許收信人也曾坐在這裡。', '榕樹下的長椅刻著一行小字，指向下一個巷弄。', '景點猜猜樂');
SET @n = LAST_INSERT_ID();
INSERT INTO task (story_id, node_id, task_type, task_describe, correct_answer, task_hint) VALUES (@s, @n, 7, '臺中文學館的建築群，在日治時期原本是做什麼用的？', NULL, '當年住在這裡的人負責維持治安。');
SET @t = LAST_INSERT_ID();
SET @opt = @opt + 1; INSERT INTO task_option (option_id, task_id, option_context, is_correct, option_key) VALUES (@opt, @t, '警察宿舍', 1, 'A');
SET @opt = @opt + 1; INSERT INTO task_option (option_id, task_id, option_context, is_correct, option_key) VALUES (@opt, @t, '學校教室', 0, 'B');
SET @opt = @opt + 1; INSERT INTO task_option (option_id, task_id, option_context, is_correct, option_key) VALUES (@opt, @t, '醫院病房', 0, 'C');
SET @opt = @opt + 1; INSERT INTO task_option (option_id, task_id, option_context, is_correct, option_key) VALUES (@opt, @t, '火車站倉庫', 0, 'D');
INSERT INTO story_node (s_id, npc_id, place_id, sn_order, sn_title, is_hidden, is_night_only, is_active, location_codename, sn_opening_text, sn_success_text, sn_task_type)
VALUES (@s, NULL, '981d28e3-9f55-4de5-a1ca-172f67e575c3', 7, '宿舍巷弄的郵差', 2, 0, 2, '文創巷弄', '老宿舍改成了小店，巷子裡有一位自稱郵差的店主，說他專門收寄不出去的信。', '郵差蓋上了一枚手作郵戳，家書只差最後一站。', 'e 人訪談型,景點猜猜樂');
SET @n = LAST_INSERT_ID();
INSERT INTO task (story_id, node_id, task_type, task_describe, correct_answer, task_hint) VALUES (@s, @n, 8, '找一位攤主或店員，請他推薦一樣在地小物，並請他在你的探險筆記上簽名。', NULL, '週末的市集攤位最多。');
INSERT INTO task (story_id, node_id, task_type, task_describe, correct_answer, task_hint) VALUES (@s, @n, 7, '審計新村原本是哪個單位的員工宿舍？', NULL, '名字裡就藏著答案。');
SET @t = LAST_INSERT_ID();
SET @opt = @opt + 1; INSERT INTO task_option (option_id, task_id, option_context, is_correct, option_key) VALUES (@opt, @t, '臺灣省政府審計處', 1, 'A');
SET @opt = @opt + 1; INSERT INTO task_option (option_id, task_id, option_context, is_correct, option_key) VALUES (@opt, @t, '臺中市政府', 0, 'B');
SET @opt = @opt + 1; INSERT INTO task_option (option_id, task_id, option_context, is_correct, option_key) VALUES (@opt, @t, '臺灣鐵路局', 0, 'C');
SET @opt = @opt + 1; INSERT INTO task_option (option_id, task_id, option_context, is_correct, option_key) VALUES (@opt, @t, '國防部', 0, 'D');
INSERT INTO story_node (s_id, npc_id, place_id, sn_order, sn_title, is_hidden, is_night_only, is_active, location_codename, sn_opening_text, sn_success_text, sn_task_type)
VALUES (@s, NULL, '86f05d8d-59b8-43aa-893c-77819d8a9981', 8, '美術館的最後一封信', 2, 0, 2, '綠色美術館', '美術館的草地上，有一座雕塑的姿勢像極了正在送信的人。', '你們把家書交到了收信人的後代手上，舊城的鐘聲響起，這趟旅程圓滿了。', '創意攝影型,文化問答型');
SET @n = LAST_INSERT_ID();
INSERT INTO task (story_id, node_id, task_type, task_describe, correct_answer, task_hint) VALUES (@s, @n, 3, '在美術館戶外雕塑中找一件最像「信差」的作品，兩人模仿它的姿勢合照。', NULL, '戶外雕塑公園在美術館的北側草地。');
INSERT INTO task (story_id, node_id, task_type, task_describe, correct_answer, task_hint) VALUES (@s, @n, 6, '國立台灣美術館位於臺中市哪一區？', NULL, '它就在審計新村附近。');
SET @t = LAST_INSERT_ID();
SET @opt = @opt + 1; INSERT INTO task_option (option_id, task_id, option_context, is_correct, option_key) VALUES (@opt, @t, '西區', 1, 'A');
SET @opt = @opt + 1; INSERT INTO task_option (option_id, task_id, option_context, is_correct, option_key) VALUES (@opt, @t, '北屯區', 0, 'B');
SET @opt = @opt + 1; INSERT INTO task_option (option_id, task_id, option_context, is_correct, option_key) VALUES (@opt, @t, '南屯區', 0, 'C');
SET @opt = @opt + 1; INSERT INTO task_option (option_id, task_id, option_context, is_correct, option_key) VALUES (@opt, @t, '東區', 0, 'D');

-- ── 3.2 劇本：綠線上的時光膠囊（臺中市西屯區）──
INSERT INTO story (au_id, city_name, district_name, party_size, sd_transport, story_title, story_prologue, story_synopsis, story_badge, story_postcards, is_active, is_night_mode)
VALUES (@au_id, '臺中市', '西屯區', 2, '步行,捷運', '綠線上的時光膠囊', '有人在捷運綠線的某一站埋了一顆時光膠囊，只留下一張寫滿站名的車票。', '搭著捷運綠線穿梭七期與南屯，一站一站拼出膠囊的下落，最後趕上高鐵把它寄出去。', '綠線乘客,時光旅人', 7, 1, 2);
SET @s = LAST_INSERT_ID();
INSERT IGNORE INTO story_tag (s_id, s_tag) VALUES (@s, '城市探索');
INSERT IGNORE INTO story_tag (s_id, s_tag) VALUES (@s, '親子同樂');
INSERT INTO story_node (s_id, npc_id, place_id, sn_order, sn_title, is_hidden, is_night_only, is_active, location_codename, sn_opening_text, sn_success_text, sn_task_type)
VALUES (@s, NULL, '1fb00e64-6ad1-465a-9efb-248c2c742da2', 1, '市府廣場的第一張車票', 2, 0, 2, '玻璃市府', '車票的第一個站名是「市政府」，膠囊的主人說，所有線索都從這片廣場開始。', '路人的回答和車票上的塗鴉對上了，下一站在不遠的曲牆建築。', 'e 人訪談型');
SET @n = LAST_INSERT_ID();
INSERT INTO task (story_id, node_id, task_type, task_describe, correct_answer, task_hint) VALUES (@s, @n, 8, '向廣場上的一位路人打聽：他最常在哪一站搭捷運綠線？記下他的回答。', NULL, '等公車或搭捷運的人最好問。');
INSERT INTO story_node (s_id, npc_id, place_id, sn_order, sn_title, is_hidden, is_night_only, is_active, location_codename, sn_opening_text, sn_success_text, sn_task_type)
VALUES (@s, NULL, '02d6bcff-0eb3-489e-9981-e5e52db8e846', 2, '會呼吸的洞穴', 2, 0, 2, '曲牆劇院', '這棟建築沒有一根直柱，牆面像洞穴一樣彎曲，膠囊主人最愛在這裡躲貓貓。', '你們在曲牆的轉角找到一張貼紙，上面畫著一座凹下去的公園。', '文化問答型,創意攝影型');
SET @n = LAST_INSERT_ID();
INSERT INTO task (story_id, node_id, task_type, task_describe, correct_answer, task_hint) VALUES (@s, @n, 6, '臺中國家歌劇院是由哪位建築師設計的？', NULL, '他是日本建築師，也拿過普立茲克建築獎。');
SET @t = LAST_INSERT_ID();
SET @opt = @opt + 1; INSERT INTO task_option (option_id, task_id, option_context, is_correct, option_key) VALUES (@opt, @t, '伊東豊雄', 1, 'A');
SET @opt = @opt + 1; INSERT INTO task_option (option_id, task_id, option_context, is_correct, option_key) VALUES (@opt, @t, '安藤忠雄', 0, 'B');
SET @opt = @opt + 1; INSERT INTO task_option (option_id, task_id, option_context, is_correct, option_key) VALUES (@opt, @t, '貝聿銘', 0, 'C');
SET @opt = @opt + 1; INSERT INTO task_option (option_id, task_id, option_context, is_correct, option_key) VALUES (@opt, @t, '隈研吾', 0, 'D');
INSERT INTO task (story_id, node_id, task_type, task_describe, correct_answer, task_hint) VALUES (@s, @n, 3, '找一面最彎的曲牆，兩人一起擺出「被吸進洞穴」的姿勢拍照。', NULL, '一樓大廳和空中花園都有很多曲牆。');
INSERT INTO story_node (s_id, npc_id, place_id, sn_order, sn_title, is_hidden, is_night_only, is_active, location_codename, sn_opening_text, sn_success_text, sn_task_type)
VALUES (@s, NULL, '3991cae1-ecda-4c17-8688-088b72bd2758', 3, '下凹的祕密谷地', 2, 0, 2, '城市谷地', '膠囊主人說，城市裡有一座「比馬路還低」的谷地，秋天的時候最美。', '你們在湖邊的木棧道上找到第三張車票，目的地寫著：森林裡的舞台。', '景點猜猜樂');
SET @n = LAST_INSERT_ID();
INSERT INTO task (story_id, node_id, task_type, task_describe, correct_answer, task_hint) VALUES (@s, @n, 7, '秋紅谷是一座怎樣的公園？', NULL, '站在馬路邊往下看看。');
SET @t = LAST_INSERT_ID();
SET @opt = @opt + 1; INSERT INTO task_option (option_id, task_id, option_context, is_correct, option_key) VALUES (@opt, @t, '比周邊馬路低的下凹式公園', 1, 'A');
SET @opt = @opt + 1; INSERT INTO task_option (option_id, task_id, option_context, is_correct, option_key) VALUES (@opt, @t, '建在屋頂上的空中花園', 0, 'B');
SET @opt = @opt + 1; INSERT INTO task_option (option_id, task_id, option_context, is_correct, option_key) VALUES (@opt, @t, '地下停車場裡的室內公園', 0, 'C');
SET @opt = @opt + 1; INSERT INTO task_option (option_id, task_id, option_context, is_correct, option_key) VALUES (@opt, @t, '山坡上的梯田公園', 0, 'D');
INSERT INTO story_node (s_id, npc_id, place_id, sn_order, sn_title, is_hidden, is_night_only, is_active, location_codename, sn_opening_text, sn_success_text, sn_task_type)
VALUES (@s, NULL, '62e4ab9c-1ea3-456c-8642-31e3d23589c1', 4, '森林裡的圓形舞台', 2, 0, 2, '白色天幕', '白色天幕下的舞台空無一人，舞台邊的告示牌只寫了半個公園的名字。', '你們合力說出了公園的全名，舞台下的抽屜彈開，裡面是一把生鏽的鑰匙。', '協作解謎型');
SET @n = LAST_INSERT_ID();
INSERT INTO task (story_id, node_id, task_type, task_describe, correct_answer, task_hint) VALUES (@s, @n, 5, '膠囊的下一個線索藏在「劇場所在的公園」裡，兩人交換線索，說出公園的全名。', '文心森林公園', '公園的名字共有六個字。');
SET @t = LAST_INSERT_ID();
INSERT INTO task_clue (task_id, seat_no, clue_text) VALUES (@t, 1, '你知道：公園名字的前兩個字，是臺中一條環狀大路的名字，捷運綠線就蓋在它上面。');
INSERT INTO task_clue (task_id, seat_no, clue_text) VALUES (@t, 2, '你知道：公園名字的後四個字是「森林公園」。');
INSERT INTO story_node (s_id, npc_id, place_id, sn_order, sn_title, is_hidden, is_night_only, is_active, location_codename, sn_opening_text, sn_success_text, sn_task_type)
VALUES (@s, NULL, '94a204e0-acea-472c-bd8c-cb84a45ad6cc', 5, '老廟前的木屐聲', 2, 0, 2, '南屯老廟', '鑰匙上刻著一座老廟的名字，端午節時，這裡的街道會響起木屐聲。', '廟公認出了鑰匙，說膠囊主人小時候每年都來這裡參加木屐比賽。', '文化問答型,地方美食型');
SET @n = LAST_INSERT_ID();
INSERT INTO task (story_id, node_id, task_type, task_describe, correct_answer, task_hint) VALUES (@s, @n, 6, '萬和宮主要供奉的是哪一位神明？', NULL, '她是守護航海與漁民的女神。');
SET @t = LAST_INSERT_ID();
SET @opt = @opt + 1; INSERT INTO task_option (option_id, task_id, option_context, is_correct, option_key) VALUES (@opt, @t, '媽祖', 1, 'A');
SET @opt = @opt + 1; INSERT INTO task_option (option_id, task_id, option_context, is_correct, option_key) VALUES (@opt, @t, '關聖帝君', 0, 'B');
SET @opt = @opt + 1; INSERT INTO task_option (option_id, task_id, option_context, is_correct, option_key) VALUES (@opt, @t, '土地公', 0, 'C');
SET @opt = @opt + 1; INSERT INTO task_option (option_id, task_id, option_context, is_correct, option_key) VALUES (@opt, @t, '玄天上帝', 0, 'D');
INSERT INTO task (story_id, node_id, task_type, task_describe, correct_answer, task_hint) VALUES (@s, @n, 4, '在南屯老街找一樣傳統點心或麻芛料理，拍下來並說出它的味道。', NULL, '麻芛是南屯的特產，老街上就有專賣店。');
INSERT INTO story_node (s_id, npc_id, place_id, sn_order, sn_title, is_hidden, is_night_only, is_active, location_codename, sn_opening_text, sn_success_text, sn_task_type)
VALUES (@s, NULL, '6bfa3056-60a9-45af-98c6-f27283c0280f', 6, '雕像們的午休時間', 2, 0, 2, '雕塑草原', '廟公說，膠囊主人把最後的地圖交給了公園裡「最愛偷懶的雕像」保管。', '你們找到那座雕像，底座下壓著一張高鐵車票。', '創意攝影型');
SET @n = LAST_INSERT_ID();
INSERT INTO task (story_id, node_id, task_type, task_describe, correct_answer, task_hint) VALUES (@s, @n, 3, '找一座你們覺得最好笑的雕塑，模仿它的動作拍一張照片。', NULL, '草地深處的雕塑比較少人注意。');
INSERT INTO story_node (s_id, npc_id, place_id, sn_order, sn_title, is_hidden, is_night_only, is_active, location_codename, sn_opening_text, sn_success_text, sn_task_type)
VALUES (@s, NULL, 'b1cd8ae7-2b10-4a98-b297-7dd0ebd89108', 7, '膠囊搭上高鐵', 2, 0, 2, '終點月台', '綠線的最後一站，膠囊的主人就在月台上等著，他要把膠囊寄給十年後的自己。', '列車進站了，膠囊和你們的合照一起出發，時光旅程完成。', '景點猜猜樂');
SET @n = LAST_INSERT_ID();
INSERT INTO task (story_id, node_id, task_type, task_describe, correct_answer, task_hint) VALUES (@s, @n, 7, '在高鐵臺中站可以直接轉乘哪些軌道運輸？', NULL, '一個是臺鐵，另一個你今天已經搭了很多次。');
SET @t = LAST_INSERT_ID();
SET @opt = @opt + 1; INSERT INTO task_option (option_id, task_id, option_context, is_correct, option_key) VALUES (@opt, @t, '臺鐵與臺中捷運綠線', 1, 'A');
SET @opt = @opt + 1; INSERT INTO task_option (option_id, task_id, option_context, is_correct, option_key) VALUES (@opt, @t, '只有臺鐵', 0, 'B');
SET @opt = @opt + 1; INSERT INTO task_option (option_id, task_id, option_context, is_correct, option_key) VALUES (@opt, @t, '只有公車', 0, 'C');
SET @opt = @opt + 1; INSERT INTO task_option (option_id, task_id, option_context, is_correct, option_key) VALUES (@opt, @t, '桃園機場捷運', 0, 'D');

-- ── 3.3 劇本：捷運迷城的懸案（臺北市中正區）──
INSERT INTO story (au_id, city_name, district_name, party_size, sd_transport, story_title, story_prologue, story_synopsis, story_badge, story_postcards, is_active, is_night_mode)
VALUES (@au_id, '臺北市', '中正區', 2, '步行,捷運', '捷運迷城的懸案', '一位老偵探在捷運站的置物櫃留下八把鑰匙，每一把都對應一個臺北的地標。', '從臺大醫院站出發，靠捷運在城南、萬華與信義之間追查線索，天黑前把鑰匙全部找齊。', '捷運偵探,臺北通', 8, 1, 2);
SET @s = LAST_INSERT_ID();
INSERT IGNORE INTO story_tag (s_id, s_tag) VALUES (@s, '歷史人文');
INSERT IGNORE INTO story_tag (s_id, s_tag) VALUES (@s, '城市探索');
INSERT INTO story_node (s_id, npc_id, place_id, sn_order, sn_title, is_hidden, is_night_only, is_active, location_codename, sn_opening_text, sn_success_text, sn_task_type)
VALUES (@s, NULL, '182c3e39-d02a-4900-b5eb-5b706d40b190', 1, '公園裡的希臘神殿', 2, 0, 2, '銅像大廳', '老偵探的第一把鑰匙，藏在一座像希臘神殿的博物館裡。', '你答出了博物館的年紀，展示櫃的鎖應聲打開。', '文化問答型');
SET @n = LAST_INSERT_ID();
INSERT INTO task (story_id, node_id, task_type, task_describe, correct_answer, task_hint) VALUES (@s, @n, 6, '國立臺灣博物館是臺灣歷史最悠久的博物館，它創立於哪一年？', NULL, '比中華民國建立還早幾年。');
SET @t = LAST_INSERT_ID();
SET @opt = @opt + 1; INSERT INTO task_option (option_id, task_id, option_context, is_correct, option_key) VALUES (@opt, @t, '1908 年', 1, 'A');
SET @opt = @opt + 1; INSERT INTO task_option (option_id, task_id, option_context, is_correct, option_key) VALUES (@opt, @t, '1945 年', 0, 'B');
SET @opt = @opt + 1; INSERT INTO task_option (option_id, task_id, option_context, is_correct, option_key) VALUES (@opt, @t, '1965 年', 0, 'C');
SET @opt = @opt + 1; INSERT INTO task_option (option_id, task_id, option_context, is_correct, option_key) VALUES (@opt, @t, '1988 年', 0, 'D');
INSERT INTO story_node (s_id, npc_id, place_id, sn_order, sn_title, is_hidden, is_night_only, is_active, location_codename, sn_opening_text, sn_success_text, sn_task_type)
VALUES (@s, NULL, 'f3ac7881-13fd-4c16-bc51-497ec0e15df7', 2, '八十九級台階', 2, 0, 2, '白牆藍瓦', '第二把鑰匙的線索是一個數字，偵探說：「爬上去的時候，記得數一數。」', '台階的數字正是保險箱的密碼。', '景點猜猜樂,創意攝影型');
SET @n = LAST_INSERT_ID();
INSERT INTO task (story_id, node_id, task_type, task_describe, correct_answer, task_hint) VALUES (@s, @n, 7, '中正紀念堂正門的階梯共有幾級？', NULL, '這個數字和紀念對象過世時的歲數有關。');
SET @t = LAST_INSERT_ID();
SET @opt = @opt + 1; INSERT INTO task_option (option_id, task_id, option_context, is_correct, option_key) VALUES (@opt, @t, '89 級', 1, 'A');
SET @opt = @opt + 1; INSERT INTO task_option (option_id, task_id, option_context, is_correct, option_key) VALUES (@opt, @t, '72 級', 0, 'B');
SET @opt = @opt + 1; INSERT INTO task_option (option_id, task_id, option_context, is_correct, option_key) VALUES (@opt, @t, '100 級', 0, 'C');
SET @opt = @opt + 1; INSERT INTO task_option (option_id, task_id, option_context, is_correct, option_key) VALUES (@opt, @t, '64 級', 0, 'D');
INSERT INTO task (story_id, node_id, task_type, task_describe, correct_answer, task_hint) VALUES (@s, @n, 3, '在自由廣場牌樓前拍一張兩人的背影照。', NULL, '站遠一點，才拍得到整座牌樓。');
INSERT INTO story_node (s_id, npc_id, place_id, sn_order, sn_title, is_hidden, is_night_only, is_active, location_codename, sn_opening_text, sn_success_text, sn_task_type)
VALUES (@s, NULL, '697ce7d5-610d-4293-b8f6-024b956e2c13', 3, '銅柱上的龍', 2, 0, 2, '艋舺古剎', '偵探年輕時常來這座古剎求籤，第三把鑰匙就繫在一支舊籤上。', '廟方的志工認出了偵探的名字，把鑰匙交給了你們。', '文化問答型');
SET @n = LAST_INSERT_ID();
INSERT INTO task (story_id, node_id, task_type, task_describe, correct_answer, task_hint) VALUES (@s, @n, 6, '艋舺龍山寺創建於清朝哪一個年代？', NULL, '那位皇帝在位六十年。');
SET @t = LAST_INSERT_ID();
SET @opt = @opt + 1; INSERT INTO task_option (option_id, task_id, option_context, is_correct, option_key) VALUES (@opt, @t, '乾隆年間', 1, 'A');
SET @opt = @opt + 1; INSERT INTO task_option (option_id, task_id, option_context, is_correct, option_key) VALUES (@opt, @t, '康熙年間', 0, 'B');
SET @opt = @opt + 1; INSERT INTO task_option (option_id, task_id, option_context, is_correct, option_key) VALUES (@opt, @t, '光緒年間', 0, 'C');
SET @opt = @opt + 1; INSERT INTO task_option (option_id, task_id, option_context, is_correct, option_key) VALUES (@opt, @t, '道光年間', 0, 'D');
INSERT INTO story_node (s_id, npc_id, place_id, sn_order, sn_title, is_hidden, is_night_only, is_active, location_codename, sn_opening_text, sn_success_text, sn_task_type)
VALUES (@s, NULL, '7144ec3d-3986-4418-ae6a-c83dbc8c6bdc', 4, '八角樓的人潮', 2, 0, 2, '紅樓廣場', '人潮最多的地方最好藏東西，偵探把第四把鑰匙交給了一位街頭藝人。', '街頭藝人收下你們的回答，從帽子裡拿出了鑰匙。', 'e 人訪談型,景點猜猜樂');
SET @n = LAST_INSERT_ID();
INSERT INTO task (story_id, node_id, task_type, task_describe, correct_answer, task_hint) VALUES (@s, @n, 8, '在西門町找一位街頭藝人或店員，請他推薦一個「只有在地人知道」的地方，並記下來。', NULL, '紅樓旁的廣場常有街頭表演。');
INSERT INTO task (story_id, node_id, task_type, task_describe, correct_answer, task_hint) VALUES (@s, @n, 7, '西門紅樓最有名的是哪一種形狀的樓？', NULL, '數數看它有幾個面。');
SET @t = LAST_INSERT_ID();
SET @opt = @opt + 1; INSERT INTO task_option (option_id, task_id, option_context, is_correct, option_key) VALUES (@opt, @t, '八角形', 1, 'A');
SET @opt = @opt + 1; INSERT INTO task_option (option_id, task_id, option_context, is_correct, option_key) VALUES (@opt, @t, '六角形', 0, 'B');
SET @opt = @opt + 1; INSERT INTO task_option (option_id, task_id, option_context, is_correct, option_key) VALUES (@opt, @t, '圓形', 0, 'C');
SET @opt = @opt + 1; INSERT INTO task_option (option_id, task_id, option_context, is_correct, option_key) VALUES (@opt, @t, '十字形', 0, 'D');
INSERT INTO story_node (s_id, npc_id, place_id, sn_order, sn_title, is_hidden, is_night_only, is_active, location_codename, sn_opening_text, sn_success_text, sn_task_type)
VALUES (@s, NULL, '283e123a-f0e7-4a7b-b97a-4d1429ba17fb', 5, '酒廠裡的空酒瓶', 2, 0, 2, '紅磚酒廠', '第五把鑰匙被塞進一個空酒瓶，瓶身上印著這座園區的舊名。', '你們說對了工廠的身分，酒瓶裡的紙條指向城東的另一座老工廠。', '文化問答型');
SET @n = LAST_INSERT_ID();
INSERT INTO task (story_id, node_id, task_type, task_describe, correct_answer, task_hint) VALUES (@s, @n, 6, '華山文創園區在日治時期原本是什麼工廠？', NULL, '那個空酒瓶就是提示。');
SET @t = LAST_INSERT_ID();
SET @opt = @opt + 1; INSERT INTO task_option (option_id, task_id, option_context, is_correct, option_key) VALUES (@opt, @t, '酒廠', 1, 'A');
SET @opt = @opt + 1; INSERT INTO task_option (option_id, task_id, option_context, is_correct, option_key) VALUES (@opt, @t, '菸廠', 0, 'B');
SET @opt = @opt + 1; INSERT INTO task_option (option_id, task_id, option_context, is_correct, option_key) VALUES (@opt, @t, '糖廠', 0, 'C');
SET @opt = @opt + 1; INSERT INTO task_option (option_id, task_id, option_context, is_correct, option_key) VALUES (@opt, @t, '紙廠', 0, 'D');
INSERT INTO story_node (s_id, npc_id, place_id, sn_order, sn_title, is_hidden, is_night_only, is_active, location_codename, sn_opening_text, sn_success_text, sn_task_type)
VALUES (@s, NULL, '70899bf2-12dd-4113-a2ee-94d00e60bfdc', 6, '菸廠的鍋爐房', 2, 0, 2, '菸廠巴洛克', '巴洛克式的辦公廳裡，偵探留了兩張殘缺的紙條，要兩個人一起才看得懂。', '你們拼出工廠的舊名，鍋爐房的門打開了。', '協作解謎型');
SET @n = LAST_INSERT_ID();
INSERT INTO task (story_id, node_id, task_type, task_describe, correct_answer, task_hint) VALUES (@s, @n, 5, '合力說出這座園區過去的工廠名稱（四個字）。', '松山菸廠', '兩張紙條分別講產品與地名。');
SET @t = LAST_INSERT_ID();
INSERT INTO task_clue (task_id, seat_no, clue_text) VALUES (@t, 1, '你的紙條：這裡以前生產的東西，是拿來「抽」的。');
INSERT INTO task_clue (task_id, seat_no, clue_text) VALUES (@t, 2, '你的紙條：工廠用所在地區命名，這一區叫「松山」。');
INSERT INTO story_node (s_id, npc_id, place_id, sn_order, sn_title, is_hidden, is_night_only, is_active, location_codename, sn_opening_text, sn_success_text, sn_task_type)
VALUES (@s, NULL, '474dd487-6f06-428c-9ac4-62d75d67ac84', 7, '衛兵交接的空檔', 2, 0, 2, '黃瓦大殿', '衛兵交接的那幾分鐘，是偵探約定交出第七把鑰匙的時間。', '交接結束，服務台的人把一個信封交給了你們。', '景點猜猜樂');
SET @n = LAST_INSERT_ID();
INSERT INTO task (story_id, node_id, task_type, task_describe, correct_answer, task_hint) VALUES (@s, @n, 7, '國父紀念館是為了紀念哪一位人物而興建？', NULL, '他被尊稱為「國父」。');
SET @t = LAST_INSERT_ID();
SET @opt = @opt + 1; INSERT INTO task_option (option_id, task_id, option_context, is_correct, option_key) VALUES (@opt, @t, '孫中山', 1, 'A');
SET @opt = @opt + 1; INSERT INTO task_option (option_id, task_id, option_context, is_correct, option_key) VALUES (@opt, @t, '蔣中正', 0, 'B');
SET @opt = @opt + 1; INSERT INTO task_option (option_id, task_id, option_context, is_correct, option_key) VALUES (@opt, @t, '鄭成功', 0, 'C');
SET @opt = @opt + 1; INSERT INTO task_option (option_id, task_id, option_context, is_correct, option_key) VALUES (@opt, @t, '劉銘傳', 0, 'D');
INSERT INTO story_node (s_id, npc_id, place_id, sn_order, sn_title, is_hidden, is_night_only, is_active, location_codename, sn_opening_text, sn_success_text, sn_task_type)
VALUES (@s, NULL, '87997053-b3f8-462b-a081-5eb9761d08c2', 8, '阻尼球的祕密', 2, 0, 2, '竹節高塔', '最後一把鑰匙在城市最高的地方，偵探說：「這棟樓的心臟會隨風擺動。」', '八把鑰匙到齊，置物櫃裡是偵探寫給你們的結案報告，懸案告破。', '文化問答型,創意攝影型');
SET @n = LAST_INSERT_ID();
INSERT INTO task (story_id, node_id, task_type, task_describe, correct_answer, task_hint) VALUES (@s, @n, 6, '台北101 大樓裡用來減少晃動的巨大鋼球叫做什麼？', NULL, '它在大風和地震時會反向擺動。');
SET @t = LAST_INSERT_ID();
SET @opt = @opt + 1; INSERT INTO task_option (option_id, task_id, option_context, is_correct, option_key) VALUES (@opt, @t, '風阻尼器', 1, 'A');
SET @opt = @opt + 1; INSERT INTO task_option (option_id, task_id, option_context, is_correct, option_key) VALUES (@opt, @t, '避雷球', 0, 'B');
SET @opt = @opt + 1; INSERT INTO task_option (option_id, task_id, option_context, is_correct, option_key) VALUES (@opt, @t, '配重塊', 0, 'C');
SET @opt = @opt + 1; INSERT INTO task_option (option_id, task_id, option_context, is_correct, option_key) VALUES (@opt, @t, '地震儀', 0, 'D');
INSERT INTO task (story_id, node_id, task_type, task_describe, correct_answer, task_hint) VALUES (@s, @n, 3, '在信義路上找一個能拍到整棟 101 的角度，兩人一起入鏡。', NULL, '往松智路或象山方向走，角度比較好。');

COMMIT;

-- 檢查：3 個劇本、各自的節點數與任務數
SELECT s.s_id, s.story_title, COUNT(DISTINCT n.sn_id) AS nodes, COUNT(DISTINCT t.task_id) AS tasks
FROM story s LEFT JOIN story_node n ON n.s_id = s.s_id LEFT JOIN task t ON t.story_id = s.s_id
WHERE s.story_title IN ('州廳鐘聲裡的家書', '綠線上的時光膠囊', '捷運迷城的懸案') GROUP BY s.s_id, s.story_title;
