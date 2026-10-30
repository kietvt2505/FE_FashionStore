-- Apply after Supabase-Web-Orders.sql using the database owner.
-- No existing invoice is marked paid or has its stock changed by this migration.
BEGIN;
SET LOCAL lock_timeout = '5s';
SET LOCAL statement_timeout = '60s';
ALTER TABLE webstore.order_snapshot ADD COLUMN IF NOT EXISTS paymentmethod varchar(50) NOT NULL DEFAULT 'COD';
CREATE TABLE IF NOT EXISTS webstore.payment_confirmation (
    sohd varchar(30) PRIMARY KEY REFERENCES webstore.order_snapshot(sohd),
    reference varchar(150) NOT NULL UNIQUE CHECK (btrim(reference)<>''),
    amount numeric(18,2) NOT NULL CHECK (amount>0),
    employeeid varchar(20) NOT NULL REFERENCES public.nhanvien(manv),
    confirmedat timestamp without time zone NOT NULL DEFAULT LOCALTIMESTAMP
);
ALTER TABLE webstore.payment_confirmation ENABLE ROW LEVEL SECURITY;
REVOKE ALL ON webstore.payment_confirmation FROM PUBLIC;

-- Privileged backend/SQL Editor only. A customer selecting bank transfer is NOT evidence of payment.
-- SECURITY INVOKER preserves the caller's normal table permissions; no browser RPC grant.
CREATE OR REPLACE FUNCTION webstore.confirm_bank_payment(
    p_order varchar, p_employee varchar, p_amount numeric, p_reference varchar
) RETURNS text LANGUAGE plpgsql SECURITY INVOKER SET search_path = pg_catalog AS $$
DECLARE
    invoice record;
    existing record;
    item record;
    available integer;
    active boolean;
BEGIN
    IF p_amount IS NULL OR p_amount<=0 OR p_reference IS NULL OR length(btrim(p_reference)) NOT BETWEEN 1 AND 150 THEN
        RAISE EXCEPTION 'Invalid payment amount or bank reference';
    END IF;
    PERFORM 1 FROM public.nhanvien WHERE manv=p_employee AND trangthai=true FOR SHARE;
    IF NOT FOUND THEN RAISE EXCEPTION 'Active confirming employee is required'; END IF;
    SELECT h.* INTO invoice FROM public.hoadon h JOIN webstore.order_snapshot s ON s.sohd=h.sohd AND s.makh=h.makh
    WHERE h.sohd=p_order AND h.nguondon='WEB' AND s.paymentmethod='CHUYEN_KHOAN' FOR UPDATE OF h;
    IF NOT FOUND THEN RAISE EXCEPTION 'Pending website bank-transfer order not found'; END IF;
    SELECT * INTO existing FROM webstore.payment_confirmation WHERE sohd=p_order;
    IF FOUND THEN
        IF existing.reference=btrim(p_reference) AND existing.amount=p_amount THEN
            RETURN 'ALREADY_CONFIRMED';
        END IF;
        RAISE EXCEPTION 'Order already has a different payment confirmation';
    END IF;
    IF invoice.loaihoadon<>'WEB_CHO_THANH_TOAN' OR invoice.trangthaithanhtoan<>'CHUA_THANH_TOAN'
       OR invoice.phuongthucthanhtoan<>'CHUYEN_KHOAN' OR invoice.khachphaitra<>p_amount
       OR invoice.trangthaigiaohang<>'Chờ xác nhận thanh toán' THEN
        RAISE EXCEPTION 'Payment amount or current invoice state does not match';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM public.chitiethoadon WHERE sohd=p_order) THEN
        RAISE EXCEPTION 'Invoice has no items';
    END IF;
    IF (SELECT sum(thanhtien) FROM public.chitiethoadon WHERE sohd=p_order) <> invoice.tongcong THEN
        RAISE EXCEPTION 'Invoice item total does not match';
    END IF;
    -- Match existing Windows prepaid behavior: deduct both stock columns and credit loyalty on settlement.
    -- Lock SKUs in a stable order; conditional updates refuse stock below zero even with concurrent POS writes.
    FOR item IN SELECT masku,soluong FROM public.chitiethoadon WHERE sohd=p_order ORDER BY masku LOOP
        SELECT p.soluongtonkho,p.trangthai AND c.trangthai INTO available,active
        FROM public.sanpham p JOIN public.loaisanpham c USING(maloai) WHERE p.masku=item.masku FOR UPDATE OF p FOR SHARE OF c;
        IF NOT FOUND OR NOT active OR item.soluong<=0 OR available<item.soluong THEN
            RAISE EXCEPTION 'Insufficient stock or disabled SKU %; payment requires staff resolution', item.masku;
        END IF;
        UPDATE public.sanpham SET soluongtonkho=soluongtonkho-item.soluong,soluong=soluong-item.soluong
        WHERE masku=item.masku AND soluongtonkho>=item.soluong AND soluong>=item.soluong;
        IF NOT FOUND THEN RAISE EXCEPTION 'Stock columns do not permit settlement for SKU %',item.masku; END IF;
    END LOOP;
    UPDATE public.khachhang SET tongchitieu=tongchitieu+invoice.khachphaitra,
        diemtichluy=diemtichluy+floor(invoice.khachphaitra/10000)::integer,
        hangthe=CASE WHEN tongchitieu+invoice.khachphaitra>15000000 THEN 'Kim Cương'
                     WHEN tongchitieu+invoice.khachphaitra>5000000 THEN 'Vàng' ELSE 'Bạc' END
    WHERE makh=invoice.makh;
    IF NOT FOUND THEN RAISE EXCEPTION 'Invoice customer is missing'; END IF;
    INSERT INTO webstore.payment_confirmation(sohd,reference,amount,employeeid)
    VALUES(p_order,btrim(p_reference),p_amount,p_employee);
    UPDATE public.hoadon SET loaihoadon='GIAO_HANG',trangthaithanhtoan='DA_THANH_TOAN',
        tienkhachdua=p_amount,tienthua=0,tiencod=0,trangthaigiaohang='Vừa xác nhận đơn'
    WHERE sohd=p_order;
    RETURN 'CONFIRMED';
END $$;
REVOKE ALL ON FUNCTION webstore.confirm_bank_payment(varchar,varchar,numeric,varchar) FROM PUBLIC;
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname='anon') THEN
        REVOKE ALL ON webstore.payment_confirmation FROM anon;
        REVOKE ALL ON FUNCTION webstore.confirm_bank_payment(varchar,varchar,numeric,varchar) FROM anon;
    END IF;
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname='authenticated') THEN
        REVOKE ALL ON webstore.payment_confirmation FROM authenticated;
        REVOKE ALL ON FUNCTION webstore.confirm_bank_payment(varchar,varchar,numeric,varchar) FROM authenticated;
    END IF;
END $$;
COMMIT;
