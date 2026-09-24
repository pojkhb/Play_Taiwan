-- ============================================================================
-- 周邊好去展示用測試資料（Play Taiwan）
--
-- 先匯入 TestData_RouteDemo.sql（3 個測試劇本），再匯入本檔。
-- GET /api/Map/{story_id}/Nearby 依「地址含劇本縣市」找地點，所以每筆都要有地址。
--
--   1. 補上測試景點缺的地址（原本地址是 NULL 的才補），這些景點才會出現在周邊好去
--   2. 三個劇本附近的「飲食」與「其他」地點（p_category = '飲食' / '其他'，p_type 是卡片上的第二個標籤）
--   3. 照片：Wikimedia Commons 的縮圖網址（原本沒有照片的才補），各張的作者與授權寫在該行上方
--
-- 注意：營業時間與座標是展示用，沒有逐一查證，正式上線前請再核對。
-- 飲食類地點不會被同心圓（Neo4j 連不上時改查 place 表）當成景點抽出來。
-- 可以重複匯入：同名地點不重複新增。
-- ============================================================================

SET NAMES utf8mb4;
START TRANSACTION;

-- ── 1. 補測試景點的地址 ──
UPDATE place SET p_address = '臺中市北區公園路37之1號' WHERE p_name = '臺中公園' AND p_address IS NULL;
UPDATE place SET p_address = '臺中市中區三民路二段87號' WHERE p_name = '第二市場' AND p_address IS NULL;
UPDATE place SET p_address = '臺中市西區民生路368巷' WHERE p_name = '審計新村' AND p_address IS NULL;
UPDATE place SET p_address = '臺中市西區五權西三街43號' WHERE p_name = '忠信市場' AND p_address IS NULL;
UPDATE place SET p_address = '臺中市西區公益路68號' WHERE p_name = '勤美誠品綠園道' AND p_address IS NULL;
UPDATE place SET p_address = '臺中市西區英才路至館前路一帶' WHERE p_name = '草悟道' AND p_address IS NULL;
UPDATE place SET p_address = '臺中市西屯區河南路三段與市政北七路口' WHERE p_name = '秋紅谷景觀生態公園' AND p_address IS NULL;
UPDATE place SET p_address = '臺中市南屯區文心南三路（文心森林公園內）' WHERE p_name = '圓滿戶外劇場' AND p_address IS NULL;
UPDATE place SET p_address = '臺中市南屯區文心南五路一段' WHERE p_name = '豐樂雕塑公園' AND p_address IS NULL;
UPDATE place SET p_address = '臺中市烏日區站區二路8號' WHERE p_name = '高鐵臺中站' AND p_address IS NULL;

-- ── 2.1 州廳鐘聲裡的家書（臺中市中區）附近 ──
INSERT INTO place (p_name, p_category, p_type, p_summary, p_address, p_open_time, p_latitude, p_longitude)
SELECT '第四信用合作社', '飲食', '冰品', '老信用合作社建築改成的冰淇淋店，招牌是配料豐富的聖代。', '臺中市中區中山路72號', '10:00-22:00', 24.1390500, 120.6822300 FROM DUAL
WHERE NOT EXISTS (SELECT 1 FROM place WHERE p_name = '第四信用合作社');
INSERT INTO place (p_name, p_category, p_type, p_summary, p_address, p_open_time, p_latitude, p_longitude)
SELECT '老賴紅茶', '飲食', '茶飲', '第二市場裡的老字號紅茶攤，配一份蘿蔔糕是在地人的早餐。', '臺中市中區三民路二段87號（第二市場內）', '06:00-18:00', 24.1424500, 120.6789500 FROM DUAL
WHERE NOT EXISTS (SELECT 1 FROM place WHERE p_name = '老賴紅茶');
INSERT INTO place (p_name, p_category, p_type, p_summary, p_address, p_open_time, p_latitude, p_longitude)
SELECT '山河魯肉飯', '飲食', '小吃', '第二市場的魯肉飯名攤，滷汁香濃，常常中午前就賣完。', '臺中市中區三民路二段87號（第二市場內）', '06:30-14:00', 24.1422800, 120.6786900 FROM DUAL
WHERE NOT EXISTS (SELECT 1 FROM place WHERE p_name = '山河魯肉飯');
INSERT INTO place (p_name, p_category, p_type, p_summary, p_address, p_open_time, p_latitude, p_longitude)
SELECT '一中豐仁冰', '飲食', '冰品', '一中街的老牌剉冰，紅豆與鳳梨醬是招牌。', '臺中市北區一中街46號', '10:00-22:00', 24.1490300, 120.6853800 FROM DUAL
WHERE NOT EXISTS (SELECT 1 FROM place WHERE p_name = '一中豐仁冰');
INSERT INTO place (p_name, p_category, p_type, p_summary, p_address, p_open_time, p_latitude, p_longitude)
SELECT '綠川水岸廊道', '其他', '河岸步道', '整治後的綠川兩岸步道，晚上點燈很適合散步。', '臺中市中區綠川西街', '全天開放', 24.1395000, 120.6849000 FROM DUAL
WHERE NOT EXISTS (SELECT 1 FROM place WHERE p_name = '綠川水岸廊道');
INSERT INTO place (p_name, p_category, p_type, p_summary, p_address, p_open_time, p_latitude, p_longitude)
SELECT '臺中文化創意產業園區', '其他', '文創園區', '由舊臺中酒廠改建，常有展覽與市集。', '臺中市南區復興路三段362號', '09:00-17:00（週一休）', 24.1332000, 120.6818000 FROM DUAL
WHERE NOT EXISTS (SELECT 1 FROM place WHERE p_name = '臺中文化創意產業園區');

-- ── 2.2 綠線上的時光膠囊（臺中市西屯區）附近 ──
INSERT INTO place (p_name, p_category, p_type, p_summary, p_address, p_open_time, p_latitude, p_longitude)
SELECT '新光三越台中中港店', '其他', '百貨商場', '市政府站旁的大型百貨，美食街與伴手禮一次買齊。', '臺中市西屯區臺灣大道三段301號', '11:00-22:00', 24.1651000, 120.6441000 FROM DUAL
WHERE NOT EXISTS (SELECT 1 FROM place WHERE p_name = '新光三越台中中港店');
INSERT INTO place (p_name, p_category, p_type, p_summary, p_address, p_open_time, p_latitude, p_longitude)
SELECT '南屯老街', '飲食', '老街小吃', '萬和宮前的老街，麻芛湯與傳統糕餅是在地特色。', '臺中市南屯區萬和路一段', '09:00-18:00（各店不同）', 24.1381500, 120.6394000 FROM DUAL
WHERE NOT EXISTS (SELECT 1 FROM place WHERE p_name = '南屯老街');

-- ── 2.3 捷運迷城的懸案（臺北市）附近 ──
INSERT INTO place (p_name, p_category, p_type, p_summary, p_address, p_open_time, p_latitude, p_longitude)
SELECT '阿宗麵線', '飲食', '小吃', '西門町排隊名店，大腸麵線站著吃是傳統。', '臺北市萬華區峨眉街8之1號', '08:30-22:30', 25.0432600, 121.5077500 FROM DUAL
WHERE NOT EXISTS (SELECT 1 FROM place WHERE p_name = '阿宗麵線');
INSERT INTO place (p_name, p_category, p_type, p_summary, p_address, p_open_time, p_latitude, p_longitude)
SELECT '周記肉粥店', '飲食', '小吃', '龍山寺旁的老店，肉粥配紅燒肉是艋舺早午餐的經典組合。', '臺北市萬華區廣州街104號', '06:30-16:00', 25.0365800, 121.5026000 FROM DUAL
WHERE NOT EXISTS (SELECT 1 FROM place WHERE p_name = '周記肉粥店');
INSERT INTO place (p_name, p_category, p_type, p_summary, p_address, p_open_time, p_latitude, p_longitude)
SELECT '永康牛肉麵', '飲食', '麵食', '東門一帶的老牌川味紅燒牛肉麵。', '臺北市大安區金山南路二段31巷17號', '11:00-21:00', 25.0330000, 121.5293000 FROM DUAL
WHERE NOT EXISTS (SELECT 1 FROM place WHERE p_name = '永康牛肉麵');
INSERT INTO place (p_name, p_category, p_type, p_summary, p_address, p_open_time, p_latitude, p_longitude)
SELECT '鼎泰豐 101 店', '飲食', '小籠包', '台北101 地下樓層的鼎泰豐，可以隔著玻璃看師傅包小籠包。', '臺北市信義區市府路45號B1', '11:00-21:00', 25.0339500, 121.5645500 FROM DUAL
WHERE NOT EXISTS (SELECT 1 FROM place WHERE p_name = '鼎泰豐 101 店');
INSERT INTO place (p_name, p_category, p_type, p_summary, p_address, p_open_time, p_latitude, p_longitude)
SELECT '蜂大咖啡', '飲食', '咖啡', '西門町的老咖啡館，自家烘焙咖啡豆與杏仁酥很有名。', '臺北市萬華區成都路42號', '08:00-22:00', 25.0423500, 121.5055000 FROM DUAL
WHERE NOT EXISTS (SELECT 1 FROM place WHERE p_name = '蜂大咖啡');
INSERT INTO place (p_name, p_category, p_type, p_summary, p_address, p_open_time, p_latitude, p_longitude)
SELECT '剝皮寮歷史街區', '其他', '歷史街區', '保存清代到日治時期的街屋，紅磚拱廊很好拍。', '臺北市萬華區康定路173巷', '09:00-18:00（週一休）', 25.0372800, 121.5017500 FROM DUAL
WHERE NOT EXISTS (SELECT 1 FROM place WHERE p_name = '剝皮寮歷史街區');
INSERT INTO place (p_name, p_category, p_type, p_summary, p_address, p_open_time, p_latitude, p_longitude)
SELECT '二二八和平紀念公園', '其他', '公園', '臺博館所在的公園，池塘與涼亭是城中綠洲。', '臺北市中正區凱達格蘭大道3號', '全天開放', 25.0404500, 121.5154000 FROM DUAL
WHERE NOT EXISTS (SELECT 1 FROM place WHERE p_name = '二二八和平紀念公園');
INSERT INTO place (p_name, p_category, p_type, p_summary, p_address, p_open_time, p_latitude, p_longitude)
SELECT '光點華山電影館', '其他', '電影院', '華山園區旁的藝術電影院，放映獨立與經典電影。', '臺北市中正區八德路一段18號', '11:00-22:00', 25.0443500, 121.5288000 FROM DUAL
WHERE NOT EXISTS (SELECT 1 FROM place WHERE p_name = '光點華山電影館');
INSERT INTO place (p_name, p_category, p_type, p_summary, p_address, p_open_time, p_latitude, p_longitude)
SELECT '四四南村', '其他', '眷村', '台北101 腳下保留下來的眷村建築，假日常有市集。', '臺北市信義區松勤街50號', '09:00-17:00（週一休）', 25.0313500, 121.5617000 FROM DUAL
WHERE NOT EXISTS (SELECT 1 FROM place WHERE p_name = '四四南村');

-- ── 3. 照片（Wikimedia Commons 縮圖，CC 授權，前端卡片會標示出處並連到檔案頁）──
-- 老賴紅茶、山河魯肉飯、一中豐仁冰、忠信市場在 Commons 找不到本店照片，先留空。
-- 宮原眼科：Miyahara Eye Hospital in March 2026.jpg（姒姓賢寧，CC BY-SA 4.0）
UPDATE place SET p_image = 'https://thumb.wikimedia.org/wikipedia/commons/thumb/2/21/Miyahara_Eye_Hospital_in_March_2026.jpg/960px-Miyahara_Eye_Hospital_in_March_2026.jpg' WHERE p_name = '宮原眼科' AND (p_image IS NULL OR p_image = '');
-- 臺中公園：湖心亭遠景.jpg（Outlookxp，CC BY-SA 4.0）
UPDATE place SET p_image = 'https://thumb.wikimedia.org/wikipedia/commons/thumb/5/57/%E6%B9%96%E5%BF%83%E4%BA%AD%E9%81%A0%E6%99%AF.jpg/960px-%E6%B9%96%E5%BF%83%E4%BA%AD%E9%81%A0%E6%99%AF.jpg' WHERE p_name = '臺中公園' AND (p_image IS NULL OR p_image = '');
-- 臺中市役所：台中市役所 (4).jpg（Daniel551727，CC BY-SA 4.0）
UPDATE place SET p_image = 'https://thumb.wikimedia.org/wikipedia/commons/thumb/1/1d/%E5%8F%B0%E4%B8%AD%E5%B8%82%E5%BD%B9%E6%89%80_%284%29.jpg/960px-%E5%8F%B0%E4%B8%AD%E5%B8%82%E5%BD%B9%E6%89%80_%284%29.jpg' WHERE p_name = '臺中市役所' AND (p_image IS NULL OR p_image = '');
-- 臺中州廳：Taichung Prefectural Hall 2022.jpg（台中市政府，Attribution）
UPDATE place SET p_image = 'https://thumb.wikimedia.org/wikipedia/commons/thumb/2/26/Taichung_Prefectural_Hall_2022.jpg/960px-Taichung_Prefectural_Hall_2022.jpg' WHERE p_name = '臺中州廳' AND (p_image IS NULL OR p_image = '');
-- 臺中文學館：Taichung Literature Museum-01.2024-12-06.jpg（阿道，CC BY-SA 4.0）
UPDATE place SET p_image = 'https://thumb.wikimedia.org/wikipedia/commons/thumb/b/b2/Taichung_Literature_Museum-01.2024-12-06.jpg/960px-Taichung_Literature_Museum-01.2024-12-06.jpg' WHERE p_name = '臺中文學館' AND (p_image IS NULL OR p_image = '');
-- 審計新村：Shenji New Village view2 201905.jpg（Wpcpey，CC BY 4.0）
UPDATE place SET p_image = 'https://thumb.wikimedia.org/wikipedia/commons/thumb/5/5e/Shenji_New_Village_view2_201905.jpg/960px-Shenji_New_Village_view2_201905.jpg' WHERE p_name = '審計新村' AND (p_image IS NULL OR p_image = '');
-- 國立台灣美術館：國立臺灣美術館景2.jpg（National Taiwan Museum of Fine Arts，CC BY-SA 4.0）
UPDATE place SET p_image = 'https://thumb.wikimedia.org/wikipedia/commons/thumb/8/8e/%E5%9C%8B%E7%AB%8B%E8%87%BA%E7%81%A3%E7%BE%8E%E8%A1%93%E9%A4%A8%E6%99%AF2.jpg/960px-%E5%9C%8B%E7%AB%8B%E8%87%BA%E7%81%A3%E7%BE%8E%E8%A1%93%E9%A4%A8%E6%99%AF2.jpg' WHERE p_name = '國立台灣美術館' AND (p_image IS NULL OR p_image = '');
-- 勤美誠品綠園道：Park Lane by CMP 20170206.jpg（Accord14，CC BY-SA 4.0）
UPDATE place SET p_image = 'https://thumb.wikimedia.org/wikipedia/commons/thumb/8/88/Park_Lane_by_CMP_20170206.jpg/960px-Park_Lane_by_CMP_20170206.jpg' WHERE p_name = '勤美誠品綠園道' AND (p_image IS NULL OR p_image = '');
-- 草悟道：Calligraphy Greenway Square.jpg（台中市政府，Attribution）
UPDATE place SET p_image = 'https://thumb.wikimedia.org/wikipedia/commons/thumb/c/c5/Calligraphy_Greenway_Square.jpg/960px-Calligraphy_Greenway_Square.jpg' WHERE p_name = '草悟道' AND (p_image IS NULL OR p_image = '');
-- 國立自然科學博物館：Entrance building of National Museum of Natural Science.jpg（Tolok Matzari，CC BY-SA 4.0）
UPDATE place SET p_image = 'https://thumb.wikimedia.org/wikipedia/commons/thumb/e/e4/Entrance_building_of_National_Museum_of_Natural_Science.jpg/960px-Entrance_building_of_National_Museum_of_Natural_Science.jpg' WHERE p_name = '國立自然科學博物館' AND (p_image IS NULL OR p_image = '');
-- 臺中市政府ˍ新市政大樓：Taichung City Hall 20190712 (cropped).jpg（台中市政府，Attribution）
UPDATE place SET p_image = 'https://thumb.wikimedia.org/wikipedia/commons/thumb/d/d2/Taichung_City_Hall_20190712_%28cropped%29.jpg/960px-Taichung_City_Hall_20190712_%28cropped%29.jpg' WHERE p_name = '臺中市政府ˍ新市政大樓' AND (p_image IS NULL OR p_image = '');
-- 臺中國家歌劇院：National Taichung Theater aerial view 2019.jpg（Wpcpey，CC BY 4.0）
UPDATE place SET p_image = 'https://thumb.wikimedia.org/wikipedia/commons/thumb/3/31/National_Taichung_Theater_aerial_view_2019.jpg/960px-National_Taichung_Theater_aerial_view_2019.jpg' WHERE p_name = '臺中國家歌劇院' AND (p_image IS NULL OR p_image = '');
-- 秋紅谷景觀生態公園：Maple Garden from above.jpg（xiquinhosilva，CC BY 2.0）
UPDATE place SET p_image = 'https://thumb.wikimedia.org/wikipedia/commons/thumb/1/14/Maple_Garden_from_above.jpg/960px-Maple_Garden_from_above.jpg' WHERE p_name = '秋紅谷景觀生態公園' AND (p_image IS NULL OR p_image = '');
-- 圓滿戶外劇場：文心森林公園.jpg（Fcuk1203，CC BY-SA 3.0）
UPDATE place SET p_image = 'https://thumb.wikimedia.org/wikipedia/commons/thumb/7/7a/%E6%96%87%E5%BF%83%E6%A3%AE%E6%9E%97%E5%85%AC%E5%9C%92.jpg/960px-%E6%96%87%E5%BF%83%E6%A3%AE%E6%9E%97%E5%85%AC%E5%9C%92.jpg' WHERE p_name = '圓滿戶外劇場' AND (p_image IS NULL OR p_image = '');
-- 豐樂雕塑公園：FengLe Sculpture Park 20141231.jpg（Hsin-Jen Hsu，CC BY 2.0）
UPDATE place SET p_image = 'https://thumb.wikimedia.org/wikipedia/commons/thumb/e/e7/FengLe_Sculpture_Park_20141231.jpg/960px-FengLe_Sculpture_Park_20141231.jpg' WHERE p_name = '豐樂雕塑公園' AND (p_image IS NULL OR p_image = '');
-- 國立臺灣博物館：國立臺灣博物館正門.jpg（Adece033090，CC BY-SA 4.0）
UPDATE place SET p_image = 'https://thumb.wikimedia.org/wikipedia/commons/thumb/f/f1/%E5%9C%8B%E7%AB%8B%E8%87%BA%E7%81%A3%E5%8D%9A%E7%89%A9%E9%A4%A8%E6%AD%A3%E9%96%80.jpg/960px-%E5%9C%8B%E7%AB%8B%E8%87%BA%E7%81%A3%E5%8D%9A%E7%89%A9%E9%A4%A8%E6%AD%A3%E9%96%80.jpg' WHERE p_name = '國立臺灣博物館' AND (p_image IS NULL OR p_image = '');
-- 國立中正紀念堂：2022年的中正紀念堂.jpg（Mrmarkertw，CC BY-SA 4.0）
UPDATE place SET p_image = 'https://thumb.wikimedia.org/wikipedia/commons/thumb/0/0d/2022%E5%B9%B4%E7%9A%84%E4%B8%AD%E6%AD%A3%E7%B4%80%E5%BF%B5%E5%A0%82.jpg/960px-2022%E5%B9%B4%E7%9A%84%E4%B8%AD%E6%AD%A3%E7%B4%80%E5%BF%B5%E5%A0%82.jpg' WHERE p_name = '國立中正紀念堂' AND (p_image IS NULL OR p_image = '');
-- 艋舺龍山寺：Bangka Lungshan Temple (cropped).jpg（臺北旅遊網，Attribution）
UPDATE place SET p_image = 'https://thumb.wikimedia.org/wikipedia/commons/thumb/9/92/Bangka_Lungshan_Temple_%28cropped%29.jpg/960px-Bangka_Lungshan_Temple_%28cropped%29.jpg' WHERE p_name = '艋舺龍山寺' AND (p_image IS NULL OR p_image = '');
-- 西門町：Ximending rainbow crossing 20260326 (IMG 8656).jpg（Bigmorr，CC BY-SA 4.0）
UPDATE place SET p_image = 'https://thumb.wikimedia.org/wikipedia/commons/thumb/2/20/Ximending_rainbow_crossing_20260326_%28IMG_8656%29.jpg/960px-Ximending_rainbow_crossing_20260326_%28IMG_8656%29.jpg' WHERE p_name = '西門町' AND (p_image IS NULL OR p_image = '');
-- 華山1914文化創意產業園區：Huashan 1914, Syntrend and Jinshan e01 20150701.jpg（Wpcpey，CC BY 4.0）
UPDATE place SET p_image = 'https://thumb.wikimedia.org/wikipedia/commons/thumb/5/55/Huashan_1914%2C_Syntrend_and_Jinshan_e01_20150701.jpg/960px-Huashan_1914%2C_Syntrend_and_Jinshan_e01_20150701.jpg' WHERE p_name = '華山1914文化創意產業園區' AND (p_image IS NULL OR p_image = '');
-- 松山文創園區：松山菸廠製菸工廠北面.jpg（寺人孟子，CC BY-SA 4.0）
UPDATE place SET p_image = 'https://thumb.wikimedia.org/wikipedia/commons/thumb/a/a2/%E6%9D%BE%E5%B1%B1%E8%8F%B8%E5%BB%A0%E8%A3%BD%E8%8F%B8%E5%B7%A5%E5%BB%A0%E5%8C%97%E9%9D%A2.jpg/960px-%E6%9D%BE%E5%B1%B1%E8%8F%B8%E5%BB%A0%E8%A3%BD%E8%8F%B8%E5%B7%A5%E5%BB%A0%E5%8C%97%E9%9D%A2.jpg' WHERE p_name = '松山文創園區' AND (p_image IS NULL OR p_image = '');
-- 國立國父紀念館：Taipeh Taipei 101 Blick von der Aussichtsplattform auf Sun Yat Sen Memorial 4.jpg（Zairon，CC BY-SA 4.0）
UPDATE place SET p_image = 'https://thumb.wikimedia.org/wikipedia/commons/thumb/d/d4/Taipeh_Taipei_101_Blick_von_der_Aussichtsplattform_auf_Sun_Yat_Sen_Memorial_4.jpg/960px-Taipeh_Taipei_101_Blick_von_der_Aussichtsplattform_auf_Sun_Yat_Sen_Memorial_4.jpg' WHERE p_name = '國立國父紀念館' AND (p_image IS NULL OR p_image = '');
-- 台北101：Taipei Taiwan Taipei-101-Tower-01.jpg（CEphoto, Uwe Aranas，CC BY-SA 3.0）
UPDATE place SET p_image = 'https://thumb.wikimedia.org/wikipedia/commons/thumb/2/2d/Taipei_Taiwan_Taipei-101-Tower-01.jpg/960px-Taipei_Taiwan_Taipei-101-Tower-01.jpg' WHERE p_name = '台北101' AND (p_image IS NULL OR p_image = '');
-- 綠川水岸廊道：Luchuan Canal (Taichung) night view 201905.jpg（Wpcpey，CC BY-SA 4.0）
UPDATE place SET p_image = 'https://thumb.wikimedia.org/wikipedia/commons/thumb/e/ea/Luchuan_Canal_%28Taichung%29_night_view_201905.jpg/960px-Luchuan_Canal_%28Taichung%29_night_view_201905.jpg' WHERE p_name = '綠川水岸廊道' AND (p_image IS NULL OR p_image = '');
-- 臺中文化創意產業園區：Taichung Creative and Cultural Park.JPG（Fcuk1203，CC BY-SA 3.0）
UPDATE place SET p_image = 'https://thumb.wikimedia.org/wikipedia/commons/thumb/d/d9/Taichung_Creative_and_Cultural_Park.JPG/960px-Taichung_Creative_and_Cultural_Park.JPG' WHERE p_name = '臺中文化創意產業園區' AND (p_image IS NULL OR p_image = '');
-- 鼎泰豐 101 店：鼎泰豐台北101店.jpg（Legoleehk，CC BY-SA 3.0）
UPDATE place SET p_image = 'https://thumb.wikimedia.org/wikipedia/commons/thumb/f/f7/%E9%BC%8E%E6%B3%B0%E8%B1%90%E5%8F%B0%E5%8C%97101%E5%BA%97.jpg/960px-%E9%BC%8E%E6%B3%B0%E8%B1%90%E5%8F%B0%E5%8C%97101%E5%BA%97.jpg' WHERE p_name = '鼎泰豐 101 店' AND (p_image IS NULL OR p_image = '');
-- 剝皮寮歷史街區：剝皮寮歷史街區20220617.jpg（Yu tptw，CC BY-SA 4.0）
UPDATE place SET p_image = 'https://thumb.wikimedia.org/wikipedia/commons/thumb/6/6d/%E5%89%9D%E7%9A%AE%E5%AF%AE%E6%AD%B7%E5%8F%B2%E8%A1%97%E5%8D%8020220617.jpg/960px-%E5%89%9D%E7%9A%AE%E5%AF%AE%E6%AD%B7%E5%8F%B2%E8%A1%97%E5%8D%8020220617.jpg' WHERE p_name = '剝皮寮歷史街區' AND (p_image IS NULL OR p_image = '');
-- 二二八和平紀念公園：Taipei228PeaceMemorialPark.jpg（Peellden，CC BY-SA 3.0）
UPDATE place SET p_image = 'https://thumb.wikimedia.org/wikipedia/commons/thumb/2/24/Taipei228PeaceMemorialPark.jpg/960px-Taipei228PeaceMemorialPark.jpg' WHERE p_name = '二二八和平紀念公園' AND (p_image IS NULL OR p_image = '');
-- 四四南村：四四南村.jpg（Men1399，CC BY-SA 3.0）
UPDATE place SET p_image = 'https://thumb.wikimedia.org/wikipedia/commons/thumb/9/91/%E5%9B%9B%E5%9B%9B%E5%8D%97%E6%9D%91.jpg/960px-%E5%9B%9B%E5%9B%9B%E5%8D%97%E6%9D%91.jpg' WHERE p_name = '四四南村' AND (p_image IS NULL OR p_image = '');
-- 第二市場：臺中第二市場六角樓.JPG（Pbdragonwang，CC BY-SA 3.0）
UPDATE place SET p_image = 'https://thumb.wikimedia.org/wikipedia/commons/thumb/1/1b/%E8%87%BA%E4%B8%AD%E7%AC%AC%E4%BA%8C%E5%B8%82%E5%A0%B4%E5%85%AD%E8%A7%92%E6%A8%93.JPG/960px-%E8%87%BA%E4%B8%AD%E7%AC%AC%E4%BA%8C%E5%B8%82%E5%A0%B4%E5%85%AD%E8%A7%92%E6%A8%93.JPG' WHERE p_name = '第二市場' AND (p_image IS NULL OR p_image = '');
-- 萬和宮：萬和宮正面照.jpg（Outlookxp，CC BY-SA 4.0）
UPDATE place SET p_image = 'https://thumb.wikimedia.org/wikipedia/commons/thumb/6/6b/%E8%90%AC%E5%92%8C%E5%AE%AE%E6%AD%A3%E9%9D%A2%E7%85%A7.jpg/960px-%E8%90%AC%E5%92%8C%E5%AE%AE%E6%AD%A3%E9%9D%A2%E7%85%A7.jpg' WHERE p_name = '萬和宮' AND (p_image IS NULL OR p_image = '');
-- 高鐵臺中站：Taiwan HighSpeedRail TaiChung Station 4.JPG（vegafish，CC BY-SA 2.5）
UPDATE place SET p_image = 'https://thumb.wikimedia.org/wikipedia/commons/thumb/8/80/Taiwan_HighSpeedRail_TaiChung_Station_4.JPG/960px-Taiwan_HighSpeedRail_TaiChung_Station_4.JPG' WHERE p_name = '高鐵臺中站' AND (p_image IS NULL OR p_image = '');
-- 第四信用合作社：日出第四信用合作社門市.jpg（Fcuk1203，CC BY-SA 3.0）
UPDATE place SET p_image = 'https://upload.wikimedia.org/wikipedia/commons/9/9f/%E6%97%A5%E5%87%BA%E7%AC%AC%E5%9B%9B%E4%BF%A1%E7%94%A8%E5%90%88%E4%BD%9C%E7%A4%BE%E9%96%80%E5%B8%82.jpg' WHERE p_name = '第四信用合作社' AND (p_image IS NULL OR p_image = '');
-- 新光三越台中中港店：Shin Kong Mitsukoshi Taichung Zhonggang.jpg（黃 zero，CC BY-SA 2.0）
UPDATE place SET p_image = 'https://thumb.wikimedia.org/wikipedia/commons/thumb/0/0d/Shin_Kong_Mitsukoshi_Taichung_Zhonggang.jpg/960px-Shin_Kong_Mitsukoshi_Taichung_Zhonggang.jpg' WHERE p_name = '新光三越台中中港店' AND (p_image IS NULL OR p_image = '');
-- 南屯老街：南屯三角街 20241205.jpg（阿道，CC BY-SA 4.0）
UPDATE place SET p_image = 'https://thumb.wikimedia.org/wikipedia/commons/thumb/b/bf/%E5%8D%97%E5%B1%AF%E4%B8%89%E8%A7%92%E8%A1%97_20241205.jpg/960px-%E5%8D%97%E5%B1%AF%E4%B8%89%E8%A7%92%E8%A1%97_20241205.jpg' WHERE p_name = '南屯老街' AND (p_image IS NULL OR p_image = '');
-- 阿宗麵線：Ay-Chung Flour-Rice Noodle 20190113.jpg（Solomon203，CC BY-SA 4.0）
UPDATE place SET p_image = 'https://thumb.wikimedia.org/wikipedia/commons/thumb/4/4a/Ay-Chung_Flour-Rice_Noodle_20190113.jpg/960px-Ay-Chung_Flour-Rice_Noodle_20190113.jpg' WHERE p_name = '阿宗麵線' AND (p_image IS NULL OR p_image = '');
-- 周記肉粥店：Chou's Rice Congee & Meat Shop 20101003.jpg（玄史生，CC BY-SA 3.0）
UPDATE place SET p_image = 'https://thumb.wikimedia.org/wikipedia/commons/thumb/d/d9/Chou%27s_Rice_Congee_%26_Meat_Shop_20101003.jpg/960px-Chou%27s_Rice_Congee_%26_Meat_Shop_20101003.jpg' WHERE p_name = '周記肉粥店' AND (p_image IS NULL OR p_image = '');
-- 永康牛肉麵：Yong-Kang Beef Noodle 20220918.jpg（Solomon203，CC BY-SA 4.0）
UPDATE place SET p_image = 'https://thumb.wikimedia.org/wikipedia/commons/thumb/9/9b/Yong-Kang_Beef_Noodle_20220918.jpg/960px-Yong-Kang_Beef_Noodle_20220918.jpg' WHERE p_name = '永康牛肉麵' AND (p_image IS NULL OR p_image = '');
-- 蜂大咖啡：Fong Da Coffee, Ximending 20220313.jpg（Solomon203，CC BY-SA 4.0）
UPDATE place SET p_image = 'https://thumb.wikimedia.org/wikipedia/commons/thumb/f/f7/Fong_Da_Coffee%2C_Ximending_20220313.jpg/960px-Fong_Da_Coffee%2C_Ximending_20220313.jpg' WHERE p_name = '蜂大咖啡' AND (p_image IS NULL OR p_image = '');
-- 光點華山電影館：SPOT-Huashan outside space 2015.jpg（Wpcpey，CC BY 4.0）
UPDATE place SET p_image = 'https://thumb.wikimedia.org/wikipedia/commons/thumb/9/9c/SPOT-Huashan_outside_space_2015.jpg/960px-SPOT-Huashan_outside_space_2015.jpg' WHERE p_name = '光點華山電影館' AND (p_image IS NULL OR p_image = '');

COMMIT;

-- 檢查：各劇本周邊好去的地點數（同 /api/Map/{story_id}/Nearby 的條件）
SELECT s.s_id, s.story_title,
       SUM(p.p_category = '飲食') AS food,
       SUM(p.p_category <> '飲食' OR p.p_category IS NULL) AS others
FROM story s
INNER JOIN place p ON s.city_name IS NULL OR p.p_address LIKE CONCAT('%', s.city_name, '%')
WHERE s.story_title IN ('州廳鐘聲裡的家書', '綠線上的時光膠囊', '捷運迷城的懸案')
GROUP BY s.s_id, s.story_title;
