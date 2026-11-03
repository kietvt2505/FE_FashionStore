-- Website: automatic member discount only; preserve all older voucher invoices.
-- Run after Supabase-Web-Orders.sql. This script includes the pricing table if absent.
-- No changes to product prices, stock, customer balances or existing invoice amounts.
BEGIN;
SET LOCAL lock_timeout='5s';
SET LOCAL statement_timeout='60s';
DO $$ BEGIN
    IF to_regclass('public.thongtindonhang') IS NULL THEN
        RAISE EXCEPTION 'Apply Supabase-Web-Orders.sql first';
    END IF;
    PERFORM tongchitieu,diemtichluy,trangthai FROM public.khachhang LIMIT 0;
END $$;
CREATE TABLE IF NOT EXISTS public.uudaidonhang (
    sohd varchar(30) PRIMARY KEY REFERENCES public.thongtindonhang(sohd),
    subtotal numeric(18,2) NOT NULL CHECK(subtotal>0),
    discount numeric(18,2) NOT NULL CHECK(discount>=0 AND discount<subtotal),
    voucher varchar(20)
);
ALTER TABLE public.uudaidonhang
    ADD COLUMN IF NOT EXISTS member_rank varchar(20),
    ADD COLUMN IF NOT EXISTS member_rate smallint NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS member_points integer,
    ADD COLUMN IF NOT EXISTS member_discount numeric(18,2) NOT NULL DEFAULT 0;
ALTER TABLE public.uudaidonhang ADD COLUMN IF NOT EXISTS giamgiatruocdo numeric(18,2) NOT NULL DEFAULT 0
    CHECK(giamgiatruocdo>=0 AND giamgiatruocdo<=discount);
-- PostgreSQL numbers anonymous checks differently when a column check mentions subtotal.
-- Remove only the old voucher-only check, never the amount check sharing that name prefix.
DO $$ DECLARE old_check record; BEGIN
    FOR old_check IN SELECT conname FROM pg_constraint
      WHERE conrelid='public.uudaidonhang'::regclass AND contype='c'
        AND conname IN ('uudaidonhang_check','uudaidonhang_check1')
        AND pg_get_constraintdef(oid) LIKE '%voucher IS NOT NULL%'
        AND pg_get_constraintdef(oid) NOT LIKE '%member_discount%'
    LOOP EXECUTE format('ALTER TABLE public.uudaidonhang DROP CONSTRAINT %I',old_check.conname); END LOOP;
END $$;
ALTER TABLE public.uudaidonhang DROP CONSTRAINT IF EXISTS uudaidonhang_amount_check;
ALTER TABLE public.uudaidonhang ADD CONSTRAINT uudaidonhang_amount_check CHECK(discount>=0 AND discount<subtotal);
ALTER TABLE public.uudaidonhang DROP CONSTRAINT IF EXISTS uudaidonhang_discount_source_check;
ALTER TABLE public.uudaidonhang ADD CONSTRAINT uudaidonhang_discount_source_check
    CHECK(discount=0 OR voucher IS NOT NULL OR member_discount>0 OR giamgiatruocdo>0);
ALTER TABLE public.uudaidonhang DROP CONSTRAINT IF EXISTS uudaidonhang_membership_check;
ALTER TABLE public.uudaidonhang ADD CONSTRAINT uudaidonhang_membership_check CHECK(
    member_rate IN (0,5,10) AND member_discount>=0 AND member_discount<=discount
    AND member_discount=round(subtotal*member_rate/100,0)
    AND ((member_rank IS NULL AND member_rate=0 AND member_points IS NULL)
      OR (member_points IS NOT NULL AND member_points>=0 AND
        ((member_rank='Bạc' AND member_rate=0) OR (member_rank='Vàng' AND member_rate=5)
          OR (member_rank='Kim Cương' AND member_rate=10)))));
ALTER TABLE public.uudaidonhang ENABLE ROW LEVEL SECURITY;
REVOKE ALL ON public.uudaidonhang FROM PUBLIC;
DO $$ BEGIN
    IF EXISTS(SELECT 1 FROM pg_roles WHERE rolname='anon') THEN REVOKE ALL ON public.uudaidonhang FROM anon; END IF;
    IF EXISTS(SELECT 1 FROM pg_roles WHERE rolname='authenticated') THEN REVOKE ALL ON public.uudaidonhang FROM authenticated; END IF;
END $$;
COMMIT;

-- Read-only verification: four columns must be returned.
SELECT column_name,data_type FROM information_schema.columns
WHERE table_schema='public' AND table_name='uudaidonhang'
  AND column_name IN ('member_rank','member_rate','member_points','member_discount')
ORDER BY column_name;
