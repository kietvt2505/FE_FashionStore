-- Apply after Supabase-Membership-Pricing.sql. Existing invoice amounts are preserved.
-- One order voucher is applied AFTER the automatic member discount.
BEGIN;
SET LOCAL lock_timeout='5s';
SET LOCAL statement_timeout='60s';
DO $$ BEGIN
    IF to_regclass('public.uudaidonhang') IS NULL THEN RAISE EXCEPTION 'Apply Supabase-Membership-Pricing.sql first'; END IF;
    PERFORM member_discount FROM public.uudaidonhang LIMIT 0;
    PERFORM makm,loaikm,giatrigiam,giatridonhangtoithieu,ngaybatdau,ngayketthuc,trangthai FROM public.khuyenmai LIMIT 0;
END $$;
ALTER TABLE public.uudaidonhang ADD COLUMN IF NOT EXISTS voucher_discount numeric(18,2)
    GENERATED ALWAYS AS (discount-member_discount-giamgiatruocdo) STORED;

-- Do not apply a SKU promotion twice, once through sale prices and once as an order voucher.
CREATE OR REPLACE FUNCTION public.is_product_promotion(p_code varchar)
RETURNS boolean LANGUAGE plpgsql STABLE SECURITY INVOKER SET search_path=pg_catalog AS $$
DECLARE linked boolean;
BEGIN
    IF to_regclass('public.khuyenmaisanpham') IS NULL THEN RETURN false; END IF;
    EXECUTE 'SELECT EXISTS(SELECT 1 FROM public.khuyenmaisanpham WHERE makm=$1)' INTO linked USING p_code;
    RETURN linked;
END $$;
CREATE OR REPLACE VIEW public.makhuyenmaihople WITH(security_invoker=true) AS
SELECT makm,tenchuongtrinh,
    CASE WHEN lower(btrim(loaikm)) IN ('phần trăm','phan_tram','phan tram','%') THEN 'PHAN_TRAM'
         WHEN lower(btrim(loaikm)) IN ('số tiền','so_tien','so tien','vnd') THEN 'SO_TIEN' ELSE NULL END AS kind,
    giatrigiam AS value,COALESCE(giatridonhangtoithieu,0) AS minimum,
    ngaybatdau::date AS starts_on,ngayketthuc::date AS ends_on,trangthai AS active,
    public.is_product_promotion(makm) AS product_promotion
FROM public.khuyenmai;
REVOKE ALL ON public.makhuyenmaihople FROM PUBLIC;
REVOKE ALL ON FUNCTION public.is_product_promotion(varchar) FROM PUBLIC;
DO $$ DECLARE r text; BEGIN
    FOREACH r IN ARRAY ARRAY['anon','authenticated'] LOOP
        IF EXISTS(SELECT 1 FROM pg_roles WHERE rolname=r) THEN
            EXECUTE format('REVOKE ALL ON public.makhuyenmaihople FROM %I',r);
            EXECUTE format('REVOKE ALL ON FUNCTION public.is_product_promotion(varchar) FROM %I',r);
        END IF;
    END LOOP;
END $$;
COMMIT;
SELECT to_regclass('public.makhuyenmaihople') IS NOT NULL AS co_ma_giam_gia,
       EXISTS(SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='uudaidonhang'
           AND column_name='voucher_discount' AND is_generated='ALWAYS') AS co_luu_giam_theo_ma;
