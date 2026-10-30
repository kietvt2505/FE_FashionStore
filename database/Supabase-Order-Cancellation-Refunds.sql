-- Apply AFTER Supabase-Customer-Order-Cancellation.sql, Supabase-Order-Cancellation-Policy.sql
-- and Supabase-Online-Payments.sql. No existing order is cancelled or refunded by this migration.
-- Refunds are MANUAL: these functions record requests/confirmed bank transfers only.
BEGIN;
SET LOCAL lock_timeout='5s';
SET LOCAL statement_timeout='60s';
DO $$ BEGIN
    IF to_regclass('webstore.order_cancellation') IS NULL OR to_regclass('webstore.gateway_receipt') IS NULL
       OR to_regclass('webstore.payment_confirmation') IS NULL THEN
        RAISE EXCEPTION 'Apply customer cancellation and online payment migrations first';
    END IF;
END $$;

ALTER TABLE webstore.order_cancellation ADD COLUMN IF NOT EXISTS stock_restored boolean NOT NULL DEFAULT false;
ALTER TABLE webstore.order_cancellation ADD COLUMN IF NOT EXISTS reversed_points integer NOT NULL DEFAULT 0;
ALTER TABLE webstore.order_cancellation ADD COLUMN IF NOT EXISTS reversed_spend numeric(18,2) NOT NULL DEFAULT 0;
CREATE TABLE IF NOT EXISTS webstore.order_refund (
    sohd varchar(30) PRIMARY KEY REFERENCES webstore.order_cancellation(sohd),
    makh varchar(20) NOT NULL REFERENCES public.khachhang(makh),
    amount numeric(18,2) NOT NULL CHECK(amount>0),
    status varchar(30) NOT NULL CHECK(status IN ('CHO_THONG_TIN','CHO_HOAN_TIEN','DA_HOAN_TIEN')),
    account_holder varchar(150), account_number varchar(34), bank_name varchar(100),
    createdat timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    submittedat timestamptz, refundedat timestamptz,
    employeeid varchar(20) REFERENCES public.nhanvien(manv), refund_reference varchar(100),
    CHECK(status='CHO_THONG_TIN' OR (length(btrim(account_holder))>=2 AND account_number ~ '^[0-9]{6,34}$'
        AND length(btrim(bank_name))>=2 AND submittedat IS NOT NULL
        AND account_holder IS NOT NULL AND account_number IS NOT NULL AND bank_name IS NOT NULL)),
    CHECK(status<>'DA_HOAN_TIEN' OR (refundedat IS NOT NULL AND employeeid IS NOT NULL AND refund_reference IS NOT NULL AND length(btrim(refund_reference))>=3))
);
CREATE UNIQUE INDEX IF NOT EXISTS order_refund_bank_reference ON webstore.order_refund(refund_reference) WHERE refund_reference IS NOT NULL;
ALTER TABLE webstore.order_refund ENABLE ROW LEVEL SECURITY;
REVOKE ALL ON webstore.order_refund FROM PUBLIC;

CREATE OR REPLACE FUNCTION webstore.can_cancel_order(p_customer varchar,p_order varchar)
RETURNS boolean LANGUAGE sql STABLE SECURITY INVOKER SET search_path=pg_catalog AS $$
    SELECT EXISTS (
        SELECT 1 FROM public.hoadon h JOIN webstore.order_snapshot s ON s.sohd=h.sohd AND s.makh=h.makh
        WHERE s.sohd=p_order AND s.makh=p_customer AND h.nguondon='WEB' AND h.ngaygiaothanhcong IS NULL
          AND btrim(h.trangthaigiaohang) IN ('MOI','CHO_XAC_NHAN','CHUA_GIAO','Chờ xác nhận','Đang chờ xác nhận',
              'Chờ xác nhận thanh toán','XAC_NHAN','Đã xác nhận','Vừa xác nhận đơn','Đơn đã được xác nhận')
          AND (
              (h.loaihoadon IN ('COD','WEB_COD','WEB_CHO_THANH_TOAN')
               AND h.trangthaithanhtoan='CHUA_THANH_TOAN' AND COALESCE(h.tienkhachdua,0)=0)
              OR
              (s.paymentmethod IN ('CHUYEN_KHOAN','PAYOS','VISA','VNPAY','ZALOPAY')
               AND h.loaihoadon IN ('GIAO_HANG','WEB_DA_THANH_TOAN','WEB_THANH_TOAN')
               AND h.trangthaithanhtoan='DA_THANH_TOAN' AND h.khachphaitra>0
               AND h.tienkhachdua=h.khachphaitra AND COALESCE(h.tienthua,0)=0
               AND (
                   EXISTS(SELECT 1 FROM webstore.payment_confirmation p WHERE p.sohd=h.sohd AND p.amount=h.khachphaitra)
                   OR EXISTS(SELECT 1 FROM webstore.gateway_receipt r JOIN webstore.gateway_attempt a ON a.id=r.attempt_id
                       WHERE a.sohd=h.sohd AND r.outcome='PAID' AND r.amount=h.khachphaitra)
               ))
          )
    )
$$;

CREATE OR REPLACE FUNCTION webstore.cancel_customer_order(
    p_customer varchar,p_order varchar,p_reason text,p_account_holder varchar,p_account_number varchar,p_bank_name varchar
) RETURNS text LANGUAGE plpgsql SECURITY INVOKER SET search_path=pg_catalog AS $$
DECLARE invoice record; method text; paid boolean; item record; points integer; reason_text text;
BEGIN
    SELECT paymentmethod INTO method FROM webstore.order_snapshot WHERE sohd=p_order AND makh=p_customer;
    IF NOT FOUND THEN RAISE EXCEPTION 'ORDER_NOT_FOUND' USING ERRCODE='P0002'; END IF;
    SELECT h.* INTO invoice FROM public.hoadon h JOIN webstore.order_snapshot s ON s.sohd=h.sohd AND s.makh=h.makh
        WHERE s.sohd=p_order AND s.makh=p_customer FOR UPDATE OF h;
    IF EXISTS(SELECT 1 FROM webstore.order_cancellation WHERE sohd=p_order AND makh=p_customer) THEN
        RETURN 'ALREADY_CANCELLED';
    END IF;
    IF invoice.sohd IS NULL OR NOT webstore.can_cancel_order(p_customer,p_order) THEN
        RAISE EXCEPTION 'ORDER_CANNOT_BE_CANCELLED';
    END IF;
    reason_text=btrim(COALESCE(p_reason,''));
    IF length(reason_text)>500 OR (method<>'COD' AND length(reason_text)<5) THEN
        RAISE EXCEPTION 'CANCELLATION_REASON_REQUIRED' USING ERRCODE='22023';
    END IF;
    IF reason_text='' THEN reason_text='Khách hàng hủy đơn từ website'; END IF;
    paid=invoice.trangthaithanhtoan='DA_THANH_TOAN';
    IF paid THEN
        IF p_account_holder IS NULL OR length(btrim(p_account_holder)) NOT BETWEEN 2 AND 150
           OR p_account_number IS NULL OR p_account_number !~ '^[0-9]{6,34}$'
           OR p_bank_name IS NULL OR length(btrim(p_bank_name)) NOT BETWEEN 2 AND 100 THEN
            RAISE EXCEPTION 'REFUND_ACCOUNT_REQUIRED' USING ERRCODE='22023';
        END IF;
        -- Restore only quantities previously settled by the website. Edited/missing invoice lines require staff review.
        IF NOT EXISTS(SELECT 1 FROM webstore.order_line_snapshot WHERE sohd=p_order) OR
           EXISTS((SELECT masku,soluong FROM public.chitiethoadon WHERE sohd=p_order
                   EXCEPT SELECT masku,quantity FROM webstore.order_line_snapshot WHERE sohd=p_order)
                  UNION ALL
                  (SELECT masku,quantity FROM webstore.order_line_snapshot WHERE sohd=p_order
                   EXCEPT SELECT masku,soluong FROM public.chitiethoadon WHERE sohd=p_order)) THEN
            RAISE EXCEPTION 'Invoice lines require staff review';
        END IF;
        FOR item IN SELECT masku,quantity FROM webstore.order_line_snapshot WHERE sohd=p_order ORDER BY masku LOOP
            UPDATE public.sanpham SET soluongtonkho=soluongtonkho+item.quantity,soluong=soluong+item.quantity
            WHERE masku=item.masku;
            IF NOT FOUND THEN RAISE EXCEPTION 'Cancelled SKU requires staff review'; END IF;
        END LOOP;
        points=floor(invoice.khachphaitra/10000)::integer;
        UPDATE public.khachhang SET tongchitieu=tongchitieu-invoice.khachphaitra,diemtichluy=diemtichluy-points,
            hangthe=CASE WHEN tongchitieu-invoice.khachphaitra>15000000 THEN 'Kim Cương'
                        WHEN tongchitieu-invoice.khachphaitra>5000000 THEN 'Vàng' ELSE 'Bạc' END
        WHERE makh=p_customer AND tongchitieu>=invoice.khachphaitra AND diemtichluy>=points;
        IF NOT FOUND THEN RAISE EXCEPTION 'Customer loyalty requires staff review'; END IF;
    END IF;
    INSERT INTO webstore.order_cancellation(sohd,makh,reason,stock_restored,reversed_points,reversed_spend)
    VALUES(p_order,p_customer,reason_text,paid,CASE WHEN paid THEN points ELSE 0 END,CASE WHEN paid THEN invoice.khachphaitra ELSE 0 END);
    IF paid THEN
        INSERT INTO webstore.order_refund(sohd,makh,amount,status,account_holder,account_number,bank_name,submittedat)
        VALUES(p_order,p_customer,invoice.khachphaitra,'CHO_HOAN_TIEN',btrim(p_account_holder),p_account_number,btrim(p_bank_name),CURRENT_TIMESTAMP);
    END IF;
    UPDATE public.hoadon SET loaihoadon='WEB_HUY',trangthaigiaohang='Đã hủy',tiencod=0 WHERE sohd=p_order;
    RETURN CASE WHEN paid THEN 'CANCELLED_REFUND_PENDING' ELSE 'CANCELLED' END;
END $$;

-- Old callers remain safe: online orders cannot bypass the reason/account form.
CREATE OR REPLACE FUNCTION webstore.cancel_customer_order(p_customer varchar,p_order varchar)
RETURNS text LANGUAGE sql SECURITY INVOKER SET search_path=pg_catalog AS $$
    SELECT webstore.cancel_customer_order(p_customer,p_order,NULL::text,NULL::varchar,NULL::varchar,NULL::varchar)
$$;

CREATE OR REPLACE FUNCTION webstore.submit_refund_account(
    p_customer varchar,p_order varchar,p_account_holder varchar,p_account_number varchar,p_bank_name varchar
) RETURNS text LANGUAGE plpgsql SECURITY INVOKER SET search_path=pg_catalog AS $$
DECLARE refund record;
BEGIN
    PERFORM 1 FROM public.hoadon h JOIN webstore.order_snapshot s ON s.sohd=h.sohd AND s.makh=h.makh
        WHERE s.sohd=p_order AND s.makh=p_customer AND h.loaihoadon='WEB_HUY' FOR UPDATE OF h;
    IF NOT FOUND THEN RAISE EXCEPTION 'ORDER_NOT_FOUND' USING ERRCODE='P0002'; END IF;
    SELECT * INTO refund FROM webstore.order_refund WHERE sohd=p_order AND makh=p_customer FOR UPDATE;
    IF NOT FOUND OR refund.status<>'CHO_THONG_TIN' THEN RAISE EXCEPTION 'Refund account cannot be changed'; END IF;
    IF p_account_holder IS NULL OR length(btrim(p_account_holder)) NOT BETWEEN 2 AND 150
       OR p_account_number IS NULL OR p_account_number !~ '^[0-9]{6,34}$'
       OR p_bank_name IS NULL OR length(btrim(p_bank_name)) NOT BETWEEN 2 AND 100 THEN
        RAISE EXCEPTION 'REFUND_ACCOUNT_REQUIRED' USING ERRCODE='22023';
    END IF;
    UPDATE webstore.order_refund SET status='CHO_HOAN_TIEN',account_holder=btrim(p_account_holder),
        account_number=p_account_number,bank_name=btrim(p_bank_name),submittedat=CURRENT_TIMESTAMP WHERE sohd=p_order;
    RETURN 'SUBMITTED';
END $$;

-- Partner calls this ONLY AFTER checking the actual outgoing bank transfer. This function does not send money.
CREATE OR REPLACE FUNCTION webstore.complete_customer_refund(p_order varchar,p_employee varchar,p_reference varchar)
RETURNS text LANGUAGE plpgsql SECURITY INVOKER SET search_path=pg_catalog AS $$
DECLARE refund record;
BEGIN
    PERFORM 1 FROM public.nhanvien WHERE manv=p_employee AND trangthai=true FOR SHARE;
    IF NOT FOUND OR p_reference IS NULL OR length(btrim(p_reference)) NOT BETWEEN 3 AND 100 THEN
        RAISE EXCEPTION 'Active employee and bank refund reference are required';
    END IF;
    PERFORM 1 FROM public.hoadon WHERE sohd=p_order AND loaihoadon='WEB_HUY' FOR UPDATE;
    IF NOT FOUND THEN RAISE EXCEPTION 'Cancelled invoice not found'; END IF;
    SELECT * INTO refund FROM webstore.order_refund WHERE sohd=p_order FOR UPDATE;
    IF NOT FOUND THEN RAISE EXCEPTION 'Refund request not found'; END IF;
    IF refund.status='DA_HOAN_TIEN' THEN
        IF refund.refund_reference=btrim(p_reference) THEN RETURN 'ALREADY_REFUNDED'; END IF;
        RAISE EXCEPTION 'Refund already has a different bank reference';
    END IF;
    IF refund.status<>'CHO_HOAN_TIEN' THEN RAISE EXCEPTION 'Customer refund account is required'; END IF;
    UPDATE webstore.order_refund SET status='DA_HOAN_TIEN',employeeid=p_employee,
        refund_reference=btrim(p_reference),refundedat=CURRENT_TIMESTAMP WHERE sohd=p_order;
    UPDATE public.hoadon SET trangthaithanhtoan='HOAN_TIEN' WHERE sohd=p_order;
    RETURN 'REFUNDED';
END $$;

-- A verified gateway payment can arrive after an unpaid order was cancelled.
-- Settlement already leaves it in REVIEW without deducting stock/crediting loyalty; collect refund details once.
CREATE OR REPLACE FUNCTION webstore.capture_cancelled_order_payment()
RETURNS trigger LANGUAGE plpgsql SECURITY INVOKER SET search_path=pg_catalog AS $$
BEGIN
    IF NEW.loaihoadon='WEB_HUY' AND NEW.trangthaithanhtoan='DA_THANH_TOAN'
       AND OLD.trangthaithanhtoan IS DISTINCT FROM 'DA_THANH_TOAN' AND NEW.tienkhachdua>0
       AND EXISTS(SELECT 1 FROM webstore.order_cancellation WHERE sohd=NEW.sohd AND makh=NEW.makh)
       AND EXISTS(SELECT 1 FROM webstore.gateway_receipt r JOIN webstore.gateway_attempt a ON a.id=r.attempt_id
           WHERE a.sohd=NEW.sohd AND r.outcome='REVIEW' AND r.amount=NEW.tienkhachdua) THEN
        INSERT INTO webstore.order_refund(sohd,makh,amount,status)
        VALUES(NEW.sohd,NEW.makh,NEW.tienkhachdua,'CHO_THONG_TIN') ON CONFLICT(sohd) DO NOTHING;
    END IF;
    RETURN NEW;
END $$;
DROP TRIGGER IF EXISTS capture_cancelled_order_payment ON public.hoadon;
CREATE TRIGGER capture_cancelled_order_payment AFTER UPDATE ON public.hoadon
FOR EACH ROW EXECUTE FUNCTION webstore.capture_cancelled_order_payment();

CREATE OR REPLACE VIEW webstore.customer_refund_queue WITH(security_invoker=true) AS
SELECT r.sohd,r.makh,c.reason,r.amount,r.status,r.account_holder,r.account_number,r.bank_name,
    r.createdat,r.submittedat,r.refundedat,r.employeeid,r.refund_reference,c.stock_restored,c.reversed_points,c.reversed_spend
FROM webstore.order_refund r JOIN webstore.order_cancellation c USING(sohd,makh);
REVOKE ALL ON webstore.customer_refund_queue FROM PUBLIC;
REVOKE ALL ON FUNCTION webstore.cancel_customer_order(varchar,varchar,text,varchar,varchar,varchar),
    webstore.submit_refund_account(varchar,varchar,varchar,varchar,varchar),
    webstore.complete_customer_refund(varchar,varchar,varchar),webstore.capture_cancelled_order_payment() FROM PUBLIC;
DO $$ DECLARE r text; BEGIN
    FOREACH r IN ARRAY ARRAY['anon','authenticated'] LOOP
        IF EXISTS(SELECT 1 FROM pg_roles WHERE rolname=r) THEN
            EXECUTE format('REVOKE ALL ON webstore.order_refund,webstore.customer_refund_queue FROM %I',r);
            EXECUTE format('REVOKE ALL ON FUNCTION webstore.cancel_customer_order(varchar,varchar,text,varchar,varchar,varchar),webstore.submit_refund_account(varchar,varchar,varchar,varchar,varchar),webstore.complete_customer_refund(varchar,varchar,varchar),webstore.capture_cancelled_order_payment() FROM %I',r);
        END IF;
    END LOOP;
END $$;
COMMIT;

SELECT to_regclass('webstore.order_refund') IS NOT NULL AS co_yeu_cau_hoan_tien,
       to_regprocedure('webstore.cancel_customer_order(character varying,character varying,text,character varying,character varying,character varying)') IS NOT NULL AS co_huy_kem_ly_do;
