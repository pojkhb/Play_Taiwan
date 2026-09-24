-- 捷運 / 輕軌資料表（資料來源：交通部 TDX /v2/Rail/Metro/*，由後端 MetroSync 自動同步）
-- 路線規劃用 metro_station_link 建成車站圖（相鄰車站 + 同名車站轉乘），支線、轉乘都能算。

CREATE TABLE IF NOT EXISTS `metro_line` (
  `ml_id` int NOT NULL AUTO_INCREMENT COMMENT '捷運路線流水號',
  `rail_system` varchar(20) COLLATE utf8mb4_general_ci NOT NULL COMMENT '捷運系統代碼（TDX RailSystem）：TRTC=臺北捷運、TMRT=臺中捷運、KRTC=高雄捷運、TYMC=桃園機場捷運、NTMC=新北捷運、KLRT=高雄輕軌、NTDLRT=淡海輕軌、NTALRT=安坑輕軌',
  `system_name` varchar(50) COLLATE utf8mb4_general_ci NOT NULL COMMENT '系統名稱，例如「臺北捷運」',
  `line_no` varchar(20) COLLATE utf8mb4_general_ci NOT NULL COMMENT '路線代碼（TDX LineNo），例如 BL',
  `line_name` varchar(100) COLLATE utf8mb4_general_ci NOT NULL COMMENT '路線名稱，例如「板南線」',
  `line_color` varchar(10) COLLATE utf8mb4_general_ci DEFAULT NULL COMMENT '路線代表色，例如 #0a59ae',
  `city_name` varchar(100) COLLATE utf8mb4_general_ci DEFAULT NULL COMMENT '主要所在縣市',
  `is_active` tinyint NOT NULL DEFAULT '1' COMMENT '是否營運中(是=1、否=2)',
  `created_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT '建立時間',
  `updated_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP COMMENT '最後更新時間',
  PRIMARY KEY (`ml_id`) USING BTREE,
  UNIQUE KEY `uk_ml_system_line` (`rail_system`, `line_no`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci ROW_FORMAT=DYNAMIC COMMENT='捷運/輕軌路線';


CREATE TABLE IF NOT EXISTS `metro_station` (
  `mst_id` int NOT NULL AUTO_INCREMENT COMMENT '捷運車站流水號',
  `station_uid` varchar(50) COLLATE utf8mb4_general_ci NOT NULL COMMENT 'TDX 車站唯一代碼 StationUID，例如 TRTC-BL12',
  `rail_system` varchar(20) COLLATE utf8mb4_general_ci NOT NULL COMMENT '捷運系統代碼，同 metro_line.rail_system',
  `station_code` varchar(20) COLLATE utf8mb4_general_ci NOT NULL COMMENT '車站代碼（TDX StationID），例如 BL12',
  `station_name` varchar(100) COLLATE utf8mb4_general_ci NOT NULL COMMENT '車站名稱，例如「台北車站」',
  `station_address` varchar(255) COLLATE utf8mb4_general_ci DEFAULT NULL COMMENT '車站地址',
  `city_name` varchar(100) COLLATE utf8mb4_general_ci DEFAULT NULL COMMENT '所在縣市',
  `town_name` varchar(100) COLLATE utf8mb4_general_ci DEFAULT NULL COMMENT '所在鄉鎮市區',
  `mst_latitude` decimal(10,7) NOT NULL COMMENT '緯度',
  `mst_longitude` decimal(10,7) NOT NULL COMMENT '經度',
  `created_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT '建立時間',
  `updated_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP COMMENT '最後更新時間',
  PRIMARY KEY (`mst_id`) USING BTREE,
  UNIQUE KEY `uk_mst_station_uid` (`station_uid`) USING BTREE,
  KEY `idx_mst_lat_lng` (`mst_latitude`, `mst_longitude`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci ROW_FORMAT=DYNAMIC COMMENT='捷運/輕軌車站（轉乘站在不同路線各有一筆，同站名且相近視為可轉乘）';


CREATE TABLE IF NOT EXISTS `metro_line_station` (
  `mls_id` int NOT NULL AUTO_INCREMENT COMMENT '路線車站流水號',
  `ml_id` int NOT NULL COMMENT '捷運路線，對應 metro_line.ml_id',
  `mst_id` int NOT NULL COMMENT '捷運車站，對應 metro_station.mst_id',
  `station_sequence` int NOT NULL COMMENT '站序（TDX StationOfLine.Sequence），有支線時支線車站接在主線後面',
  `cumulative_km` decimal(6,2) DEFAULT NULL COMMENT '距起站累計距離（公里）',
  PRIMARY KEY (`mls_id`) USING BTREE,
  UNIQUE KEY `uk_mls_line_seq` (`ml_id`, `station_sequence`) USING BTREE,
  KEY `idx_mls_mst_id` (`mst_id`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci ROW_FORMAT=DYNAMIC COMMENT='捷運路線經過的車站（顯示路線站序用）';


CREATE TABLE IF NOT EXISTS `metro_station_link` (
  `msl_id` int NOT NULL AUTO_INCREMENT COMMENT '相鄰車站流水號',
  `ml_id` int NOT NULL COMMENT '所屬路線，對應 metro_line.ml_id',
  `from_mst_id` int NOT NULL COMMENT '出發車站，對應 metro_station.mst_id',
  `to_mst_id` int NOT NULL COMMENT '下一站，對應 metro_station.mst_id',
  `run_seconds` int NOT NULL COMMENT '行駛秒數（TDX S2STravelTime.RunTime）',
  `stop_seconds` int NOT NULL DEFAULT '0' COMMENT '抵達下一站後的停靠秒數（TDX S2STravelTime.StopTime）',
  PRIMARY KEY (`msl_id`) USING BTREE,
  UNIQUE KEY `uk_msl_from_to` (`from_mst_id`, `to_mst_id`) USING BTREE,
  KEY `idx_msl_ml_id` (`ml_id`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci ROW_FORMAT=DYNAMIC COMMENT='相鄰車站之間的行駛時間（雙向各一筆，路線規劃用）';


CREATE TABLE IF NOT EXISTS `metro_line_shape` (
  `ml_id` int NOT NULL COMMENT '捷運路線，對應 metro_line.ml_id',
  `geometry` mediumtext COLLATE utf8mb4_general_ci NOT NULL COMMENT '路線線形，WKT LINESTRING / MULTILINESTRING 格式（TDX Shape 原樣存入）',
  PRIMARY KEY (`ml_id`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci ROW_FORMAT=DYNAMIC COMMENT='捷運路線線形';
