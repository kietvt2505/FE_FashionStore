-- Compatibility with partner WinForms commit 6d11b10. No new tables.
-- NULL stock flags on other historical invoices deliberately require staff reconciliation.
BEGIN;
SET LOCAL lock_timeout='5s';
SET LOCAL statement_timeout='60s';
ALTER TABLE public.hoadon ADD COLUMN IF NOT EXISTS datrutonkho boolean NULL;
ALTER TABLE public.hoadon ALTER COLUMN datrutonkho SET DEFAULT FALSE;
ALTER TABLE public.hoadon ADD COLUMN IF NOT EXISTS daghinhanthanhtoan boolean NULL;
ALTER TABLE public.hoadon ALTER COLUMN daghinhanthanhtoan SET DEFAULT FALSE;

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
