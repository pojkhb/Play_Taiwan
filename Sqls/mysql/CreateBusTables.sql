-- =====================================================================
-- 公車 / 台灣好行 相關資料表（設計草案）
-- 資料來源：交通部 TDX 運輸資料流通服務（市區公車、公路客運、台灣好行）
--
-- 設計重點：
--   1. 路線、站牌、「路線經過哪些站（依方向、順序）」分開存，
--      同一個站牌可被多條路線共用，路線改線時只要重建 bus_route_stop。
--   2. 台灣好行跟一般公車共用同一組表，用 bus_route.route_type 區分。
--   3. 即時到站時間變動太快，不存資料庫，前端要顯示時由後端即時打 TDX。
--   4. story_node_transit 記錄「劇本節點 A → 節點 B 搭哪一路、哪站上下車」，
--      站牌清單由 bus_route_stop 依上下車站的順序區間帶出，不重複存。
-- =====================================================================

SET NAMES utf8mb4;

-- ----------------------------
-- 公車路線
-- ----------------------------
DROP TABLE IF EXISTS `bus_route`;
CREATE TABLE `bus_route`  (
  `br_id` int NOT NULL AUTO_INCREMENT COMMENT '公車路線流水號',
  `route_uid` varchar(50) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NOT NULL COMMENT 'TDX 路線唯一代碼 RouteUID，例如 TXG300',
  `route_type` tinyint NOT NULL DEFAULT 1 COMMENT '路線類型：1=市區公車、2=公路客運、3=台灣好行',
  `route_name` varchar(100) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NOT NULL COMMENT '路線名稱，例如「300」、「台灣好行 日月潭線」',
  `city_name` varchar(100) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NULL DEFAULT NULL COMMENT '所屬縣市，公路客運/台灣好行跨縣市時為 NULL',
  `operator_name` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NULL DEFAULT NULL COMMENT '營運業者',
  `departure_stop_name` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NULL DEFAULT NULL COMMENT '起站名稱',
  `destination_stop_name` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NULL DEFAULT NULL COMMENT '迄站名稱',
  `fare_desc` text CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NULL COMMENT '票價說明（台灣好行的一日券/套票也寫在這裡）',
  `route_map_url` text CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NULL COMMENT '官方路線圖網址',
  `headway_desc` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NULL DEFAULT NULL COMMENT '班距說明，例如「尖峰 10~15 分、離峰 20~30 分」',
  `is_active` tinyint NOT NULL DEFAULT 1 COMMENT '是否營運中：1=是、2=否（停駛/改線後保留紀錄）',
  `data_updated_at` datetime NULL DEFAULT NULL COMMENT 'TDX 資料版本時間，用來判斷要不要重新同步',
  `created_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT '建立時間',
  `updated_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP COMMENT '最後更新時間',
  PRIMARY KEY (`br_id`) USING BTREE,
  UNIQUE INDEX `uk_br_route_uid`(`route_uid` ASC) USING BTREE,
  INDEX `idx_br_type_city`(`route_type` ASC, `city_name` ASC) USING BTREE
) ENGINE = InnoDB CHARACTER SET = utf8mb4 COLLATE = utf8mb4_general_ci COMMENT = '公車路線主表（含台灣好行）' ROW_FORMAT = DYNAMIC;

-- ----------------------------
-- 公車站牌
-- ----------------------------
DROP TABLE IF EXISTS `bus_stop`;
CREATE TABLE `bus_stop`  (
  `bs_id` int NOT NULL AUTO_INCREMENT COMMENT '站牌流水號',
  `stop_uid` varchar(50) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NOT NULL COMMENT 'TDX 站牌唯一代碼 StopUID',
  `station_id` varchar(50) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NULL DEFAULT NULL COMMENT 'TDX 站位代碼 StationID，同一路口對向的兩個站牌會共用，用來合併顯示',
  `stop_name` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NOT NULL COMMENT '站牌名稱，例如「臺中車站」',
  `stop_address` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NULL DEFAULT NULL COMMENT '站牌地址',
  `city_name` varchar(100) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NULL DEFAULT NULL COMMENT '所在縣市',
  `bs_latitude` decimal(10, 7) NOT NULL COMMENT '緯度',
  `bs_longitude` decimal(10, 7) NOT NULL COMMENT '經度',
  `created_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT '建立時間',
  `updated_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP COMMENT '最後更新時間',
  PRIMARY KEY (`bs_id`) USING BTREE,
  UNIQUE INDEX `uk_bs_stop_uid`(`stop_uid` ASC) USING BTREE,
  INDEX `idx_bs_station_id`(`station_id` ASC) USING BTREE,
  INDEX `idx_bs_lat_lng`(`bs_latitude` ASC, `bs_longitude` ASC) USING BTREE
) ENGINE = InnoDB CHARACTER SET = utf8mb4 COLLATE = utf8mb4_general_ci COMMENT = '公車站牌主表（可被多條路線共用）' ROW_FORMAT = DYNAMIC;

-- ----------------------------
-- 路線經過的站牌（依方向、順序）
-- ----------------------------
DROP TABLE IF EXISTS `bus_route_stop`;
CREATE TABLE `bus_route_stop`  (
  `brs_id` int NOT NULL AUTO_INCREMENT COMMENT '路線站序流水號',
  `br_id` int NOT NULL COMMENT '公車路線，對應 bus_route.br_id',
  `bs_id` int NOT NULL COMMENT '站牌，對應 bus_stop.bs_id',
  `direction` tinyint NOT NULL DEFAULT 0 COMMENT '方向：0=去程、1=返程、2=迴圈',
  `stop_sequence` int NOT NULL COMMENT '站序，從 1 開始',
  PRIMARY KEY (`brs_id`) USING BTREE,
  UNIQUE INDEX `uk_brs_route_dir_seq`(`br_id` ASC, `direction` ASC, `stop_sequence` ASC) USING BTREE,
  INDEX `idx_brs_bs_id`(`bs_id` ASC) USING BTREE
) ENGINE = InnoDB CHARACTER SET = utf8mb4 COLLATE = utf8mb4_general_ci COMMENT = '公車路線站序（路線改線時整條重建）' ROW_FORMAT = DYNAMIC;

-- ----------------------------
-- 路線線形（地圖上畫公車路線用）
-- ----------------------------
DROP TABLE IF EXISTS `bus_route_shape`;
CREATE TABLE `bus_route_shape`  (
  `br_id` int NOT NULL COMMENT '公車路線，對應 bus_route.br_id',
  `direction` tinyint NOT NULL DEFAULT 0 COMMENT '方向：0=去程、1=返程、2=迴圈',
  `geometry` mediumtext CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NOT NULL COMMENT '路線線形，WKT LINESTRING 格式（TDX Shape 原樣存入）',
  PRIMARY KEY (`br_id`, `direction`) USING BTREE
) ENGINE = InnoDB CHARACTER SET = utf8mb4 COLLATE = utf8mb4_general_ci COMMENT = '公車路線線形' ROW_FORMAT = DYNAMIC;

-- ----------------------------
-- 班表（台灣好行、客運班次少，一定要有；市區公車多為固定班距，可只填 bus_route.headway_desc）
-- ----------------------------
DROP TABLE IF EXISTS `bus_timetable`;
CREATE TABLE `bus_timetable`  (
  `bt_id` int NOT NULL AUTO_INCREMENT COMMENT '班次流水號',
  `br_id` int NOT NULL COMMENT '公車路線，對應 bus_route.br_id',
  `direction` tinyint NOT NULL DEFAULT 0 COMMENT '方向：0=去程、1=返程、2=迴圈',
  `departure_time` time NOT NULL COMMENT '起站發車時間',
  `service_days` char(7) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NOT NULL DEFAULT '1111111' COMMENT '行駛日，週一到週日各一碼，1=有開、0=不開，例如 0000011 = 只有週末',
  `is_holiday_only` tinyint NOT NULL DEFAULT 2 COMMENT '是否只在國定假日行駛：1=是、2=否',
  PRIMARY KEY (`bt_id`) USING BTREE,
  INDEX `idx_bt_route_dir_time`(`br_id` ASC, `direction` ASC, `departure_time` ASC) USING BTREE
) ENGINE = InnoDB CHARACTER SET = utf8mb4 COLLATE = utf8mb4_general_ci COMMENT = '公車班表（起站發車時間）' ROW_FORMAT = DYNAMIC;

-- ----------------------------
-- 景點附近的站牌（預先算好，生成劇本時直接查）
-- ----------------------------
DROP TABLE IF EXISTS `place_bus_stop`;
CREATE TABLE `place_bus_stop`  (
  `place_id` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NOT NULL COMMENT '景點，Neo4j uuid（同 story_node.place_id）',
  `bs_id` int NOT NULL COMMENT '站牌，對應 bus_stop.bs_id',
  `walk_distance_m` int NOT NULL COMMENT '景點走到站牌的步行距離（公尺）',
  `walk_minutes` decimal(5, 1) NOT NULL COMMENT '景點走到站牌的步行時間（分鐘）',
  PRIMARY KEY (`place_id`, `bs_id`) USING BTREE,
  INDEX `idx_pbs_bs_id`(`bs_id` ASC) USING BTREE
) ENGINE = InnoDB CHARACTER SET = utf8mb4 COLLATE = utf8mb4_general_ci COMMENT = '景點與附近站牌對照（每個景點存步行 500 公尺內的站牌）' ROW_FORMAT = DYNAMIC;

-- ----------------------------
-- 劇本節點之間的公車交通方案
-- ----------------------------
DROP TABLE IF EXISTS `story_node_transit`;
CREATE TABLE `story_node_transit`  (
  `snt_id` int NOT NULL AUTO_INCREMENT COMMENT '節點交通流水號',
  `s_id` int NOT NULL COMMENT '劇本，對應 story.s_id',
  `from_sn_id` int NULL DEFAULT NULL COMMENT '出發節點，對應 story_node.sn_id；NULL = 從使用者起點出發',
  `to_sn_id` int NOT NULL COMMENT '抵達節點，對應 story_node.sn_id',
  `leg_order` int NOT NULL DEFAULT 1 COMMENT '同一段若要轉乘，依搭乘順序 1、2、3…',
  `br_id` int NOT NULL COMMENT '搭乘路線，對應 bus_route.br_id',
  `direction` tinyint NOT NULL DEFAULT 0 COMMENT '搭乘方向：0=去程、1=返程、2=迴圈',
  `board_bs_id` int NOT NULL COMMENT '上車站牌，對應 bus_stop.bs_id',
  `alight_bs_id` int NOT NULL COMMENT '下車站牌，對應 bus_stop.bs_id',
  `stop_count` int NOT NULL COMMENT '搭乘站數',
  `walk_to_board_m` int NULL DEFAULT NULL COMMENT '走到上車站牌的距離（公尺）',
  `walk_from_alight_m` int NULL DEFAULT NULL COMMENT '下車後走到景點的距離（公尺）',
  `est_ride_minutes` decimal(5, 1) NULL DEFAULT NULL COMMENT '預估乘車時間（分鐘，不含等車）',
  `created_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT '建立時間',
  PRIMARY KEY (`snt_id`) USING BTREE,
  INDEX `idx_snt_s_id`(`s_id` ASC) USING BTREE,
  INDEX `idx_snt_to_sn`(`to_sn_id` ASC, `leg_order` ASC) USING BTREE
) ENGINE = InnoDB CHARACTER SET = utf8mb4 COLLATE = utf8mb4_general_ci COMMENT = '劇本節點之間的公車交通方案（經過的站牌由 bus_route_stop 依上下車站序區間帶出）' ROW_FORMAT = DYNAMIC;
