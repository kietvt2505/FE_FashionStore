-- Apply after Supabase-Web-Orders.sql. Keeps checkout prices independently of later catalog/promotion changes.
-- Does not change existing invoices, promotions, inventory, payments or loyalty balances.
BEGIN;
SET LOCAL lock_timeout='5s';
SET LOCAL statement_timeout='60s';
DO $$ BEGIN
    IF to_regclass('webstore.order_snapshot') IS NULL THEN
        RAISE EXCEPTION 'Apply Supabase-Web-Orders.sql first';
    END IF;
    PERFORM makm,chietkhau,tongcong,khachphaitra FROM public.hoadon LIMIT 0;
    PERFORM makm,loaikm,giatrigiam,giatridonhangtoithieu,ngaybatdau,ngayketthuc,trangthai FROM public.khuyenmai LIMIT 0;
END $$;
CREATE TABLE IF NOT EXISTS webstore.order_pricing (
    sohd varchar(30) PRIMARY KEY REFERENCES webstore.order_snapshot(sohd),
    subtotal numeric(18,2) NOT NULL CHECK(subtotal>0),
    discount numeric(18,2) NOT NULL CHECK(discount>=0 AND discount<subtotal),
    voucher varchar(20),
    CHECK(discount=0 OR voucher IS NOT NULL)
);
ALTER TABLE webstore.order_pricing ENABLE ROW LEVEL SECURITY;
REVOKE ALL ON webstore.order_pricing FROM PUBLIC;
DO $$ BEGIN
    IF EXISTS(SELECT 1 FROM pg_roles WHERE rolname='anon') THEN REVOKE ALL ON webstore.order_pricing FROM anon; END IF;
    IF EXISTS(SELECT 1 FROM pg_roles WHERE rolname='authenticated') THEN REVOKE ALL ON webstore.order_pricing FROM authenticated; END IF;
END $$;
COMMIT;
