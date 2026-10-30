-- Apply after Supabase-Customer-Order-Cancellation.sql.
-- Only the first two delivery milestones may be cancelled by customers.
-- Does not modify invoices, stock, payments or loyalty points.
BEGIN;
SET LOCAL lock_timeout='5s';
SET LOCAL statement_timeout='60s';
DO $$ BEGIN
    IF to_regprocedure('webstore.cancel_customer_order(character varying,character varying)') IS NULL THEN
        RAISE EXCEPTION 'Apply Supabase-Customer-Order-Cancellation.sql first';
    END IF;
END $$;

CREATE OR REPLACE FUNCTION webstore.can_cancel_order(p_customer varchar,p_order varchar)
RETURNS boolean LANGUAGE sql STABLE SECURITY INVOKER SET search_path=pg_catalog AS $$
    SELECT EXISTS (
        SELECT 1 FROM public.hoadon h JOIN webstore.order_snapshot s ON s.sohd=h.sohd AND s.makh=h.makh
        WHERE s.sohd=p_order AND s.makh=p_customer AND h.nguondon='WEB'
          AND h.loaihoadon IN ('COD','WEB_COD','WEB_CHO_THANH_TOAN')
          AND h.trangthaithanhtoan='CHUA_THANH_TOAN' AND COALESCE(h.tienkhachdua,0)=0
          AND h.ngaygiaothanhcong IS NULL
          AND btrim(h.trangthaigiaohang) IN ('MOI','CHO_XAC_NHAN','CHUA_GIAO','Chờ xác nhận','Chờ xác nhận thanh toán',
              'XAC_NHAN','Đã xác nhận','Vừa xác nhận đơn')
    )
$$;
COMMIT;

SELECT 'Chỉ cho phép hủy ở bước Chờ xác nhận hoặc Đã xác nhận' AS ket_qua;
