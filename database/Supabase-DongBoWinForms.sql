-- Compatibility with partner WinForms commit 6d11b10. No new tables.
-- NULL stock flags on other historical invoices deliberately require staff reconciliation.
BEGIN;
SET LOCAL lock_timeout='5s';
SET LOCAL statement_timeout='60s';
ALTER TABLE public.hoadon ADD COLUMN IF NOT EXISTS datrutonkho boolean NULL;
ALTER TABLE public.hoadon ALTER COLUMN datrutonkho SET DEFAULT FALSE;
ALTER TABLE public.hoadon ADD COLUMN IF NOT EXISTS daghinhanthanhtoan boolean NULL;
ALTER TABLE public.hoadon ALTER COLUMN daghinhanthanhtoan SET DEFAULT FALSE;

-- The retired supplemental-table schema can leave these triggers behind after its
-- tables are deleted. Keep cancellation protection while accepting the native kind.
CREATE OR REPLACE FUNCTION public.guard_cancelled_web_order()
RETURNS trigger LANGUAGE plpgsql SECURITY INVOKER SET search_path=pg_catalog AS $$
DECLARE cancelled boolean; legacy_kind_move boolean;
BEGIN
    cancelled := OLD.loaihoadon='WEB_HUY' OR
        (upper(btrim(COALESCE(OLD.nguondon,'')))='WEB' AND OLD.trangthaigiaohang IN
            ('Đã hủy','Đã huỷ','HUY','DA_HUY','CANCELLED','CANCELED','Đã nhận tiền - cần cửa hàng xử lý'));
    IF cancelled THEN
        IF TG_OP='DELETE' THEN RAISE EXCEPTION 'Website order was cancelled; retain its invoice'; END IF;
        legacy_kind_move := COALESCE(OLD.loaihoadon='WEB_HUY' AND OLD.nguondon='WEB'
            AND OLD.trangthaigiaohang='Đã hủy'
            AND NEW.loaihoadon=CASE WHEN OLD.phuongthucthanhtoan='COD' THEN 'COD' ELSE 'GIAO_HANG' END
            AND (to_jsonb(NEW)-'loaihoadon')=(to_jsonb(OLD)-'loaihoadon'),FALSE);
        IF (NEW.loaihoadon IS DISTINCT FROM OLD.loaihoadon AND NOT legacy_kind_move)
           OR (NEW.trangthaigiaohang IS DISTINCT FROM OLD.trangthaigiaohang AND NOT (
                NEW.trangthaigiaohang IS NOT DISTINCT FROM 'Đã nhận tiền - cần cửa hàng xử lý'
                AND NEW.trangthaithanhtoan IS NOT DISTINCT FROM 'DA_THANH_TOAN')) THEN
            RAISE EXCEPTION 'Website order was cancelled; refresh its status';
        END IF;
    END IF;
    IF TG_OP='DELETE' THEN RETURN OLD; END IF;
    RETURN NEW;
END $$;
DROP TRIGGER IF EXISTS guard_cancelled_web_order ON public.hoadon;
CREATE TRIGGER guard_cancelled_web_order BEFORE UPDATE OR DELETE ON public.hoadon
FOR EACH ROW EXECUTE FUNCTION public.guard_cancelled_web_order();
REVOKE ALL ON FUNCTION public.guard_cancelled_web_order() FROM PUBLIC;

-- Only detach the known obsolete refund-capture trigger when its backing tables
-- are missing. Native-18 refund handling uses phieudoitra; no invoice/money is deleted.
DO $$ BEGIN
    IF EXISTS(SELECT 1 FROM pg_trigger t JOIN pg_proc p ON p.oid=t.tgfoid
              JOIN pg_namespace n ON n.oid=p.pronamespace
              WHERE t.tgrelid='public.hoadon'::regclass AND t.tgname='capture_cancelled_order_payment'
                AND p.proname='capture_cancelled_order_payment' AND n.nspname='public')
       AND (to_regclass('public.lichsuhuydonhang') IS NULL OR to_regclass('public.biennhanthanhtoantructuyen') IS NULL
         OR to_regclass('public.yeucauthanhtoantructuyen') IS NULL OR to_regclass('public.yeucauhoantien') IS NULL) THEN
        DROP TRIGGER capture_cancelled_order_payment ON public.hoadon;
    END IF;
END $$;

-- Old website code never reserved stock while its own COD invoices were pending.
-- Do not reinterpret paid invoices, QR attempts or any flag already set by the form.
UPDATE public.hoadon h
SET trangthaigiaohang='Vừa xác nhận đơn',
    datrutonkho=COALESCE(datrutonkho,FALSE),
    daghinhanthanhtoan=COALESCE(daghinhanthanhtoan,FALSE)
WHERE upper(btrim(h.nguondon))='WEB' AND h.dauvantayyeucau IS NOT NULL
  AND upper(h.loaihoadon) IN ('COD','WEB_COD') AND h.phuongthucthanhtoan='COD'
  AND h.trangthaigiaohang IN ('Chờ xác nhận','Đang chờ xác nhận','MOI','CHO_XAC_NHAN','CHUA_GIAO')
  AND h.trangthaithanhtoan='CHUA_THANH_TOAN' AND h.tienkhachdua=0
  AND h.ngaygiaothanhcong IS NULL
  AND NOT EXISTS(SELECT 1 FROM public.giaodichthanhtoanqr q WHERE q.sohd=h.sohd);

-- WinForms lists cancelled shipments by delivery kind, not by WEB_HUY.
UPDATE public.hoadon
SET loaihoadon=CASE WHEN phuongthucthanhtoan='COD' THEN 'COD' ELSE 'GIAO_HANG' END
WHERE nguondon='WEB' AND loaihoadon='WEB_HUY' AND trangthaigiaohang='Đã hủy';
COMMIT;
