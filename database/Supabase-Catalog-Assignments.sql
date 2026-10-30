-- Phan loai 22 san pham thuc te da doc tu API ngay 27/10/2026.
-- Chay SAU Supabase-Catalog-Merchandising.sql trong SQL Editor Supabase.
-- UNISEX: hien o ca Nam va Nu. Vay/dam: Nu. Khong phan loai SP-023 (ten "123").
-- Khong thay doi gia ban, gia goc, ton kho hay du lieu don hang.
-- Bao toan audience da duoc nhom gan; chay lai khong ghi de quyet dinh cua nhom.
BEGIN;
SET LOCAL lock_timeout = '5s';
SET LOCAL statement_timeout = '60s';
DO $$ BEGIN
    IF to_regclass('webstore.catalog_product') IS NULL THEN
        RAISE EXCEPTION 'Apply Supabase-Catalog-Merchandising.sql first';
    END IF;
END $$;

CREATE TEMP TABLE catalog_assignment_seed (
    masp varchar(20) PRIMARY KEY,
    expected_name text NOT NULL,
    audience varchar(20) NOT NULL,
    kind varchar(40) NOT NULL
) ON COMMIT DROP;
INSERT INTO catalog_assignment_seed VALUES
    ('SP-001','Áo thun','UNISEX','ao-thun'),
    ('SP-002','Áo sơ mi','UNISEX','ao-so-mi'),
    ('SP-003','Áo polo','UNISEX','ao-polo'),
    ('SP-004','Áo khoác gió','UNISEX','ao-khoac'),
    ('SP-005','Áo hoodie','UNISEX','ao-hoodie'),
    ('SP-006','Áo vest','UNISEX','ao-vest'),
    ('SP-007','Quần dài kaki','UNISEX','quan-dai'),
    ('SP-008','Quần dài jean','UNISEX','quan-dai'),
    ('SP-009','Quần ngắn kaki','UNISEX','quan-ngan'),
    ('SP-010','Quần ngắn jeans','UNISEX','quan-ngan'),
    ('SP-011','Quần jogger','UNISEX','quan-jogger'),
    ('SP-012','Mắt kính mát','UNISEX','kinh-mat'),
    ('SP-013','Mắt kính râm','UNISEX','kinh-mat'),
    ('SP-014','Nón','UNISEX','mu'),
    ('SP-015','Dây nịch','UNISEX','day-nit'),
    ('SP-016','Vớ','UNISEX','vo'),
    ('SP-017','Váy ngắn','NU','vay-ngan'),
    ('SP-018','Váy dài','NU','vay-dai'),
    ('SP-019','Đầm ngắn','NU','dam-ngan'),
    ('SP-020','Đầm dài','NU','dam-dai'),
    ('SP-021','Áo','UNISEX','ao-khac'),
    ('SP-022','Áo hoodie có lông','UNISEX','ao-hoodie');

-- Stop if an ID has been reused or renamed since this list was reviewed.
DO $$ BEGIN
    IF EXISTS (
        SELECT 1 FROM catalog_assignment_seed a
        WHERE NOT EXISTS (SELECT 1 FROM public.sanpham p WHERE p.masp=a.masp)
           OR EXISTS (SELECT 1 FROM public.sanpham p WHERE p.masp=a.masp
               AND btrim(p.tensanpham) IS DISTINCT FROM a.expected_name)
    ) THEN
        RAISE EXCEPTION 'Product IDs/names have changed. Review the assignment list before applying.';
    END IF;
END $$;

ALTER TABLE webstore.catalog_product DROP CONSTRAINT IF EXISTS catalog_product_kind_check;
ALTER TABLE webstore.catalog_product ADD CONSTRAINT catalog_product_kind_check CHECK(kind IN (
    'ao-thun','ao-polo','ao-so-mi','ao-khoac','ao-hoodie','ao-vest','ao-khac',
    'quan-ngan','quan-dai','quan-jogger','vay-ngan','vay-dai','dam-ngan','dam-dai',
    'giay','dep','kinh-mat','mu','vo','day-nit','balo','tui-xach'));

INSERT INTO webstore.catalog_product(masp,audience,kind)
SELECT masp,audience,kind FROM catalog_assignment_seed
ON CONFLICT(masp) DO UPDATE SET audience=EXCLUDED.audience,kind=EXCLUDED.kind
WHERE webstore.catalog_product.audience IS NULL;
COMMIT;

-- Ket qua: 18 UNISEX + 4 NU. SP-023 van chua phan loai.
SELECT m.masp,min(p.tensanpham) AS ten_san_pham,m.audience,m.kind
FROM webstore.catalog_product m JOIN public.sanpham p USING(masp)
WHERE m.masp BETWEEN 'SP-001' AND 'SP-023'
GROUP BY m.masp,m.audience,m.kind ORDER BY m.masp;
