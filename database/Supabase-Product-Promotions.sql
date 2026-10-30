-- Read-only bridge from WinForms promotions. Does not update public product prices.
-- Requires Supabase-Storefront-Enhancements.sql and partner khuyenmaisanpham(masku,makm,phantramgiam).
BEGIN;
SET LOCAL lock_timeout='5s';
CREATE OR REPLACE VIEW webstore.sku_price AS
WITH native_offer AS (
    SELECT d.masku,max(d.phantramgiam) AS percent
    FROM public.khuyenmaisanpham d JOIN public.khuyenmai k ON k.makm=d.makm
    WHERE k.trangthai=true AND d.phantramgiam>0 AND d.phantramgiam<100
      AND COALESCE(k.giatridonhangtoithieu,0)=0
      AND (CURRENT_TIMESTAMP AT TIME ZONE 'Asia/Ho_Chi_Minh')::date BETWEEN k.ngaybatdau::date AND k.ngayketthuc::date
    GROUP BY d.masku
), base AS (
    SELECT p.masku,p.dongia,n.percent,
        CASE WHEN old.percent>0 AND round(old.base_price*(100-old.percent)/100,0)=p.dongia
            THEN old.base_price ELSE p.dongia END AS base_price
    FROM public.sanpham p LEFT JOIN native_offer n ON n.masku=p.masku
    LEFT JOIN webstore.product_discount_price old ON old.masku=p.masku
)
SELECT masku,base_price,
    CASE WHEN percent IS NOT NULL THEN round(base_price*(100-percent)/100,0) ELSE dongia END AS sale_price,
    percent AS native_percent
FROM base;
REVOKE ALL ON webstore.sku_price FROM PUBLIC;
DO $$ BEGIN
    IF EXISTS(SELECT 1 FROM pg_roles WHERE rolname='anon') THEN REVOKE ALL ON webstore.sku_price FROM anon; END IF;
    IF EXISTS(SELECT 1 FROM pg_roles WHERE rolname='authenticated') THEN REVOKE ALL ON webstore.sku_price FROM authenticated; END IF;
END $$;
COMMIT;
-- Check: SELECT masku,base_price,sale_price,native_percent FROM webstore.sku_price WHERE native_percent IS NOT NULL;
