-- Локальная база аудита Nur Market Kassa
CREATE DATABASE IF NOT EXISTS nurmarket_kassa
  CHARACTER SET utf8mb4
  COLLATE utf8mb4_unicode_ci;

USE nurmarket_kassa;

CREATE TABLE IF NOT EXISTS audit_events (
  id BIGINT AUTO_INCREMENT PRIMARY KEY,
  created_at DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  category VARCHAR(64) NOT NULL,
  action VARCHAR(128) NOT NULL,
  user_id VARCHAR(128) NULL,
  device_name VARCHAR(128) NULL,
  details_json JSON NULL,
  INDEX idx_created_at (created_at),
  INDEX idx_category_action (category, action)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- Примеры запросов мониторинга:
-- SELECT * FROM audit_events ORDER BY created_at DESC LIMIT 100;
-- SELECT category, action, COUNT(*) cnt FROM audit_events GROUP BY category, action;
