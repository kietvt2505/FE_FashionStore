-- Run after Supabase-Customer-Features.sql. Reviews from verified purchases
-- are public immediately. Existing hidden reviews remain hidden.
BEGIN;
SET LOCAL lock_timeout='5s';
SET LOCAL statement_timeout='60s';

ALTER TABLE public.product_review ALTER COLUMN status SET DEFAULT 'HIEN_THI';

WITH published AS (
    UPDATE public.product_review r SET status='HIEN_THI'
    WHERE r.status='CHO_DUYET' AND EXISTS (
        SELECT 1 FROM public.hoadon h
        JOIN public.order_snapshot s ON s.sohd=h.sohd AND s.makh=h.makh
        JOIN public.order_line_snapshot l ON l.sohd=h.sohd
        JOIN public.sanpham p ON p.masku=l.masku
        WHERE h.sohd=r.sohd AND h.makh=r.makh AND p.masp=r.masp
            AND h.trangthaigiaohang='Hàng giao thành công'
            AND h.trangthaithanhtoan='DA_THANH_TOAN'
            AND h.loaihoadon IS DISTINCT FROM 'WEB_HUY'
    ) RETURNING id
)
SELECT count(*) AS so_danh_gia_vua_hien_thi FROM published;

COMMIT;

SELECT EXISTS (
    SELECT 1 FROM pg_attrdef d
    JOIN pg_attribute a ON a.attrelid=d.adrelid AND a.attnum=d.adnum
    WHERE d.adrelid='public.product_review'::regclass AND a.attname='status'
        AND pg_get_expr(d.adbin,d.adrelid) LIKE '%HIEN_THI%'
) AS tu_dong_hien_thi;
