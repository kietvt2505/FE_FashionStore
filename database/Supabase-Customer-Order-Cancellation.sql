-- Apply after Supabase-Web-Orders.sql and Supabase-Web-Payments.sql.
-- Retains cancelled invoices and snapshots. Never changes stock, loyalty or refunds.
BEGIN;
SET LOCAL lock_timeout='5s';
SET LOCAL statement_timeout='60s';
CREATE TABLE IF NOT EXISTS webstore.order_cancellation (
    sohd varchar(30) PRIMARY KEY REFERENCES webstore.order_snapshot(sohd),
    makh varchar(20) NOT NULL REFERENCES public.khachhang(makh),
    cancelledat timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    reason varchar(500) NOT NULL DEFAULT 'Khách hàng hủy đơn từ website'
);
ALTER TABLE webstore.order_cancellation ENABLE ROW LEVEL SECURITY;
REVOKE ALL ON webstore.order_cancellation FROM PUBLIC;

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

CREATE OR REPLACE FUNCTION webstore.cancel_customer_order(p_customer varchar,p_order varchar)
RETURNS text LANGUAGE plpgsql SECURITY INVOKER SET search_path=pg_catalog AS $$
DECLARE invoice record;
BEGIN
    PERFORM 1 FROM webstore.order_snapshot WHERE sohd=p_order AND makh=p_customer;
    IF NOT FOUND THEN RAISE EXCEPTION 'ORDER_NOT_FOUND' USING ERRCODE='P0002'; END IF;
    -- Same invoice lock as bank confirmation and gateway settlement.
    SELECT h.* INTO invoice FROM public.hoadon h JOIN webstore.order_snapshot s ON s.sohd=h.sohd AND s.makh=h.makh
        WHERE s.sohd=p_order AND s.makh=p_customer FOR UPDATE OF h;
    IF EXISTS(SELECT 1 FROM webstore.order_cancellation WHERE sohd=p_order AND makh=p_customer) THEN
        RETURN 'ALREADY_CANCELLED';
    END IF;
    IF invoice.sohd IS NULL OR NOT webstore.can_cancel_order(p_customer,p_order) THEN
        RAISE EXCEPTION 'ORDER_CANNOT_BE_CANCELLED';
    END IF;
    INSERT INTO webstore.order_cancellation(sohd,makh) VALUES(p_order,p_customer);
    -- WEB_HUY is excluded from the partner's active COD/delivery queues; retain invoice lines for audit.
    UPDATE public.hoadon SET loaihoadon='WEB_HUY',trangthaigiaohang='Đã hủy',tiencod=0 WHERE sohd=p_order;
    RETURN 'CANCELLED';
END $$;

-- The old WinForms code reads status before its UPDATE/DELETE. Reject stale operations after a website cancellation.
CREATE OR REPLACE FUNCTION webstore.guard_cancelled_web_order()
RETURNS trigger LANGUAGE plpgsql SECURITY INVOKER SET search_path=pg_catalog AS $$
BEGIN
    IF OLD.loaihoadon='WEB_HUY' THEN
        IF TG_OP='DELETE' THEN RAISE EXCEPTION 'Website order was cancelled; retain its invoice'; END IF;
        IF NEW.loaihoadon IS DISTINCT FROM 'WEB_HUY' OR
           (NEW.trangthaigiaohang IS DISTINCT FROM OLD.trangthaigiaohang AND NOT (
                NEW.trangthaigiaohang IS NOT DISTINCT FROM 'Đã nhận tiền - cần cửa hàng xử lý' AND NEW.trangthaithanhtoan IS NOT DISTINCT FROM 'DA_THANH_TOAN')) THEN
            RAISE EXCEPTION 'Website order was cancelled; refresh its status';
        END IF;
    END IF;
    IF TG_OP='DELETE' THEN RETURN OLD; END IF;
    RETURN NEW;
END $$;
DROP TRIGGER IF EXISTS guard_cancelled_web_order ON public.hoadon;
CREATE TRIGGER guard_cancelled_web_order BEFORE UPDATE OR DELETE ON public.hoadon
FOR EACH ROW EXECUTE FUNCTION webstore.guard_cancelled_web_order();

REVOKE ALL ON FUNCTION webstore.can_cancel_order(varchar,varchar),webstore.cancel_customer_order(varchar,varchar),
    webstore.guard_cancelled_web_order() FROM PUBLIC;
DO $$ DECLARE r text; BEGIN
    FOREACH r IN ARRAY ARRAY['anon','authenticated'] LOOP
        IF EXISTS(SELECT 1 FROM pg_roles WHERE rolname=r) THEN
            EXECUTE format('REVOKE ALL ON webstore.order_cancellation FROM %I',r);
            EXECUTE format('REVOKE ALL ON FUNCTION webstore.can_cancel_order(varchar,varchar),webstore.cancel_customer_order(varchar,varchar),webstore.guard_cancelled_web_order() FROM %I',r);
        END IF;
    END LOOP;
END $$;
COMMIT;

SELECT to_regprocedure('webstore.cancel_customer_order(character varying,character varying)') IS NOT NULL AS co_ham_huy,
       EXISTS(SELECT 1 FROM pg_trigger WHERE tgrelid='public.hoadon'::regclass AND tgname='guard_cancelled_web_order' AND NOT tgisinternal) AS co_bao_ve_don_huy;
