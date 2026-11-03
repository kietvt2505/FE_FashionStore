$ErrorActionPreference='Stop'
$schemaRoot=Split-Path -Parent $PSScriptRoot
$databaseRoot=Join-Path $schemaRoot 'database'
$parts=[System.Collections.Generic.List[string]]::new()
$parts.Add(@'
-- Khoi phuc cac chuc nang website theo cau truc public ngay 03/11/2026.
-- Chi bo sung cac bang con thieu voi ten tieng Viet; khong tao schema/DB moi.
-- Tai khoan dung khachhang.tendangnhap/matkhau. Anh/mo ta/phan loai dung sanpham.
-- Gia san pham dung view sanphamkhuyenmai_hientai hien co.
-- Chay ca file trong SQL Editor cua dung project Khoa Luan Cu Nhan.
BEGIN;
SET LOCAL lock_timeout='5s';
SET LOCAL statement_timeout='120s';
-- Lich su/kiem thu localhost: du an da chot chi 18 bang goc, giu nguyen cac cot.
DO $$ BEGIN
    IF current_database() NOT LIKE 'fashionstore_vietnamese_%' THEN
        RAISE EXCEPTION 'Bo cai 17 bang bo sung da ngung su dung. Khong ap dung tren database that.';
    END IF;
END $$;
DO $$ BEGIN
    PERFORM makh,tendangnhap,matkhau,nhankhuyenmai FROM public.khachhang LIMIT 0;
    PERFORM masp,masku,maloai,dongia,soluongtonkho,danhmuc,danhsachhinhanh,mota FROM public.sanpham LIMIT 0;
    PERFORM sohd,makh,nguondon,tennguoinhan,sodienthoai_kh,diachigiao,ghichugiaohang,khachphaitra FROM public.hoadon LIMIT 0;
    PERFORM sohd,masku,soluong,dongia,thanhtien FROM public.chitiethoadon LIMIT 0;
    PERFORM masku,giagoc,giakhuyenmai,phantramgiam FROM public.sanphamkhuyenmai_hientai LIMIT 0;
    PERFORM magd,sohd,nguondon,sotien,malienket,trangthai FROM public.giaodichthanhtoanqr LIMIT 0;
END $$;
'@)
$schemaFiles=@('Supabase-Customer-Cart.sql','Supabase-Web-Orders.sql','Supabase-Membership-Pricing.sql',
    'Supabase-Checkout-Voucher-Codes.sql','Supabase-Web-Payments.sql','Supabase-Customer-Features.sql',
    'Supabase-Customer-Order-Cancellation.sql','Supabase-Order-Cancellation-Policy.sql',
    'Supabase-Online-Payments.sql','Supabase-Order-Cancellation-Refunds.sql','Supabase-Short-Order-Numbers.sql',
    'Supabase-Daily-Order-Numbers.sql','Supabase-PayOS.sql')
foreach($schemaFile in $schemaFiles){
    $body=[System.IO.File]::ReadAllText((Join-Path $databaseRoot $schemaFile))
    # Keep PL/pgSQL blocks; remove only standalone transaction/settings lines from the component scripts.
    $body=[regex]::Replace($body,'(?m)^\s*(BEGIN;|COMMIT;|SET LOCAL[^\r\n]*;)\s*$','')
    $parts.Add("`n-- Thanh phan: $schemaFile`n"+$body)
}
$enhancements=[System.IO.File]::ReadAllText((Join-Path $databaseRoot 'Supabase-Storefront-Enhancements.sql'))
$mergeStart=$enhancements.IndexOf('CREATE TABLE IF NOT EXISTS public.lichsugopgiohang(')
$mergeEnd=$enhancements.IndexOf('ALTER TABLE public.hinhanhsanpham ENABLE')
if($mergeStart -lt 0 -or $mergeEnd -le $mergeStart){throw 'Khong tim thay thanh phan gop gio hang.'}
$parts.Add($enhancements.Substring($mergeStart,$mergeEnd-$mergeStart))
$parts.Add(@'
ALTER TABLE public.lichsugopgiohang ENABLE ROW LEVEL SECURITY;
REVOKE ALL ON public.lichsugopgiohang FROM PUBLIC;
REVOKE ALL ON FUNCTION public.merge_guest_cart(varchar,uuid,jsonb) FROM PUBLIC;

-- Adapter chi doc: gia goc va khuyen mai tu chinh view partner hien tai.
CREATE OR REPLACE VIEW public.giabanthuctesanpham WITH(security_invoker=true) AS
SELECT p.masku,p.dongia AS base_price,COALESCE(k.giakhuyenmai,p.dongia) AS sale_price,k.phantramgiam AS native_percent
FROM public.sanpham p LEFT JOIN public.sanphamkhuyenmai_hientai k ON k.masku=p.masku;
REVOKE ALL ON public.giabanthuctesanpham FROM PUBLIC;

-- Khong tao lai cac don da xoa. Chi noi cac hoa don WEB dang con lai voi du lieu website.
-- Gia/so luong lay tu hoa don; ten/size/mau lay tu san pham hien tai neu snapshot cu da bi xoa.
INSERT INTO public.thongtindonhang(sohd,makh,ngaylap,recipient,phone,address,note,total,paymentmethod)
SELECT h.sohd,h.makh,h.ngaylap,COALESCE(h.tennguoinhan,k.hoten,''),COALESCE(h.sodienthoai_kh,k.sodienthoai,''),
    COALESCE(h.diachigiao,k.diachi,''),COALESCE(h.ghichugiaohang,''),h.khachphaitra,COALESCE(h.phuongthucthanhtoan,'COD')
FROM public.hoadon h JOIN public.khachhang k ON k.makh=h.makh
WHERE h.nguondon='WEB' AND h.khachphaitra>0 ON CONFLICT(sohd) DO NOTHING;
INSERT INTO public.chitietdonhanglucmua(sohd,masku,name,size,color,quantity,price)
SELECT d.sohd,d.masku,p.tensanpham,p.kichco,p.mausac,d.soluong,d.dongia
FROM public.chitiethoadon d JOIN public.thongtindonhang s USING(sohd) JOIN public.sanpham p USING(masku)
WHERE d.soluong>0 AND d.dongia>0 ON CONFLICT(sohd,masku) DO NOTHING;
INSERT INTO public.uudaidonhang(sohd,subtotal,discount,voucher,member_discount,giamgiatruocdo)
SELECT h.sohd,h.tongcong,GREATEST(h.tongcong-h.khachphaitra,0),h.makm,0,GREATEST(h.tongcong-h.khachphaitra,0)
FROM public.hoadon h JOIN public.thongtindonhang s USING(sohd)
WHERE h.tongcong>0 AND h.khachphaitra>0 AND h.tongcong>=h.khachphaitra ON CONFLICT(sohd) DO NOTHING;

DO $$ DECLARE quyen text; BEGIN
  FOREACH quyen IN ARRAY ARRAY['anon','authenticated'] LOOP
    IF EXISTS(SELECT 1 FROM pg_roles WHERE rolname=quyen) THEN
      EXECUTE format('REVOKE ALL ON public.lichsugopgiohang,public.giabanthuctesanpham FROM %I',quyen);
      EXECUTE format('REVOKE ALL ON FUNCTION public.merge_guest_cart(varchar,uuid,jsonb) FROM %I',quyen);
    END IF;
  END LOOP;
END $$;
COMMIT;
SELECT 'DA_CAP_NHAT' AS ketqua,
    (SELECT count(*) FROM pg_tables WHERE schemaname='public') AS sobang,
    (SELECT count(*) FROM public.thongtindonhang) AS sodonweb;
'@)
$target=Join-Path $databaseRoot 'Supabase-Website-TenViet.sql'
[System.IO.File]::WriteAllText($target,($parts -join "`n"),[System.Text.UTF8Encoding]::new($false))
Write-Host "Da tao $target"
