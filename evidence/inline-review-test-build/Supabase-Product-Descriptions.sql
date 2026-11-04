-- Optional product copy for WinForms and the website. Existing rows remain unchanged.
BEGIN;
SET LOCAL lock_timeout='5s';
SET LOCAL statement_timeout='60s';
ALTER TABLE public.sanpham ADD COLUMN IF NOT EXISTS mota text;
COMMENT ON COLUMN public.sanpham.mota IS
    'Mo ta san pham dang van ban thuan. Dung chung noi dung cho cac SKU cung MaSP; website uu tien noi dung nay khi khong trong.';
COMMIT;

SELECT EXISTS (
    SELECT 1 FROM information_schema.columns
    WHERE table_schema='public' AND table_name='sanpham' AND column_name='mota' AND data_type='text'
) AS co_cot_mo_ta;
