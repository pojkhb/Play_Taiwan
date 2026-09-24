-- 商家 VLOG 的敘事語氣選項（GET /api/MerchantVlog/Tones）
-- nt_prompt 會併進送給 AI 的 promo_focus，當作旁白語氣的提示。
INSERT INTO narrative_tone (nt_name, nt_prompt, is_active) VALUES
('幽默詼諧', '輕鬆有梗、節奏明快，可以用一點自嘲或誇飾，讓人會心一笑', 1),
('質感專業', '沉穩精準、用詞講究，強調選材、工法與店家堅持，像生活風格雜誌的介紹', 1),
('溫情走心', '溫暖真誠、有畫面感，從人與人的故事切入，讓人想起某個值得回去的地方', 1);
