
/*!40101 SET @OLD_CHARACTER_SET_CLIENT=@@CHARACTER_SET_CLIENT */;
/*!40101 SET @OLD_CHARACTER_SET_RESULTS=@@CHARACTER_SET_RESULTS */;
/*!40101 SET @OLD_COLLATION_CONNECTION=@@COLLATION_CONNECTION */;
/*!50503 SET NAMES utf8mb4 */;
/*!40103 SET @OLD_TIME_ZONE=@@TIME_ZONE */;
/*!40103 SET TIME_ZONE='+00:00' */;
/*!40014 SET @OLD_UNIQUE_CHECKS=@@UNIQUE_CHECKS, UNIQUE_CHECKS=0 */;
/*!40014 SET @OLD_FOREIGN_KEY_CHECKS=@@FOREIGN_KEY_CHECKS, FOREIGN_KEY_CHECKS=0 */;
/*!40101 SET @OLD_SQL_MODE=@@SQL_MODE, SQL_MODE='NO_AUTO_VALUE_ON_ZERO' */;
/*!40111 SET @OLD_SQL_NOTES=@@SQL_NOTES, SQL_NOTES=0 */;
DROP TABLE IF EXISTS `au_badge`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `au_badge` (
  `ab_id` int NOT NULL AUTO_INCREMENT COMMENT '使用者勳章流水號',
  `au_id` int NOT NULL COMMENT '使用者流水號',
  `b_id` int NOT NULL COMMENT '勳章流水號',
  `s_id` int DEFAULT NULL COMMENT '從哪個劇本抽到',
  `created_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT '創建時間',
  `updated_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP COMMENT '更新時間',
  PRIMARY KEY (`ab_id`) USING BTREE,
  UNIQUE KEY `uk_au_badge` (`au_id`,`b_id`) USING BTREE,
  UNIQUE KEY `uk_au_badge_story` (`au_id`,`s_id`),
  KEY `idx_au_badge_au_id` (`au_id`) USING BTREE,
  KEY `idx_au_badge_b_id` (`b_id`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci ROW_FORMAT=DYNAMIC COMMENT='使用者勳章對照表';
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `au_vlog`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `au_vlog` (
  `av_id` int NOT NULL AUTO_INCREMENT COMMENT 'VLOG表流水號',
  `au_id` int NOT NULL COMMENT '建立VLOG使用者ID',
  `s_id` int DEFAULT NULL COMMENT '所屬劇本流水號',
  `av_title` text COLLATE utf8mb4_general_ci COMMENT 'VLOG標題',
  `av_final_script` text COLLATE utf8mb4_general_ci COMMENT '確認後送出合成的最終旁白',
  `av_seo_keywords` text COLLATE utf8mb4_general_ci COMMENT 'seo關鍵字',
  `av_promo_copy` text COLLATE utf8mb4_general_ci COMMENT '宣傳文案',
  `av_video_url` text COLLATE utf8mb4_general_ci COMMENT '正式完成的影片網址',
  `av_thumbnail` text COLLATE utf8mb4_general_ci COMMENT 'vlog封面',
  `av_vlog_status` tinyint NOT NULL DEFAULT '1' COMMENT 'vlog狀態(1=待處理、2=處理中、3=已完成、4=失敗)',
  `error_message` text COLLATE utf8mb4_general_ci COMMENT '失敗錯誤訊息',
  `created_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT '建立時間',
  `updated_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP COMMENT '最後更新時間',
  PRIMARY KEY (`av_id`) USING BTREE,
  KEY `idx_vlog_au_id` (`au_id`) USING BTREE,
  KEY `idx_vlog_s_id` (`s_id`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci ROW_FORMAT=DYNAMIC COMMENT='遊客VLOG表';
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `auth`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `auth` (
  `au_id` int NOT NULL AUTO_INCREMENT COMMENT '使用者流水號',
  `auth_name` varchar(255) COLLATE utf8mb4_general_ci NOT NULL COMMENT '使用者名稱',
  `auth_type` tinyint NOT NULL DEFAULT '1' COMMENT '使用者身分 1=遊客、2=商家、3=管理員',
  `auth_email` varchar(255) COLLATE utf8mb4_general_ci NOT NULL COMMENT '使用者gmail',
  `auth_pswd` varchar(255) COLLATE utf8mb4_general_ci NOT NULL COMMENT '使用者密碼雜湊值',
  `is_active` tinyint NOT NULL DEFAULT '1' COMMENT '是否啟用：1＝啟用、0＝停用',
  `is_email_verified` tinyint NOT NULL DEFAULT '0' COMMENT 'Gmail是否已驗證',
  `au_email_token` varchar(255) COLLATE utf8mb4_general_ci DEFAULT NULL COMMENT '使用者驗證Token',
  `au_email_expires` datetime DEFAULT NULL COMMENT 'gmail驗證令牌到期時間',
  `last_login_at` datetime DEFAULT NULL COMMENT '使用者最後登入時間',
  `pwd_reset_token` varchar(255) COLLATE utf8mb4_general_ci DEFAULT NULL COMMENT '忘記密碼重設Token的雜湊值',
  `pwd_reset_expires` datetime DEFAULT NULL COMMENT '忘記密碼重設Token的到期時間',
  `au_birthday` date DEFAULT NULL COMMENT '使用者生日',
  `au_gender` varchar(20) COLLATE utf8mb4_general_ci DEFAULT NULL COMMENT '使用者性別',
  `created_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT '帳號創建時間',
  `updated_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP COMMENT '帳號更新時間',
  PRIMARY KEY (`au_id`) USING BTREE,
  UNIQUE KEY `uk_auth_email` (`auth_email`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci ROW_FORMAT=DYNAMIC COMMENT='使用者主表';
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `badge`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `badge` (
  `b_id` int NOT NULL AUTO_INCREMENT COMMENT '勳章流水號ID',
  `b_fication` varchar(255) COLLATE utf8mb4_general_ci DEFAULT NULL COMMENT '勳章分類',
  `b_thing` varchar(255) COLLATE utf8mb4_general_ci DEFAULT NULL COMMENT '勳章對應物件',
  `b_name` varchar(255) COLLATE utf8mb4_general_ci NOT NULL COMMENT '勳章名字',
  `b_image` text COLLATE utf8mb4_general_ci COMMENT '勳章圖片網址',
  `created_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT '創建時間',
  `updated_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP COMMENT '更新時間',
  PRIMARY KEY (`b_id`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci ROW_FORMAT=DYNAMIC COMMENT='勳章主表';
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `bus_route`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `bus_route` (
  `br_id` int NOT NULL AUTO_INCREMENT COMMENT '公車路線流水號',
  `route_uid` varchar(50) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NOT NULL COMMENT 'TDX 路線唯一代碼 RouteUID，例如 TXG300',
  `route_type` tinyint NOT NULL DEFAULT '1' COMMENT '路線類型：1=市區公車、2=公路客運、3=台灣好行',
  `route_name` varchar(100) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NOT NULL COMMENT '路線名稱，例如「300」、「台灣好行 日月潭線」',
  `city_name` varchar(100) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci DEFAULT NULL COMMENT '所屬縣市，公路客運/台灣好行跨縣市時為 NULL',
  `departure_stop_name` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci DEFAULT NULL COMMENT '路線招牌上的起站名稱（TDX 路線屬性，不一定等於第 1 站站牌名稱）',
  `destination_stop_name` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci DEFAULT NULL COMMENT '路線招牌上的迄站名稱（TDX 路線屬性，不一定等於最後一站站牌名稱）',
  `fare_desc` text CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci COMMENT '票價說明（台灣好行的一日券/套票也寫在這裡）',
  `route_map_url` text CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci COMMENT '官方路線圖網址',
  `headway_desc` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci DEFAULT NULL COMMENT '班距說明，例如「尖峰 10~15 分、離峰 20~30 分」',
  `is_active` tinyint NOT NULL DEFAULT '1' COMMENT '是否營運中：1=是、2=否（停駛/改線後保留紀錄）',
  `data_updated_at` datetime DEFAULT NULL COMMENT 'TDX 資料版本時間，用來判斷要不要重新同步',
  `created_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT '建立時間',
  `updated_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP COMMENT '最後更新時間',
  PRIMARY KEY (`br_id`) USING BTREE,
  UNIQUE KEY `uk_br_route_uid` (`route_uid`) USING BTREE,
  KEY `idx_br_type_city` (`route_type`,`city_name`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci ROW_FORMAT=DYNAMIC COMMENT='公車路線主表（含台灣好行）';
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `bus_route_pattern`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `bus_route_pattern` (
  `brp_id` int NOT NULL AUTO_INCREMENT COMMENT '行駛型態流水號',
  `bsr_id` int NOT NULL COMMENT '子路線，對應 bus_sub_route.bsr_id',
  `direction` tinyint NOT NULL DEFAULT '0' COMMENT '方向：0=去程、1=返程、2=迴圈',
  `headsign` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci DEFAULT NULL COMMENT '車頭顯示的往向，例如「往 臺中車站」',
  PRIMARY KEY (`brp_id`) USING BTREE,
  UNIQUE KEY `uk_brp_sub_route_dir` (`bsr_id`,`direction`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci ROW_FORMAT=DYNAMIC COMMENT='行駛型態（子路線 × 方向），站序、線形、班表都掛在這裡';
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `bus_route_shape`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `bus_route_shape` (
  `brp_id` int NOT NULL COMMENT '行駛型態，對應 bus_route_pattern.brp_id',
  `geometry` mediumtext CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NOT NULL COMMENT '路線線形，WKT LINESTRING 格式（TDX Shape 原樣存入）',
  PRIMARY KEY (`brp_id`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci ROW_FORMAT=DYNAMIC COMMENT='公車路線線形';
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `bus_route_stop`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `bus_route_stop` (
  `brs_id` int NOT NULL AUTO_INCREMENT COMMENT '路線站序流水號',
  `brp_id` int NOT NULL COMMENT '行駛型態，對應 bus_route_pattern.brp_id',
  `bs_id` int NOT NULL COMMENT '站牌，對應 bus_stop.bs_id',
  `stop_sequence` int NOT NULL COMMENT '站序，從 1 開始',
  PRIMARY KEY (`brs_id`) USING BTREE,
  UNIQUE KEY `uk_brs_pattern_seq` (`brp_id`,`stop_sequence`) USING BTREE,
  KEY `idx_brs_bs_id` (`bs_id`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci ROW_FORMAT=DYNAMIC COMMENT='公車站序（路線改線時整個行駛型態重建）';
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `bus_service_calendar`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `bus_service_calendar` (
  `bsc_id` int NOT NULL AUTO_INCREMENT COMMENT '行駛日型態流水號',
  `run_mon` tinyint NOT NULL DEFAULT '1' COMMENT '週一是否行駛：1=是、2=否',
  `run_tue` tinyint NOT NULL DEFAULT '1' COMMENT '週二是否行駛：1=是、2=否',
  `run_wed` tinyint NOT NULL DEFAULT '1' COMMENT '週三是否行駛：1=是、2=否',
  `run_thu` tinyint NOT NULL DEFAULT '1' COMMENT '週四是否行駛：1=是、2=否',
  `run_fri` tinyint NOT NULL DEFAULT '1' COMMENT '週五是否行駛：1=是、2=否',
  `run_sat` tinyint NOT NULL DEFAULT '1' COMMENT '週六是否行駛：1=是、2=否',
  `run_sun` tinyint NOT NULL DEFAULT '1' COMMENT '週日是否行駛：1=是、2=否',
  `run_holiday` tinyint NOT NULL DEFAULT '1' COMMENT '國定假日是否行駛：1=是、2=否（只在國定假日行駛 = 週一到週日都 2、這欄 1）',
  PRIMARY KEY (`bsc_id`) USING BTREE,
  UNIQUE KEY `uk_bsc_days` (`run_mon`,`run_tue`,`run_wed`,`run_thu`,`run_fri`,`run_sat`,`run_sun`,`run_holiday`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci ROW_FORMAT=DYNAMIC COMMENT='行駛日型態（例如「只有週末」只存一列，所有週末班次共用）';
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `bus_stop`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `bus_stop` (
  `bs_id` int NOT NULL AUTO_INCREMENT COMMENT '站牌流水號',
  `stop_uid` varchar(50) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NOT NULL COMMENT 'TDX 站牌唯一代碼 StopUID',
  `station_id` varchar(50) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci DEFAULT NULL COMMENT 'TDX 站位代碼 StationID，同一路口對向的兩個站牌會共用，用來合併顯示',
  `stop_name` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NOT NULL COMMENT '站牌名稱，例如「臺中車站」',
  `stop_address` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci DEFAULT NULL COMMENT '站牌地址',
  `city_name` varchar(100) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci DEFAULT NULL COMMENT '所在縣市',
  `bs_latitude` decimal(10,7) NOT NULL COMMENT '緯度',
  `bs_longitude` decimal(10,7) NOT NULL COMMENT '經度',
  `created_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT '建立時間',
  `updated_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP COMMENT '最後更新時間',
  PRIMARY KEY (`bs_id`) USING BTREE,
  UNIQUE KEY `uk_bs_stop_uid` (`stop_uid`) USING BTREE,
  KEY `idx_bs_station_id` (`station_id`) USING BTREE,
  KEY `idx_bs_lat_lng` (`bs_latitude`,`bs_longitude`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci ROW_FORMAT=DYNAMIC COMMENT='公車站牌主表（可被多條路線共用）';
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `bus_sub_route`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `bus_sub_route` (
  `bsr_id` int NOT NULL AUTO_INCREMENT COMMENT '子路線流水號',
  `br_id` int NOT NULL COMMENT '所屬路線，對應 bus_route.br_id',
  `sub_route_uid` varchar(50) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NOT NULL COMMENT 'TDX 子路線代碼 SubRouteUID（沒有區間車的路線，只會有一筆主線）',
  `sub_route_name` varchar(100) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci DEFAULT NULL COMMENT '子路線名稱，例如「300 區間車」',
  PRIMARY KEY (`bsr_id`) USING BTREE,
  UNIQUE KEY `uk_bsr_sub_route_uid` (`sub_route_uid`) USING BTREE,
  KEY `idx_bsr_br_id` (`br_id`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci ROW_FORMAT=DYNAMIC COMMENT='公車子路線';
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `bus_timetable`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `bus_timetable` (
  `bt_id` int NOT NULL AUTO_INCREMENT COMMENT '班次流水號',
  `brp_id` int NOT NULL COMMENT '行駛型態，對應 bus_route_pattern.brp_id',
  `bsc_id` int NOT NULL COMMENT '行駛日型態，對應 bus_service_calendar.bsc_id',
  `departure_time` time NOT NULL COMMENT '起站發車時間',
  PRIMARY KEY (`bt_id`) USING BTREE,
  UNIQUE KEY `uk_bt_pattern_calendar_time` (`brp_id`,`bsc_id`,`departure_time`) USING BTREE,
  KEY `idx_bt_bsc_id` (`bsc_id`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci ROW_FORMAT=DYNAMIC COMMENT='公車班表（起站發車時間；市區公車多為固定班距，可只填 bus_route.headway_desc）';
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `coupon`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `coupon` (
  `coupon_id` int NOT NULL AUTO_INCREMENT,
  `s_id` int NOT NULL COMMENT '所屬商家，對應ep_store.s_id',
  `coupon_code` varchar(50) COLLATE utf8mb4_general_ci NOT NULL COMMENT '優惠券代碼，商家自訂',
  `coupon_name` varchar(255) COLLATE utf8mb4_general_ci NOT NULL COMMENT '優惠券名稱/說明',
  `discount_commodity` varchar(255) COLLATE utf8mb4_general_ci NOT NULL COMMENT '折扣品項',
  `discount_type` varchar(10) COLLATE utf8mb4_general_ci NOT NULL COMMENT '折扣類型：percent（百分比折扣）/amount（固定金額折抵）',
  `discount_value` decimal(10,2) NOT NULL COMMENT '折扣數值',
  `valid_from` datetime DEFAULT NULL COMMENT '優惠券生效時間',
  `valid_to` datetime DEFAULT NULL COMMENT '優惠券截止時間',
  `status` varchar(20) COLLATE utf8mb4_general_ci NOT NULL DEFAULT 'active' COMMENT '優惠券狀態：active/inactive，商家可自行下架',
  PRIMARY KEY (`coupon_id`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci ROW_FORMAT=DYNAMIC COMMENT='商家優惠券主檔';
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `merchant_media`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `merchant_media` (
  `mm_id` int NOT NULL AUTO_INCREMENT COMMENT '商家影音流水號',
  `au_id` int NOT NULL COMMENT '建立專案的商家ID',
  `nt_id` int DEFAULT NULL COMMENT '敘事語氣流水號',
  `mm_task_id` varchar(255) COLLATE utf8mb4_general_ci DEFAULT NULL COMMENT '外部任務ID',
  `mm_title` varchar(255) COLLATE utf8mb4_general_ci DEFAULT NULL COMMENT '影音專案標題',
  `mm_description` text COLLATE utf8mb4_general_ci COMMENT '商品或服務說明',
  `mm_text` text COLLATE utf8mb4_general_ci COMMENT '推廣資訊文字',
  `mm_hashtage` text COLLATE utf8mb4_general_ci COMMENT '推薦標籤',
  `mm_video_url` text COLLATE utf8mb4_general_ci COMMENT '正式完成reels影片網址',
  `mm_thumbnail` text COLLATE utf8mb4_general_ci COMMENT 'reels封面網址',
  `mm_aspect` varchar(50) COLLATE utf8mb4_general_ci DEFAULT NULL COMMENT '專案採用的輸出比例',
  `mm_status` tinyint NOT NULL DEFAULT '1' COMMENT '專案狀態(1=草稿、2=處理中、3=已完成、4=失敗)',
  `error_message` text COLLATE utf8mb4_general_ci COMMENT '失敗錯誤訊息',
  `created_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT '專案建立時間',
  `updated_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP COMMENT '專案最後更新時間',
  PRIMARY KEY (`mm_id`) USING BTREE,
  KEY `idx_mm_au_id` (`au_id`) USING BTREE,
  KEY `idx_mm_nt_id` (`nt_id`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci ROW_FORMAT=DYNAMIC COMMENT='商家影音表';
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `merchant_media_asset`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `merchant_media_asset` (
  `mma_id` int NOT NULL AUTO_INCREMENT COMMENT '素材流水號',
  `au_id` int NOT NULL COMMENT '哪個商家上傳',
  `mm_id` int NOT NULL COMMENT '商家影音表ID',
  `asset_url` text COLLATE utf8mb4_general_ci COMMENT '上傳素材的檔案網址',
  `asset_type` tinyint NOT NULL DEFAULT '1' COMMENT '素材類型(1=照片、2=影片)',
  `sort_order` int NOT NULL DEFAULT '0' COMMENT '這個素材在影片裡的排列順序',
  `created_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT '上傳時間',
  PRIMARY KEY (`mma_id`) USING BTREE,
  KEY `idx_mma_au_id` (`au_id`) USING BTREE,
  KEY `idx_mma_mm_id` (`mm_id`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci ROW_FORMAT=DYNAMIC COMMENT='商家上傳素材';
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `metro_line`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `metro_line` (
  `ml_id` int NOT NULL AUTO_INCREMENT COMMENT '捷運路線流水號',
  `rail_system` varchar(20) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NOT NULL COMMENT '捷運系統代碼（TDX RailSystem）：TRTC=臺北捷運、TMRT=臺中捷運、KRTC=高雄捷運、TYMC=桃園機場捷運、NTMC=新北捷運、KLRT=高雄輕軌、NTDLRT=淡海輕軌、NTALRT=安坑輕軌',
  `system_name` varchar(50) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NOT NULL COMMENT '系統名稱，例如「臺北捷運」',
  `line_no` varchar(20) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NOT NULL COMMENT '路線代碼（TDX LineNo），例如 BL',
  `line_name` varchar(100) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NOT NULL COMMENT '路線名稱，例如「板南線」',
  `line_color` varchar(10) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci DEFAULT NULL COMMENT '路線代表色，例如 #0a59ae',
  `city_name` varchar(100) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci DEFAULT NULL COMMENT '主要所在縣市',
  `is_active` tinyint NOT NULL DEFAULT '1' COMMENT '是否營運中(是=1、否=2)',
  `created_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT '建立時間',
  `updated_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP COMMENT '最後更新時間',
  PRIMARY KEY (`ml_id`) USING BTREE,
  UNIQUE KEY `uk_ml_system_line` (`rail_system`,`line_no`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci ROW_FORMAT=DYNAMIC COMMENT='捷運/輕軌路線';
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `metro_line_shape`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `metro_line_shape` (
  `ml_id` int NOT NULL COMMENT '捷運路線，對應 metro_line.ml_id',
  `geometry` mediumtext CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NOT NULL COMMENT '路線線形，WKT LINESTRING / MULTILINESTRING 格式（TDX Shape 原樣存入）',
  PRIMARY KEY (`ml_id`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci ROW_FORMAT=DYNAMIC COMMENT='捷運路線線形';
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `metro_line_station`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `metro_line_station` (
  `mls_id` int NOT NULL AUTO_INCREMENT COMMENT '路線車站流水號',
  `ml_id` int NOT NULL COMMENT '捷運路線，對應 metro_line.ml_id',
  `mst_id` int NOT NULL COMMENT '捷運車站，對應 metro_station.mst_id',
  `station_sequence` int NOT NULL COMMENT '站序（TDX StationOfLine.Sequence），有支線時支線車站接在主線後面',
  `cumulative_km` decimal(6,2) DEFAULT NULL COMMENT '距起站累計距離（公里）',
  PRIMARY KEY (`mls_id`) USING BTREE,
  UNIQUE KEY `uk_mls_line_seq` (`ml_id`,`station_sequence`) USING BTREE,
  KEY `idx_mls_mst_id` (`mst_id`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci ROW_FORMAT=DYNAMIC COMMENT='捷運路線經過的車站（顯示路線站序用）';
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `metro_station`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `metro_station` (
  `mst_id` int NOT NULL AUTO_INCREMENT COMMENT '捷運車站流水號',
  `station_uid` varchar(50) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NOT NULL COMMENT 'TDX 車站唯一代碼 StationUID，例如 TRTC-BL12',
  `rail_system` varchar(20) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NOT NULL COMMENT '捷運系統代碼，同 metro_line.rail_system',
  `station_code` varchar(20) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NOT NULL COMMENT '車站代碼（TDX StationID），例如 BL12',
  `station_name` varchar(100) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NOT NULL COMMENT '車站名稱，例如「台北車站」',
  `station_address` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci DEFAULT NULL COMMENT '車站地址',
  `city_name` varchar(100) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci DEFAULT NULL COMMENT '所在縣市',
  `town_name` varchar(100) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci DEFAULT NULL COMMENT '所在鄉鎮市區',
  `mst_latitude` decimal(10,7) NOT NULL COMMENT '緯度',
  `mst_longitude` decimal(10,7) NOT NULL COMMENT '經度',
  `created_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT '建立時間',
  `updated_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP COMMENT '最後更新時間',
  PRIMARY KEY (`mst_id`) USING BTREE,
  UNIQUE KEY `uk_mst_station_uid` (`station_uid`) USING BTREE,
  KEY `idx_mst_lat_lng` (`mst_latitude`,`mst_longitude`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci ROW_FORMAT=DYNAMIC COMMENT='捷運/輕軌車站（轉乘站在不同路線各有一筆，同站名且相近視為可轉乘）';
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `metro_station_link`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `metro_station_link` (
  `msl_id` int NOT NULL AUTO_INCREMENT COMMENT '相鄰車站流水號',
  `ml_id` int NOT NULL COMMENT '所屬路線，對應 metro_line.ml_id',
  `from_mst_id` int NOT NULL COMMENT '出發車站，對應 metro_station.mst_id',
  `to_mst_id` int NOT NULL COMMENT '下一站，對應 metro_station.mst_id',
  `run_seconds` int NOT NULL COMMENT '行駛秒數（TDX S2STravelTime.RunTime）',
  `stop_seconds` int NOT NULL DEFAULT '0' COMMENT '抵達下一站後的停靠秒數（TDX S2STravelTime.StopTime）',
  PRIMARY KEY (`msl_id`) USING BTREE,
  UNIQUE KEY `uk_msl_from_to` (`from_mst_id`,`to_mst_id`) USING BTREE,
  KEY `idx_msl_ml_id` (`ml_id`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci ROW_FORMAT=DYNAMIC COMMENT='相鄰車站之間的行駛時間（雙向各一筆，路線規劃用）';
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `narrative_tone`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `narrative_tone` (
  `nt_id` int NOT NULL AUTO_INCREMENT COMMENT '敘事語氣流水號',
  `nt_name` varchar(225) COLLATE utf8mb4_general_ci NOT NULL COMMENT '敘事語氣名稱',
  `nt_prompt` text COLLATE utf8mb4_general_ci COMMENT '語氣說明或提示字',
  `is_active` tinyint NOT NULL DEFAULT '1' COMMENT '是否啟用(是=1、否=2)',
  `created_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT '建立時間',
  `updated_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP COMMENT '最後更新時間',
  PRIMARY KEY (`nt_id`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci ROW_FORMAT=DYNAMIC COMMENT='敘事語氣';
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `npc`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `npc` (
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
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `qrcode_coupon`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `qrcode_coupon` (
  `qr_uid` varchar(255) COLLATE utf8mb4_general_ci NOT NULL COMMENT 'NFC貼紙UID，掃描後讀取到的卡片序號',
  `coupon_id` int NOT NULL COMMENT '對應的優惠券ID，可join回md_coupon取得商家資訊',
  `used_count` tinyint NOT NULL DEFAULT '0' COMMENT '此貼紙被兌換次數',
  PRIMARY KEY (`qr_uid`) USING BTREE,
  UNIQUE KEY `nfc_uid` (`qr_uid`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci ROW_FORMAT=DYNAMIC COMMENT='NFC貼紙-優惠券對照表（掃描入口，join md_coupon → ep_store 取得完整資訊）';
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `place`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `place` (
  `p_id` int NOT NULL AUTO_INCREMENT COMMENT '地點流水號',
  `region_id` varchar(255) COLLATE utf8mb4_general_ci DEFAULT NULL COMMENT '所屬地區 UUID',
  `p_name` varchar(255) COLLATE utf8mb4_general_ci NOT NULL COMMENT '地點/店家名稱',
  `p_category` varchar(255) COLLATE utf8mb4_general_ci DEFAULT NULL COMMENT '分類',
  `p_type` varchar(255) COLLATE utf8mb4_general_ci DEFAULT NULL COMMENT '細分類',
  `p_summary` text COLLATE utf8mb4_general_ci COMMENT '卡片簡短說明',
  `p_introduction` text COLLATE utf8mb4_general_ci COMMENT '詳細介紹',
  `p_address` varchar(255) COLLATE utf8mb4_general_ci DEFAULT NULL COMMENT '地址',
  `p_latitude` decimal(10,7) DEFAULT NULL COMMENT '緯度',
  `p_longitude` decimal(10,7) DEFAULT NULL COMMENT '經度',
  `p_open_time` varchar(255) COLLATE utf8mb4_general_ci DEFAULT NULL COMMENT '營業時間',
  `p_tel` varchar(255) COLLATE utf8mb4_general_ci DEFAULT NULL COMMENT '連絡電話',
  `p_website_url` text COLLATE utf8mb4_general_ci COMMENT '官方網站',
  `p_navigation_url` text COLLATE utf8mb4_general_ci COMMENT 'google map',
  `p_image` text COLLATE utf8mb4_general_ci COMMENT '地點封面圖片',
  `p_price_level` tinyint DEFAULT NULL COMMENT '消費等級：1＝平價、2＝中等、3＝高價',
  `p_rating` decimal(2,1) DEFAULT NULL COMMENT '評分',
  `p_count` int DEFAULT NULL COMMENT '評論數量',
  `p_business` varchar(255) COLLATE utf8mb4_general_ci DEFAULT NULL COMMENT '營業狀態',
  `created_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT '建立時間',
  `updated_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP COMMENT '最後更新時間',
  PRIMARY KEY (`p_id`) USING BTREE,
  KEY `idx_place_region_id` (`region_id`) USING BTREE,
  KEY `idx_place_category` (`p_category`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci ROW_FORMAT=DYNAMIC COMMENT='景點/店家主表';
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `place_bus_stop`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `place_bus_stop` (
  `place_id` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NOT NULL COMMENT '景點，Neo4j uuid（同 story_node.place_id）',
  `bs_id` int NOT NULL COMMENT '站牌，對應 bus_stop.bs_id',
  `walk_distance_m` int NOT NULL COMMENT '景點走到站牌的步行距離（公尺）',
  `walk_minutes` decimal(5,1) NOT NULL COMMENT '景點走到站牌的步行時間（分鐘）',
  PRIMARY KEY (`place_id`,`bs_id`) USING BTREE,
  KEY `idx_pbs_bs_id` (`bs_id`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci ROW_FORMAT=DYNAMIC COMMENT='景點與附近站牌對照（每個景點存步行 500 公尺內的站牌）';
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `place_type`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `place_type` (
  `place_type_id` int NOT NULL COMMENT '對照表ID',
  `place_id` varchar(255) COLLATE utf8mb4_general_ci NOT NULL COMMENT '景點UUID',
  `place_name` varchar(255) COLLATE utf8mb4_general_ci NOT NULL COMMENT '景點名稱',
  `place_category` varchar(255) COLLATE utf8mb4_general_ci NOT NULL COMMENT '景點分類，例如Attraction',
  `type_id` int NOT NULL COMMENT '對應的任務類型ID，對應md_type.type_id',
  PRIMARY KEY (`place_type_id`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci ROW_FORMAT=DYNAMIC COMMENT='景點任務類型對照表';
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `postcard`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `postcard` (
  `p_id` int NOT NULL AUTO_INCREMENT COMMENT '明信片流水號',
  `au_id` int NOT NULL COMMENT '使用者ID',
  `s_id` int DEFAULT NULL COMMENT '對應劇本流水號',
  `sn_id` int DEFAULT NULL COMMENT '取得完成節點ID',
  `p_name` varchar(255) COLLATE utf8mb4_general_ci NOT NULL COMMENT '明信片名稱',
  `p_summary` text COLLATE utf8mb4_general_ci COMMENT '明信片簡介',
  `p_imag_url` text COLLATE utf8mb4_general_ci COMMENT 'AI service網址',
  `is_night` tinyint NOT NULL DEFAULT '0' COMMENT '是否為夜間版明信片：1＝夜間、0＝白天',
  `created_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT '建立時間',
  `updated_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP COMMENT '最後更新時間',
  PRIMARY KEY (`p_id`) USING BTREE,
  KEY `idx_postcard_au_id` (`au_id`) USING BTREE,
  KEY `idx_postcard_s_id` (`s_id`) USING BTREE,
  KEY `idx_postcard_sn_id` (`sn_id`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci ROW_FORMAT=DYNAMIC COMMENT='明信片主表';
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `question_option`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `question_option` (
  `option_id` int NOT NULL AUTO_INCREMENT,
  `question_id` int NOT NULL COMMENT '所屬商家題庫題目ID',
  `option_context` varchar(255) COLLATE utf8mb4_general_ci NOT NULL COMMENT '選項文字內容',
  `option_url` varchar(255) COLLATE utf8mb4_general_ci DEFAULT NULL COMMENT '選項對應的圖片URL',
  `is_correct` tinyint NOT NULL DEFAULT '0' COMMENT '是否為正確答案，1=正確、0=錯誤',
  `option_key` varchar(10) COLLATE utf8mb4_general_ci DEFAULT NULL COMMENT '選項代號，例如A/B/C/D',
  PRIMARY KEY (`option_id`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci ROW_FORMAT=DYNAMIC COMMENT='商家題庫的選項表';
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `record_media`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `record_media` (
  `media_id` int NOT NULL COMMENT '媒體ID',
  `record_id` int NOT NULL COMMENT '所屬任務ID',
  `media_url` varchar(255) COLLATE utf8mb4_general_ci NOT NULL COMMENT 'vlog素材連結',
  `ep_id` varchar(50) COLLATE utf8mb4_general_ci DEFAULT NULL COMMENT '集數/素材編號',
  PRIMARY KEY (`media_id`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci ROW_FORMAT=DYNAMIC COMMENT='vlog素材表';
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `fog`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `fog` (
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
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `store`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `store` (
  `s_id` int NOT NULL AUTO_INCREMENT,
  `au_id` int NOT NULL COMMENT '使用者流水號，對應帳號表',
  `store_name` varchar(255) COLLATE utf8mb4_general_ci NOT NULL COMMENT '商家名稱',
  `store_dec` mediumtext COLLATE utf8mb4_general_ci COMMENT '商家介紹',
  `store_address` varchar(255) COLLATE utf8mb4_general_ci DEFAULT NULL COMMENT '商家地址(不包含縣市鄉鎮市區)',
  `store_city` varchar(255) COLLATE utf8mb4_general_ci DEFAULT NULL COMMENT '商家縣市',
  `store_town` varchar(255) COLLATE utf8mb4_general_ci DEFAULT NULL COMMENT '商家行政區\r\n',
  `store_uid` varchar(255) COLLATE utf8mb4_general_ci DEFAULT NULL COMMENT '對應Neo4j圖資料庫的地點node UUID',
  PRIMARY KEY (`s_id`) USING BTREE,
  UNIQUE KEY `au_id` (`au_id`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci ROW_FORMAT=DYNAMIC COMMENT='商家資訊表';
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `store_question`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `store_question` (
  `question_id` int NOT NULL AUTO_INCREMENT,
  `store_id` int NOT NULL COMMENT '商家ID，對應ep_store.s_id',
  `question_describe` text COLLATE utf8mb4_general_ci NOT NULL COMMENT '題目內容，商家可自行編輯，可被多個md_task重複引用',
  PRIMARY KEY (`question_id`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci ROW_FORMAT=DYNAMIC COMMENT='商家自建題庫（一份內容可被多個md_task引用）';
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `story`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `story` (
  `s_id` int NOT NULL AUTO_INCREMENT COMMENT '劇本流水號',
  `au_id` int NOT NULL COMMENT '使用者流水號',
  `npc_id` int DEFAULT NULL COMMENT '劇本主要npc id',
  `city_name` varchar(100) COLLATE utf8mb4_general_ci DEFAULT NULL COMMENT '使用者選擇城市名稱',
  `district_name` varchar(100) COLLATE utf8mb4_general_ci DEFAULT NULL COMMENT '使用者選擇鄉區名稱',
  `party_size` int DEFAULT NULL COMMENT '人數',
  `sd_transport` varchar(255) COLLATE utf8mb4_general_ci DEFAULT NULL COMMENT '交通方式偏好',
  `story_title` varchar(255) COLLATE utf8mb4_general_ci NOT NULL COMMENT '劇本標題',
  `story_prologue` text COLLATE utf8mb4_general_ci COMMENT '劇本前傳',
  `story_synopsis` text COLLATE utf8mb4_general_ci COMMENT '劇本簡介',
  `story_badge` text COLLATE utf8mb4_general_ci COMMENT '預期可以獲得勳章的清單',
  `story_postcards` int DEFAULT NULL COMMENT '預期可以獲得明信片數量',
  `is_active` tinyint NOT NULL DEFAULT '1' COMMENT '劇本是否啟用',
  `is_night_mode` tinyint NOT NULL DEFAULT '0' COMMENT '是否為夜間劇本：1＝夜間、0＝白天',
  `is_favorite` tinyint NOT NULL DEFAULT '0' COMMENT '是否為使用者喜愛的劇本：1＝喜愛、0＝否',
  `created_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT '劇本建立時間',
  `updated_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP COMMENT '劇本最後更新時間',
  PRIMARY KEY (`s_id`) USING BTREE,
  KEY `idx_story_au_id` (`au_id`) USING BTREE,
  KEY `idx_story_au_favorite` (`au_id`,`is_favorite`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci ROW_FORMAT=DYNAMIC COMMENT='劇本表';
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `story_node`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `story_node` (
  `sn_id` int NOT NULL AUTO_INCREMENT COMMENT '劇本節點流水號',
  `s_id` int NOT NULL COMMENT '劇本主表id',
  `npc_id` int DEFAULT NULL COMMENT '該劇本節點的npc id',
  `place_id` varchar(255) COLLATE utf8mb4_general_ci DEFAULT NULL COMMENT '對應景點，neo4j uuid',
  `sn_order` int NOT NULL DEFAULT '0' COMMENT '劇本節點順序',
  `sn_title` varchar(255) COLLATE utf8mb4_general_ci DEFAULT NULL COMMENT '劇本節點標題',
  `is_hidden` tinyint NOT NULL DEFAULT '2' COMMENT '是否為隱藏節點(是=1，否=2)',
  `is_night_only` tinyint NOT NULL DEFAULT '0' COMMENT '是否僅限夜間解鎖：1＝夜間、0＝白天',
  `is_active` tinyint NOT NULL DEFAULT '2' COMMENT '是否解鎖 (是=1否=2)',
  `last_time` datetime DEFAULT NULL COMMENT '該節點完成時間',
  `location_codename` varchar(255) COLLATE utf8mb4_general_ci DEFAULT NULL COMMENT '地點代號名稱，用在謎題呈現',
  `sn_opening_text` text COLLATE utf8mb4_general_ci COMMENT '節點固定開場劇情文字',
  `sn_success_text` text COLLATE utf8mb4_general_ci COMMENT '節點固定完成劇情文字',
  `sn_task_type` varchar(50) COLLATE utf8mb4_general_ci DEFAULT NULL COMMENT '節點任務類型',
  `created_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT '節點建立時間',
  `updated_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP COMMENT '節點最後更新時間',
  PRIMARY KEY (`sn_id`) USING BTREE,
  KEY `idx_sn_s_id` (`s_id`) USING BTREE,
  KEY `idx_sn_place_id` (`place_id`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci ROW_FORMAT=DYNAMIC COMMENT='劇本節點';
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `story_node_transit`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `story_node_transit` (
  `snt_id` int NOT NULL AUTO_INCREMENT COMMENT '節點交通流水號',
  `from_sn_id` int NOT NULL COMMENT '出發節點，對應 story_node.sn_id（從使用者目前位置出發的那段不存，開始遊玩時即時計算）',
  `to_sn_id` int NOT NULL COMMENT '抵達節點，對應 story_node.sn_id',
  `leg_order` int NOT NULL DEFAULT '1' COMMENT '同一段若要轉乘，依搭乘順序 1、2、3…',
  `board_brs_id` int NOT NULL COMMENT '上車站序，對應 bus_route_stop.brs_id（路線、方向、站牌都由此推導）',
  `alight_brs_id` int NOT NULL COMMENT '下車站序，對應 bus_route_stop.brs_id（須與上車站同一個行駛型態，且站序較大）',
  `est_ride_minutes` decimal(5,1) DEFAULT NULL COMMENT '產生劇本當下估算的乘車時間（分鐘，不含等車），為外部計算的快照值',
  `created_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT '建立時間',
  PRIMARY KEY (`snt_id`) USING BTREE,
  UNIQUE KEY `uk_snt_from_to_leg` (`from_sn_id`,`to_sn_id`,`leg_order`) USING BTREE,
  KEY `idx_snt_to_sn_id` (`to_sn_id`) USING BTREE,
  KEY `idx_snt_board_brs_id` (`board_brs_id`) USING BTREE,
  KEY `idx_snt_alight_brs_id` (`alight_brs_id`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci ROW_FORMAT=DYNAMIC COMMENT='劇本節點之間的公車交通方案（經過的站牌由 bus_route_stop 依上下車站序區間帶出）';
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `story_pair_member`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `story_pair_member` (
  `member_id` int NOT NULL AUTO_INCREMENT COMMENT '參與者流水號',
  `pair_id` int NOT NULL COMMENT '對應story_pair_session.pair_id',
  `au_id` int NOT NULL COMMENT '參與的玩家',
  `seat_no` int NOT NULL COMMENT '座位編號，決定此玩家在整段劇本中看哪一段線索，對應task_clue.seat_no',
  `is_host` tinyint NOT NULL DEFAULT '0' COMMENT '是否為建立此隊伍的人：1=是、0=否',
  `joined_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT '加入時間',
  PRIMARY KEY (`member_id`) USING BTREE,
  UNIQUE KEY `uk_spm_pair_au` (`pair_id`,`au_id`) USING BTREE,
  UNIQUE KEY `uk_spm_pair_seat` (`pair_id`,`seat_no`) USING BTREE,
  KEY `idx_spm_au_id` (`au_id`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci ROW_FORMAT=DYNAMIC COMMENT='劇本協作隊伍成員表（座位對應線索）';
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `story_pair_session`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `story_pair_session` (
  `pair_id` int NOT NULL AUTO_INCREMENT COMMENT '協作隊伍場次流水號',
  `s_id` int NOT NULL COMMENT '對應story.s_id，整段劇本的協作隊伍',
  `pair_code` varchar(10) COLLATE utf8mb4_general_ci NOT NULL COMMENT '配對編號，其他玩家輸入此編號加入整段劇本的協作隊伍',
  `pair_status` varchar(20) COLLATE utf8mb4_general_ci NOT NULL DEFAULT 'waiting' COMMENT '隊伍狀態：waiting候位中(可加入)、locked已鎖定(不開放新增名額，原座位空缺仍可補人)、completed整段劇本協作完成、expired逾期、cancelled取消',
  `active_code` varchar(10) COLLATE utf8mb4_general_ci GENERATED ALWAYS AS ((case when (`pair_status` = _utf8mb4'waiting') then `pair_code` else NULL end)) VIRTUAL COMMENT '僅供索引使用：pair_status=waiting時等於pair_code，其餘狀態為NULL，不需要應用層寫入',
  `created_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT '建立時間（產生配對編號的時間）',
  `locked_at` datetime DEFAULT NULL COMMENT '鎖定時間',
  `expires_at` datetime DEFAULT NULL COMMENT '配對編號有效期限，逾期後不可再被加入（由應用層邏輯判斷/更新狀態）',
  `completed_at` datetime DEFAULT NULL COMMENT '整段劇本協作完成時間',
  PRIMARY KEY (`pair_id`) USING BTREE,
  UNIQUE KEY `uk_sps_active_code` (`active_code`) USING BTREE,
  KEY `idx_sps_s_id` (`s_id`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci ROW_FORMAT=DYNAMIC COMMENT='劇本協作隊伍場次表（雙裝置以上配對編號）';
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `story_session`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `story_session` (
  `ss_id` int NOT NULL AUTO_INCREMENT COMMENT '遊玩記錄流水號',
  `au_id` int NOT NULL COMMENT '使用者id',
  `s_id` int NOT NULL COMMENT '劇本id',
  `sn_id` int DEFAULT NULL COMMENT '使用者目前所在或最後到達的節點id',
  `ss_current` int NOT NULL DEFAULT '0' COMMENT '現在使用者解鎖的節點順序',
  `ss_status` varchar(20) COLLATE utf8mb4_general_ci NOT NULL DEFAULT 'in_progress' COMMENT '遊玩狀態',
  `started_at` datetime DEFAULT NULL COMMENT '開始遊玩時間',
  `last_played_at` datetime DEFAULT NULL COMMENT '最後互動時間',
  `completed_at` datetime DEFAULT NULL COMMENT '完成整個劇本的時間',
  `updated_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP COMMENT '這筆紀錄最後更新時間',
  PRIMARY KEY (`ss_id`) USING BTREE,
  KEY `idx_ss_au_id` (`au_id`) USING BTREE,
  KEY `idx_ss_s_id` (`s_id`) USING BTREE,
  KEY `idx_ss_sn_id` (`sn_id`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci ROW_FORMAT=DYNAMIC COMMENT='使用者遊玩紀錄表';
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `story_tag`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `story_tag` (
  `s_id` int NOT NULL COMMENT '劇本流水號',
  `s_tag` varchar(100) COLLATE utf8mb4_general_ci NOT NULL COMMENT '偏好標籤',
  PRIMARY KEY (`s_id`,`s_tag`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci ROW_FORMAT=DYNAMIC COMMENT='劇本偏好標籤';
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `taiwan_tripper_route`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `taiwan_tripper_route` (
  `br_id` int NOT NULL COMMENT '對應 bus_route.br_id（一對一，直接當主鍵）',
  `ttr_theme` varchar(100) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci DEFAULT NULL COMMENT '路線主題，例如「山林湖光」',
  `ttr_introduction` text CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci COMMENT '路線介紹',
  `ttr_cover_image` text CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci COMMENT '封面圖片',
  `ttr_website_url` text CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci COMMENT '台灣好行官網路線頁',
  `sort_order` int NOT NULL DEFAULT '0' COMMENT '顯示排序',
  `created_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT '建立時間',
  `updated_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP COMMENT '最後更新時間',
  PRIMARY KEY (`br_id`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci ROW_FORMAT=DYNAMIC COMMENT='台灣好行路線專屬資訊（一般公車沒有的欄位放這裡，避免 bus_route 出現大量 NULL）';
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `task`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `task` (
  `task_id` int NOT NULL AUTO_INCREMENT COMMENT '任務ID',
  `story_id` int DEFAULT NULL COMMENT '所屬劇情ID；商家題庫任務審核前可能尚未掛入劇情，可為空',
  `node_id` int DEFAULT NULL COMMENT '所屬劇情節點ID；地點資訊由node帶出，可為空',
  `task_type` int NOT NULL COMMENT '關卡類型，對應md_type.type_id',
  `question_id` int DEFAULT NULL COMMENT '內容來自商家題庫時才有值，對應md_merchant_question.question_id',
  `task_describe` text COLLATE utf8mb4_general_ci COMMENT 'AI生成任務的題目描述文字；商家題庫任務此欄留空，文字由question_id取得',
  `correct_answer` varchar(255) COLLATE utf8mb4_general_ci DEFAULT NULL COMMENT '協作解謎型等任務用的正確答案文字',
  `task_hint` mediumtext COLLATE utf8mb4_general_ci COMMENT '任務提示',
  `pass` int NOT NULL DEFAULT '0' COMMENT '預設0->未通過任務，1->已通過任務',
  PRIMARY KEY (`task_id`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci ROW_FORMAT=DYNAMIC COMMENT='任務表（任務實例：某題出現在story/node的哪個位置）';
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `task_clue`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `task_clue` (
  `task_clue_id` int NOT NULL AUTO_INCREMENT COMMENT '線索流水號',
  `task_id` int NOT NULL COMMENT '對應task.task_id',
  `seat_no` int NOT NULL COMMENT '座位編號，對應story_pair_member.seat_no',
  `clue_text` text COLLATE utf8mb4_general_ci COMMENT '該座位玩家在此task看到的線索內容',
  PRIMARY KEY (`task_clue_id`) USING BTREE,
  UNIQUE KEY `uk_tc_task_seat` (`task_id`,`seat_no`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci ROW_FORMAT=DYNAMIC COMMENT='協作解謎任務的分段線索（依座位編號拆分）';
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `task_option`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `task_option` (
  `option_id` int NOT NULL COMMENT '選項ID',
  `task_id` int NOT NULL COMMENT '所屬任務ID',
  `option_context` varchar(255) COLLATE utf8mb4_general_ci NOT NULL COMMENT '選項文字內容',
  `option_url` varchar(255) COLLATE utf8mb4_general_ci DEFAULT NULL COMMENT '選項對應的圖片URL',
  `is_correct` tinyint NOT NULL DEFAULT '0' COMMENT '是否為正確答案，1=正確、0=錯誤',
  `option_key` varchar(10) COLLATE utf8mb4_general_ci DEFAULT NULL COMMENT '選項代號，例如A/B/C/D'
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci ROW_FORMAT=DYNAMIC COMMENT='AI生成任務的選項表';
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `travel_preference`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `travel_preference` (
  `tp_id` int NOT NULL AUTO_INCREMENT COMMENT '旅遊推薦流水號',
  `travel_uuid` varchar(255) COLLATE utf8mb4_general_ci DEFAULT NULL COMMENT '景點Neo4j uuid',
  `au_id` int NOT NULL COMMENT '使用者ID',
  `tp_transport` varchar(255) COLLATE utf8mb4_general_ci DEFAULT NULL COMMENT '偏好交通方式',
  `tp_rence` text COLLATE utf8mb4_general_ci COMMENT '使用者偏好',
  `tp_party` int DEFAULT NULL COMMENT '參與人數',
  `created_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT '創建時間',
  PRIMARY KEY (`tp_id`) USING BTREE,
  KEY `idx_tp_au_id` (`au_id`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci ROW_FORMAT=DYNAMIC COMMENT='旅遊推薦紀錄';
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `type`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `type` (
  `type_id` int NOT NULL COMMENT '任務類型ID',
  `type_name` varchar(255) COLLATE utf8mb4_general_ci NOT NULL COMMENT '任務類型名稱'
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci ROW_FORMAT=DYNAMIC COMMENT='任務類型表';
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `user_coupon`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `user_coupon` (
  `uc_id` int NOT NULL AUTO_INCREMENT,
  `au_id` int NOT NULL COMMENT '使用者流水號，對應帳號表（帳號表尚未匯入此檔案，先用au_id代表外鍵）',
  `coupon_id` int NOT NULL COMMENT '所屬優惠券ID',
  `obtained_at` timestamp NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT '領取/掃描取得時間',
  `is_used` tinyint NOT NULL DEFAULT '0' COMMENT '此使用者是否已核銷使用此優惠券，1=已使用',
  `used_at` datetime DEFAULT NULL COMMENT '核銷時間',
  PRIMARY KEY (`uc_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci ROW_FORMAT=DYNAMIC COMMENT='使用者持有的優惠券（誰擁有哪些優惠券，掃描NFC寫入一筆）';
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `user_task_record`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `user_task_record` (
  `record_id` int NOT NULL COMMENT '流水號',
  `au_id` int NOT NULL COMMENT '使用者流水號，對應帳號表',
  `task_id` int NOT NULL COMMENT '所屬任務ID，對應task.task_id',
  `pair_id` int DEFAULT NULL COMMENT '若此任務為協作解謎型，對應story_pair_session.pair_id；同一隊伍的成員紀錄會指向同一筆',
  `answer_content` text COLLATE utf8mb4_general_ci COMMENT '玩家提交的答案內容快照：選擇題存所選option_key或文字，文字題存輸入內容',
  `answer_media_url` varchar(255) COLLATE utf8mb4_general_ci DEFAULT NULL COMMENT '拍照/影片類任務的上傳素材連結，非此類任務留空',
  `is_correct` tinyint DEFAULT NULL COMMENT '答對與否快照：1=正確、0=錯誤，NULL=此任務類型無對錯判定（例如純打卡型任務）',
  `attempt_no` int NOT NULL DEFAULT '1' COMMENT '第幾次作答，允許重試的任務用來記錄嘗試次數',
  `answered_at` timestamp NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT '作答時間',
  KEY `idx_utr_pair_id` (`pair_id`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci ROW_FORMAT=DYNAMIC COMMENT='使用者作答紀錄（答案內容與對錯為作答當下的快照，不隨題目/選項之後被修改而變動）';
/*!40101 SET character_set_client = @saved_cs_client */;
/*!40103 SET TIME_ZONE=@OLD_TIME_ZONE */;

/*!40101 SET SQL_MODE=@OLD_SQL_MODE */;
/*!40014 SET FOREIGN_KEY_CHECKS=@OLD_FOREIGN_KEY_CHECKS */;
/*!40014 SET UNIQUE_CHECKS=@OLD_UNIQUE_CHECKS */;
/*!40101 SET CHARACTER_SET_CLIENT=@OLD_CHARACTER_SET_CLIENT */;
/*!40101 SET CHARACTER_SET_RESULTS=@OLD_CHARACTER_SET_RESULTS */;
/*!40101 SET COLLATION_CONNECTION=@OLD_COLLATION_CONNECTION */;
/*!40111 SET SQL_NOTES=@OLD_SQL_NOTES */;

