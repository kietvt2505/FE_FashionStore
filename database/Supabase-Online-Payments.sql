-- Apply after Supabase-Web-Payments.sql. Does not mark any existing invoice paid.
BEGIN;
SET LOCAL lock_timeout='5s';
CREATE TABLE IF NOT EXISTS webstore.gateway_attempt (
    id varchar(40) PRIMARY KEY,
    sohd varchar(30) NOT NULL REFERENCES webstore.order_snapshot(sohd),
    provider varchar(10) NOT NULL CHECK(provider IN ('VNPAY','ZALOPAY')),
    method varchar(10) NOT NULL CHECK(method IN ('VNPAY','VISA','ZALOPAY')),
    merchant varchar(50) NOT NULL,
    amount numeric(18,2) NOT NULL CHECK(amount>0),
    status varchar(10) NOT NULL DEFAULT 'PENDING' CHECK(status IN ('PENDING','PAID','FAILED','REVIEW')),
    payment_url text,
    created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    expires_at timestamptz NOT NULL,
    UNIQUE(sohd,id)
);
CREATE INDEX IF NOT EXISTS gateway_attempt_order ON webstore.gateway_attempt(sohd,created_at DESC);
CREATE TABLE IF NOT EXISTS webstore.gateway_receipt (
    provider varchar(10) NOT NULL,
    transaction_id varchar(100) NOT NULL,
    attempt_id varchar(40) NOT NULL REFERENCES webstore.gateway_attempt(id),
    amount numeric(18,2) NOT NULL,
    outcome varchar(10) NOT NULL CHECK(outcome IN ('PAID','REVIEW')),
    received_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY(provider,transaction_id)
);
-- Only the backend calls this after authenticating the gateway notification.
CREATE OR REPLACE FUNCTION webstore.settle_gateway_payment(
    p_attempt varchar,p_provider varchar,p_merchant varchar,p_amount numeric,p_transaction varchar
) RETURNS text LANGUAGE plpgsql SECURITY INVOKER SET search_path=pg_catalog AS $$
DECLARE a record; h record; old_receipt record; item record; available integer; active boolean;
BEGIN
    SELECT * INTO a FROM webstore.gateway_attempt WHERE id=p_attempt;
    IF NOT FOUND THEN RAISE EXCEPTION 'PAYMENT_NOT_FOUND'; END IF;
    SELECT invoice.* INTO h FROM public.hoadon invoice JOIN webstore.order_snapshot s ON s.sohd=invoice.sohd AND s.makh=invoice.makh
        WHERE invoice.sohd=a.sohd FOR UPDATE OF invoice;
    -- Consistent invoice -> attempt lock order also used by payment creation.
    SELECT * INTO a FROM webstore.gateway_attempt WHERE id=p_attempt FOR UPDATE;
    IF a.provider<>p_provider OR a.merchant<>p_merchant OR a.amount<>p_amount OR p_amount<=0
        OR p_transaction IS NULL OR length(btrim(p_transaction)) NOT BETWEEN 1 AND 100 THEN
        RAISE EXCEPTION 'PAYMENT_MISMATCH';
    END IF;
    SELECT * INTO old_receipt FROM webstore.gateway_receipt WHERE provider=p_provider AND transaction_id=p_transaction;
    IF FOUND THEN
        IF old_receipt.attempt_id<>p_attempt OR old_receipt.amount<>p_amount THEN RAISE EXCEPTION 'PAYMENT_MISMATCH'; END IF;
        RETURN old_receipt.outcome;
    END IF;
    INSERT INTO webstore.gateway_receipt(provider,transaction_id,attempt_id,amount,outcome)
        VALUES(p_provider,p_transaction,p_attempt,p_amount,'REVIEW');
    -- Another attempt may have already paid this order. Preserve the extra receipt for staff/refund review.
    IF h.sohd IS NULL OR h.trangthaithanhtoan='DA_THANH_TOAN' THEN RETURN 'REVIEW'; END IF;
    BEGIN
        IF h.loaihoadon IS DISTINCT FROM 'WEB_CHO_THANH_TOAN' OR h.nguondon IS DISTINCT FROM 'WEB' OR h.khachphaitra IS DISTINCT FROM p_amount
            OR h.trangthaithanhtoan IS DISTINCT FROM 'CHUA_THANH_TOAN' OR h.phuongthucthanhtoan IS DISTINCT FROM a.method
            OR h.trangthaigiaohang IS DISTINCT FROM 'Chờ xác nhận thanh toán' THEN RAISE EXCEPTION 'Payment requires staff review'; END IF;
        IF NOT EXISTS(SELECT 1 FROM public.chitiethoadon WHERE sohd=a.sohd)
            OR (SELECT sum(thanhtien) FROM public.chitiethoadon WHERE sohd=a.sohd)<>h.tongcong THEN
            RAISE EXCEPTION 'Invoice lines require staff review';
        END IF;
        FOR item IN SELECT masku,soluong FROM public.chitiethoadon WHERE sohd=a.sohd ORDER BY masku LOOP
            SELECT p.soluongtonkho,p.trangthai AND c.trangthai INTO available,active
                FROM public.sanpham p JOIN public.loaisanpham c USING(maloai) WHERE p.masku=item.masku FOR UPDATE OF p FOR SHARE OF c;
            IF NOT FOUND OR NOT active OR item.soluong<=0 OR available<item.soluong THEN
                RAISE EXCEPTION 'Stock requires staff review';
            END IF;
            UPDATE public.sanpham SET soluongtonkho=soluongtonkho-item.soluong,soluong=soluong-item.soluong
                WHERE masku=item.masku AND soluongtonkho>=item.soluong AND soluong>=item.soluong;
            IF NOT FOUND THEN RAISE EXCEPTION 'Stock requires staff review'; END IF;
        END LOOP;
        UPDATE public.khachhang SET tongchitieu=COALESCE(tongchitieu,0)+p_amount,
            diemtichluy=COALESCE(diemtichluy,0)+floor(p_amount/10000)::integer,
            hangthe=CASE WHEN COALESCE(tongchitieu,0)+p_amount>15000000 THEN 'Kim Cương'
                        WHEN COALESCE(tongchitieu,0)+p_amount>5000000 THEN 'Vàng' ELSE 'Bạc' END
            WHERE makh=h.makh;
        IF NOT FOUND THEN RAISE EXCEPTION 'Customer requires staff review'; END IF;
        UPDATE public.hoadon SET loaihoadon='GIAO_HANG',trangthaithanhtoan='DA_THANH_TOAN',
            tienkhachdua=p_amount,tienthua=0,tiencod=0,trangthaigiaohang='Vừa xác nhận đơn' WHERE sohd=a.sohd;
        UPDATE webstore.gateway_attempt SET status='PAID' WHERE id=p_attempt;
        UPDATE webstore.gateway_receipt SET outcome='PAID' WHERE provider=p_provider AND transaction_id=p_transaction;
        RETURN 'PAID';
    EXCEPTION WHEN raise_exception THEN
        -- The inner block rolls back stock/loyalty changes, while the verified receipt is retained.
        UPDATE webstore.gateway_attempt SET status='REVIEW' WHERE id=p_attempt;
        UPDATE public.hoadon SET trangthaithanhtoan='DA_THANH_TOAN',tienkhachdua=p_amount,tiencod=0,
            trangthaigiaohang='Đã nhận tiền - cần cửa hàng xử lý' WHERE sohd=a.sohd;
        RETURN 'REVIEW';
    END;
END $$;
ALTER TABLE webstore.gateway_attempt ENABLE ROW LEVEL SECURITY;
ALTER TABLE webstore.gateway_receipt ENABLE ROW LEVEL SECURITY;
REVOKE ALL ON webstore.gateway_attempt,webstore.gateway_receipt FROM PUBLIC;
REVOKE ALL ON FUNCTION webstore.settle_gateway_payment(varchar,varchar,varchar,numeric,varchar) FROM PUBLIC;
DO $$ DECLARE r text; BEGIN
    FOREACH r IN ARRAY ARRAY['anon','authenticated'] LOOP
        IF EXISTS(SELECT 1 FROM pg_roles WHERE rolname=r) THEN
            EXECUTE format('REVOKE ALL ON webstore.gateway_attempt,webstore.gateway_receipt FROM %I',r);
            EXECUTE format('REVOKE ALL ON FUNCTION webstore.settle_gateway_payment(varchar,varchar,varchar,numeric,varchar) FROM %I',r);
        END IF;
    END LOOP;
END $$;
COMMIT;
