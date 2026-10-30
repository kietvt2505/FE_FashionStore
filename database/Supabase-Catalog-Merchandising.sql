-- Product classification and original list prices for website navigation/sale collections.
-- Uses the existing sanpham.dongia as the actual price paid by both Web and POS.
-- Never invent audience assignments or lower real product prices during this migration.
BEGIN;
SET LOCAL lock_timeout = '5s';
SET LOCAL statement_timeout = '60s';
CREATE SCHEMA IF NOT EXISTS webstore;
REVOKE ALL ON SCHEMA webstore FROM PUBLIC;
CREATE TABLE IF NOT EXISTS webstore.catalog_product (
    masp varchar(20) PRIMARY KEY,
    audience varchar(20) NULL CHECK(audience IN ('NAM','NU','TRE_EM','UNISEX')),
    kind varchar(40) NULL,
    regular_price numeric(18,2) NULL CHECK(regular_price>0)
);
-- Extend the same constraint on databases that already applied this migration.
ALTER TABLE webstore.catalog_product DROP CONSTRAINT IF EXISTS catalog_product_kind_check;
ALTER TABLE webstore.catalog_product ADD CONSTRAINT catalog_product_kind_check CHECK(kind IN (
    'ao-thun','ao-polo','ao-so-mi','ao-khoac','ao-hoodie','ao-vest','ao-khac',
    'quan-ngan','quan-dai','quan-jogger','vay-ngan','vay-dai','dam-ngan','dam-dai',
    'giay','dep','kinh-mat','mu','vo','day-nit','balo','tui-xach'));
-- Classify only explicit names. A generic "Quần" is not enough evidence to choose short/long.
INSERT INTO webstore.catalog_product(masp,kind)
SELECT masp, CASE
    WHEN lower(min(tensanpham)) LIKE '%áo thun%' OR lower(min(tensanpham)) LIKE '%áo phông%' THEN 'ao-thun'
    WHEN lower(min(tensanpham)) LIKE '%polo%' THEN 'ao-polo'
    WHEN lower(min(tensanpham)) LIKE '%sơ mi%' THEN 'ao-so-mi'
    WHEN lower(min(tensanpham)) LIKE '%áo khoác%' THEN 'ao-khoac'
    WHEN lower(min(tensanpham)) LIKE '%hoodie%' THEN 'ao-hoodie'
    WHEN lower(min(tensanpham)) LIKE '%áo vest%' THEN 'ao-vest'
    WHEN lower(min(tensanpham)) LIKE '%quần ngắn%' OR lower(min(tensanpham)) LIKE '%quần short%' THEN 'quan-ngan'
    WHEN lower(min(tensanpham)) LIKE '%quần dài%' THEN 'quan-dai'
    WHEN lower(min(tensanpham)) LIKE '%jogger%' THEN 'quan-jogger'
    WHEN lower(min(tensanpham)) LIKE '%váy ngắn%' THEN 'vay-ngan'
    WHEN lower(min(tensanpham)) LIKE '%váy dài%' THEN 'vay-dai'
    WHEN lower(min(tensanpham)) LIKE '%đầm ngắn%' THEN 'dam-ngan'
    WHEN lower(min(tensanpham)) LIKE '%đầm dài%' THEN 'dam-dai'
    WHEN lower(min(tensanpham)) LIKE '%giày%' THEN 'giay'
    WHEN lower(min(tensanpham)) LIKE '%dép%' THEN 'dep'
    WHEN lower(min(tensanpham)) LIKE '%kính mát%' OR lower(min(tensanpham)) LIKE '%kính râm%' THEN 'kinh-mat'
    WHEN lower(min(tensanpham)) LIKE '%mũ%' OR lower(min(tensanpham)) LIKE '%nón%' THEN 'mu'
    WHEN lower(min(tensanpham)) LIKE '%vớ%' OR lower(min(tensanpham)) LIKE '%tất %' THEN 'vo'
    WHEN lower(min(tensanpham)) LIKE '%dây nịt%' OR lower(min(tensanpham)) LIKE '%dây nịch%' THEN 'day-nit'
    WHEN lower(min(tensanpham)) LIKE '%balo%' OR lower(min(tensanpham)) LIKE '%ba lô%' THEN 'balo'
    WHEN lower(min(tensanpham)) LIKE '%túi xách%' THEN 'tui-xach'
    END
FROM public.sanpham GROUP BY masp ON CONFLICT(masp) DO NOTHING;
CREATE INDEX IF NOT EXISTS ix_catalog_audience_kind ON webstore.catalog_product(audience,kind);
ALTER TABLE webstore.catalog_product ENABLE ROW LEVEL SECURITY;
REVOKE ALL ON webstore.catalog_product FROM PUBLIC;
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname='anon') THEN REVOKE ALL ON webstore.catalog_product FROM anon; END IF;
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname='authenticated') THEN REVOKE ALL ON webstore.catalog_product FROM authenticated; END IF;
END $$;
COMMIT;

-- Read-only classification checklist. NULL means the team still needs to classify this product.
SELECT m.masp,min(p.tensanpham) AS name,m.audience,m.kind,m.regular_price,min(p.dongia) AS current_price
FROM webstore.catalog_product m JOIN public.sanpham p USING(masp)
GROUP BY m.masp,m.audience,m.kind,m.regular_price ORDER BY m.masp;
