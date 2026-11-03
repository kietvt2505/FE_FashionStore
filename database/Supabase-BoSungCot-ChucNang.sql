-- Bo sung cot vao 4 bang hien co. Khong tao/xoa bang, khong doi cot cu.
-- Cot tieng Viet khong dau, chu thuong, viet lien theo quy uoc partner.
BEGIN;
SET LOCAL lock_timeout='5s';
SET LOCAL statement_timeout='120s';

ALTER TABLE public.khachhang
    ADD COLUMN IF NOT EXISTS makhoiphucbam varchar(64),
    ADD COLUMN IF NOT EXISTS hethanmakhoiphuc timestamp with time zone,
    ADD COLUMN IF NOT EXISTS phienmatkhaukhoiphuc varchar(64);
ALTER TABLE public.chitiethoadon
    ADD COLUMN IF NOT EXISTS masplucmua varchar(20),
    ADD COLUMN IF NOT EXISTS tensanphamlucmua varchar(200),
    ADD COLUMN IF NOT EXISTS kichcolucmua varchar(30),
    ADD COLUMN IF NOT EXISTS mausaclucmua varchar(60),
    ADD COLUMN IF NOT EXISTS hinhanhlucmua text,
    ADD COLUMN IF NOT EXISTS sosao smallint,
    ADD COLUMN IF NOT EXISTS noidungdanhgia varchar(2000),
    ADD COLUMN IF NOT EXISTS ngaydanhgia timestamp with time zone,
    ADD COLUMN IF NOT EXISTS trangthaidanhgia varchar(20);
ALTER TABLE public.hoadon
    ADD COLUMN IF NOT EXISTS lydohuy varchar(500),
    ADD COLUMN IF NOT EXISTS ngayhuy timestamp with time zone;
ALTER TABLE public.phieudoitra
    ADD COLUMN IF NOT EXISTS tennganhanghoantien varchar(100),
    ADD COLUMN IF NOT EXISTS sotaikhoanhoantien varchar(34),
    ADD COLUMN IF NOT EXISTS chutaikhoanhoantien varchar(150),
    ADD COLUMN IF NOT EXISTS mathamchieuhoantien varchar(100),
    ADD COLUMN IF NOT EXISTS ngayhoantien timestamp with time zone;

DO $$ BEGIN
    IF NOT EXISTS(SELECT 1 FROM pg_constraint WHERE conrelid='public.khachhang'::regclass AND conname='kiemtra_makhoiphuc') THEN
        ALTER TABLE public.khachhang ADD CONSTRAINT kiemtra_makhoiphuc CHECK(
          (makhoiphucbam IS NULL AND hethanmakhoiphuc IS NULL AND phienmatkhaukhoiphuc IS NULL) OR
          (makhoiphucbam IS NOT NULL AND hethanmakhoiphuc IS NOT NULL AND phienmatkhaukhoiphuc IS NOT NULL
            AND makhoiphucbam ~ '^[0-9A-F]{64}$' AND phienmatkhaukhoiphuc ~ '^[0-9A-F]{64}$'));
    END IF;
    IF NOT EXISTS(SELECT 1 FROM pg_constraint WHERE conrelid='public.chitiethoadon'::regclass AND conname='kiemtra_danhgia') THEN
        ALTER TABLE public.chitiethoadon ADD CONSTRAINT kiemtra_danhgia CHECK(
          (sosao IS NULL AND noidungdanhgia IS NULL AND ngaydanhgia IS NULL AND trangthaidanhgia IS NULL) OR
          (sosao IS NOT NULL AND sosao BETWEEN 1 AND 5 AND NULLIF(btrim(noidungdanhgia),'') IS NOT NULL
            AND ngaydanhgia IS NOT NULL AND trangthaidanhgia IS NOT NULL
            AND trangthaidanhgia IN ('HIEN_THI','CHO_DUYET','TU_CHOI')));
    END IF;
    IF NOT EXISTS(SELECT 1 FROM pg_constraint WHERE conrelid='public.phieudoitra'::regclass AND conname='kiemtra_taikhoanhoantien') THEN
        ALTER TABLE public.phieudoitra ADD CONSTRAINT kiemtra_taikhoanhoantien CHECK(
          (tennganhanghoantien IS NULL AND sotaikhoanhoantien IS NULL AND chutaikhoanhoantien IS NULL) OR
          (NULLIF(btrim(tennganhanghoantien),'') IS NOT NULL AND sotaikhoanhoantien IS NOT NULL
            AND sotaikhoanhoantien ~ '^[0-9]{6,34}$' AND NULLIF(btrim(chutaikhoanhoantien),'') IS NOT NULL));
    END IF;
END $$;
CREATE UNIQUE INDEX IF NOT EXISTS duy_nhat_makhoiphuc ON public.khachhang(makhoiphucbam) WHERE makhoiphucbam IS NOT NULL;

-- Don cu: chi co the sao chep thong tin san pham hien tai, khong suy doan lich su da mat.
-- Chi dien dong chua tung co snapshot; chay lai khong ghi de snapshot da luu.
UPDATE public.chitiethoadon d SET masplucmua=p.masp,tensanphamlucmua=p.tensanpham,
    kichcolucmua=p.kichco,mausaclucmua=p.mausac,hinhanhlucmua=p.hinhanh
FROM public.sanpham p WHERE p.masku=d.masku AND d.masplucmua IS NULL;
CREATE UNIQUE INDEX IF NOT EXISTS duy_nhat_danhgia_sanpham_don ON public.chitiethoadon(sohd,masplucmua) WHERE sosao IS NOT NULL;
CREATE INDEX IF NOT EXISTS tracuu_danhgia_sanpham ON public.chitiethoadon(masplucmua,ngaydanhgia DESC) WHERE trangthaidanhgia='HIEN_THI';

CREATE OR REPLACE FUNCTION public.ghinhansanphamlucmua()
RETURNS trigger LANGUAGE plpgsql SECURITY INVOKER SET search_path=pg_catalog AS $$
DECLARE sanpham record; doi_danhgia boolean;
BEGIN
    IF TG_OP='INSERT' OR NEW.masku IS DISTINCT FROM OLD.masku THEN
        IF TG_OP='UPDATE' AND OLD.sosao IS NOT NULL THEN
            RAISE EXCEPTION 'Khong duoc doi SKU cua chi tiet da danh gia' USING ERRCODE='23514';
        END IF;
        SELECT p.masp,p.tensanpham,p.kichco,p.mausac,p.hinhanh INTO sanpham FROM public.sanpham p WHERE p.masku=NEW.masku;
        IF NOT FOUND THEN RAISE EXCEPTION 'SKU khong ton tai' USING ERRCODE='23503'; END IF;
        NEW.masplucmua=sanpham.masp;NEW.tensanphamlucmua=sanpham.tensanpham;
        NEW.kichcolucmua=sanpham.kichco;NEW.mausaclucmua=sanpham.mausac;NEW.hinhanhlucmua=sanpham.hinhanh;
    END IF;
    IF TG_OP='INSERT' THEN doi_danhgia=NEW.sosao IS NOT NULL;
    ELSE doi_danhgia=ROW(NEW.sosao,NEW.noidungdanhgia,NEW.ngaydanhgia,NEW.trangthaidanhgia)
        IS DISTINCT FROM ROW(OLD.sosao,OLD.noidungdanhgia,OLD.ngaydanhgia,OLD.trangthaidanhgia); END IF;
    IF doi_danhgia THEN
        IF current_user IN ('anon','authenticated') THEN
            RAISE EXCEPTION 'Danh gia phai qua backend xac thuc khach hang' USING ERRCODE='42501';
        END IF;
        IF NEW.sosao IS NOT NULL AND NEW.trangthaidanhgia<>'TU_CHOI' AND NOT EXISTS(SELECT 1 FROM public.hoadon h WHERE h.sohd=NEW.sohd
          AND h.trangthaigiaohang='Hàng giao thành công' AND h.trangthaithanhtoan='DA_THANH_TOAN' AND h.loaihoadon<>'WEB_HUY') THEN
            RAISE EXCEPTION 'Chi danh gia sau khi nhan hang va thanh toan' USING ERRCODE='23514';
        END IF;
    END IF;
    RETURN NEW;
END $$;
DROP TRIGGER IF EXISTS ghinhansanphamlucmua ON public.chitiethoadon;
CREATE TRIGGER ghinhansanphamlucmua BEFORE INSERT OR UPDATE OF masku,sosao,noidungdanhgia,ngaydanhgia,trangthaidanhgia
    ON public.chitiethoadon FOR EACH ROW EXECUTE FUNCTION public.ghinhansanphamlucmua();

CREATE OR REPLACE FUNCTION public.baovecotkhoiphucmatkhau()
RETURNS trigger LANGUAGE plpgsql SECURITY INVOKER SET search_path=pg_catalog AS $$
BEGIN
    IF current_user IN ('anon','authenticated') THEN
        IF TG_OP='INSERT' THEN
            IF NEW.makhoiphucbam IS NOT NULL OR NEW.hethanmakhoiphuc IS NOT NULL OR NEW.phienmatkhaukhoiphuc IS NOT NULL THEN
                RAISE EXCEPTION 'Khoi phuc mat khau phai qua backend' USING ERRCODE='42501';
            END IF;
        ELSIF ROW(NEW.makhoiphucbam,NEW.hethanmakhoiphuc,NEW.phienmatkhaukhoiphuc)
              IS DISTINCT FROM ROW(OLD.makhoiphucbam,OLD.hethanmakhoiphuc,OLD.phienmatkhaukhoiphuc) THEN
            RAISE EXCEPTION 'Khoi phuc mat khau phai qua backend' USING ERRCODE='42501';
        END IF;
    END IF;
    RETURN NEW;
END $$;
DROP TRIGGER IF EXISTS baovecotkhoiphucmatkhau ON public.khachhang;
CREATE TRIGGER baovecotkhoiphucmatkhau BEFORE INSERT OR UPDATE OF makhoiphucbam,hethanmakhoiphuc,phienmatkhaukhoiphuc
    ON public.khachhang FOR EACH ROW EXECUTE FUNCTION public.baovecotkhoiphucmatkhau();
REVOKE ALL ON FUNCTION public.ghinhansanphamlucmua(),public.baovecotkhoiphucmatkhau() FROM PUBLIC;

COMMENT ON COLUMN public.khachhang.makhoiphucbam IS 'SHA-256 của mã khôi phục ngẫu nhiên; không lưu mã gửi email dạng rõ.';
COMMENT ON COLUMN public.khachhang.hethanmakhoiphuc IS 'Hạn dùng mã khôi phục; mỗi tài khoản chỉ có một mã đang hiệu lực.';
COMMENT ON COLUMN public.khachhang.phienmatkhaukhoiphuc IS 'Dấu băm phiên mật khẩu và email khi yêu cầu; đổi mật khẩu/email làm mã cũ mất hiệu lực.';
COMMENT ON COLUMN public.chitiethoadon.masplucmua IS 'Mã sản phẩm tại thời điểm lập chi tiết hóa đơn; giữ khi danh mục thay đổi.';
COMMENT ON COLUMN public.chitiethoadon.tensanphamlucmua IS 'Tên sản phẩm tại thời điểm lập chi tiết; dữ liệu cũ sao từ danh mục hiện tại.';
COMMENT ON COLUMN public.chitiethoadon.kichcolucmua IS 'Kích cỡ lúc mua.';
COMMENT ON COLUMN public.chitiethoadon.mausaclucmua IS 'Màu sắc lúc mua.';
COMMENT ON COLUMN public.chitiethoadon.hinhanhlucmua IS 'Đường dẫn ảnh lúc mua.';
COMMENT ON COLUMN public.chitiethoadon.sosao IS 'Một đến năm sao; tối đa một đánh giá cho mỗi sản phẩm trong một đơn.';
COMMENT ON COLUMN public.chitiethoadon.noidungdanhgia IS 'Nội dung đánh giá sau khi nhận hàng và thanh toán.';
COMMENT ON COLUMN public.chitiethoadon.ngaydanhgia IS 'Thời điểm gửi đánh giá.';
COMMENT ON COLUMN public.chitiethoadon.trangthaidanhgia IS 'HIEN_THI, CHO_DUYET hoặc TU_CHOI; NULL khi chưa đánh giá.';
COMMENT ON COLUMN public.hoadon.lydohuy IS 'Lý do hủy đơn; tách khỏi ghi chú giao hàng.';
COMMENT ON COLUMN public.hoadon.ngayhuy IS 'Thời điểm hủy đơn thực tế; không suy đoán thời điểm của đơn cũ.';
COMMENT ON COLUMN public.phieudoitra.tennganhanghoantien IS 'Ngân hàng khách đăng ký nhận hoàn tiền.';
COMMENT ON COLUMN public.phieudoitra.sotaikhoanhoantien IS 'Số tài khoản nhận hoàn tiền; chỉ hiển thị che bớt cho khách.';
COMMENT ON COLUMN public.phieudoitra.chutaikhoanhoantien IS 'Tên chủ tài khoản nhận hoàn tiền.';
COMMENT ON COLUMN public.phieudoitra.mathamchieuhoantien IS 'Mã tham chiếu xác minh hoàn tiền; không tự xác nhận đã chuyển tiền.';
COMMENT ON COLUMN public.phieudoitra.ngayhoantien IS 'Thời điểm cửa hàng xác nhận hoàn tiền; không tự điền cho phiếu lịch sử.';
COMMIT;
