CREATE TABLE IF NOT EXISTS md_silhouette (
    silhouette_id VARCHAR(50) NOT NULL COMMENT '剪影識別碼',
    name VARCHAR(150) NOT NULL COMMENT '剪影名稱，例如：台北101剪影',
    image_url VARCHAR(1000) NOT NULL COMMENT '剪影圖片路徑，例如：/images/silhouettes/taipei-101.jpg',
    city VARCHAR(30) NULL COMMENT '所屬縣市',
    category VARCHAR(50) NULL COMMENT '分類，例如：地標、自然景觀、古蹟',
    is_active TINYINT(1) NOT NULL DEFAULT 1 COMMENT '是否顯示：1 顯示、0 隱藏',
    sort_order INT NOT NULL DEFAULT 0 COMMENT '排序，數字越小越前面',
    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT '建立時間',
    updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP
        ON UPDATE CURRENT_TIMESTAMP COMMENT '最後更新時間',
    PRIMARY KEY (silhouette_id),
    INDEX idx_md_silhouette_active_sort (is_active, sort_order)
) ENGINE = InnoDB
  DEFAULT CHARSET = utf8mb4
  COLLATE = utf8mb4_unicode_ci
  COMMENT = '景點剪影圖片資料';

INSERT INTO md_silhouette
(silhouette_id, name, image_url, city, category, is_active, sort_order)
VALUES
('SIL-001', '台北101剪影', '/images/silhouettes/taipei-101.jpg', '臺北市', '地標', 1, 1);
