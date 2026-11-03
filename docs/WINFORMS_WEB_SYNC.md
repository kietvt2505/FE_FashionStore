# Đồng bộ website với WinForms 6d11b10

Đối chiếu source tại `Winform_FashionStore/QLIShopThoiTrang`, commit `6d11b10`. Không sửa, build hoặc chạy API/initializer của form trên Supabase thật. Bộ kiểm thử chỉ biên dịch các lớp repository của form từ đường dẫn chỉ đọc, gọi vào database tạm.

## Các phần đã đối chiếu

- Các vỏ WinForms/WebView2, đăng nhập nhân viên, phiên nhân viên và API chạy cổng 5001; website tiếp tục dùng backend riêng cổng 5000.
- Quản lý SKU/loại sản phẩm, danh mục, ảnh, trạng thái bán, tồn kho/nhập kho; giá sale, voucher và hạng thành viên.
- Đối tác/khách hàng và API đọc/ghi tài khoản; POS lập hóa đơn, COD/giao hàng, đơn hủy, giữ kho và kiểm nhận hàng hoàn.
- Yêu cầu đổi trả, duyệt/từ chối, giá trị thực trả từng dòng; QR payOS và báo cáo. Các phần không liên quan lỗi hiện tại giữ nguyên hợp đồng đang có.
- Thư mục `web/FE_FashionStore` nằm trong repo partner là bản website kèm theo; không chép đè nó lên website đang sửa.

## Mật khẩu

Form hiện tại trả nguyên `khachhang.matkhau` qua `GetPartnerCredentialsAsync`, điền vào ô mật khẩu và ghi giá trị nhập qua `SavePartnerAsync`. Băm một chiều không thể giải mã. Giữ nguyên form nghĩa là không thể sửa cách form hiển thị chuỗi băm từ phía web.

Web tiếp tục tạo mật khẩu Identity V3. Chế độ Supabase chỉ chuyển đổi giá trị chữ thường cũ khi khách nhập đúng mật khẩu; so sánh thời gian cố định, thay bằng hash trước khi cấp phiên. Đăng nhập sai không sửa dữ liệu. Giá trị có dạng hash không được chấp nhận bằng cách nhập chính chuỗi hash. Không hỗ trợ giải mã/so sánh MD5. Mật khẩu form mới ghi còn ở dạng chữ thường cho đến lần đăng nhập web đúng; muốn chặn ngay khi ghi và bỏ việc hiển thị hash phải thay đổi nơi form đọc/ghi mật khẩu sau này.

Đăng nhập nhận tối đa 200 ký tự theo form; quy tắc đăng ký/đặt mật khẩu mới của website vẫn giữ 12–128 ký tự. Tài khoản được đối chiếu bằng tên đăng nhập, không tự gán hóa đơn/hồ sơ bằng số điện thoại.

## Đơn hàng và tồn kho

- COD mới: `loaihoadon=COD`, `nguondon=WEB`, `trangthaigiaohang=Vừa xác nhận đơn`, `CHUA_THANH_TOAN`. Đây là trạng thái đầu tiên form hiện có chấp nhận; không có bước nhân viên xác nhận riêng trước nó khi giữ nguyên form.
- Website giữ tồn trong cùng transaction tạo hóa đơn/chi tiết, rồi ghi `datrutonkho=TRUE`, `daghinhanthanhtoan=FALSE`. Form thấy cờ đã giữ và không trừ kho thêm; giao thành công ghi nhận thanh toán/tích điểm theo form.
- Khóa sản phẩm theo thứ tự SKU; không khóa hàng khách hàng trước sản phẩm vì form cập nhật điểm sau khi giữ khóa sản phẩm. Gửi lại cùng request không tạo đơn/trừ tồn lần hai.
- Hủy giữ nguyên loại giao hàng hợp lệ (`COD` hoặc `GIAO_HANG`) và ghi `Đã hủy`, để danh sách đơn hủy của form đọc được. Không tự cộng kho hoặc xác nhận hoàn tiền: form kiểm nhận hàng thực tế mới quyết định nhập lại kho.
- Lịch sử và chi tiết đọc mọi hóa đơn có `makh` đúng khách đăng nhập, gồm hóa đơn POS. Hóa đơn POS chỉ đọc trên web; không tự mở quyền hủy/sửa/thực hiện đổi trả web cho hóa đơn POS.
- Tiền chuyển khoản vẫn cần nhân viên xác minh. Website không đánh dấu đã thanh toán chỉ vì người dùng chọn chuyển khoản.

## Áp dụng

`scripts/Sync-WinForms.ps1 -StartBackend` dùng chuỗi kết nối hiện có, hoặc hỏi nhập kín nếu thiếu. Script áp dụng `database/Supabase-DongBoWinForms.sql`, kiểm tra giữ đúng 18 bảng, giữ nguyên tài khoản, tiền và số lượng tồn, rồi chạy lại backend website. Chỉ bổ sung hai cột cờ tiếng Việt mà form mới đã dùng nếu còn thiếu; không tạo bảng.

Đơn COD web cũ còn chờ, chưa thu tiền và chưa có QR được đưa về trạng thái đầu của form. Cờ tồn lịch sử không xác định ở các trường hợp khác tiếp tục để NULL cho nhân viên đối chiếu; không đoán đã xuất kho. Chạy lặp không đặt lại cờ đã xử lý. Script chỉ sửa các trường trạng thái/cờ đã nêu; dữ liệu khác thay đổi thì rollback.

Database có thể còn trigger từ bộ bảng bổ sung đã xóa. Bản đồng bộ cập nhật `guard_cancelled_web_order` để chỉ cho phép đổi loại `WEB_HUY` sang loại giao hàng phù hợp khi toàn bộ trường khác giữ nguyên; vẫn chặn mở lại/xóa đơn đã hủy, kể cả trạng thái bị đổi thành NULL. Trigger `capture_cancelled_order_payment` cũ chỉ được gỡ khỏi `hoadon` nếu đúng hàm cũ và thiếu bảng phụ thuộc; không xóa chứng từ, tiền hay chức năng hoàn tiền native-18 trên `phieudoitra`. Các trigger khác giữ nguyên. Script lưu riêng metadata trigger trước khi áp dụng để đối chiếu khi lỗi; báo cáo này không chứa dữ liệu khách hàng. Nếu lỗi, cửa sổ chờ R để thử lại sau khi sửa SQL hoặc Q để đóng; kết nối không lưu xuống file.

## Quên mật khẩu

Đã kiểm thử liên kết gửi qua lớp email, đặt lại mật khẩu, đăng nhập bằng mật khẩu mới, hết hạn/dùng một lần và vô hiệu phiên cũ. Không gửi thư ra ngoài trong kiểm thử. Cửa hàng hiện chưa có email gửi nên chưa bật gửi thư thật; API trả thông báo chưa cấu hình cho mọi địa chỉ. Khi có email, cấu hình SMTP TLS và `Recovery:ResetUrl` HTTPS trỏ tới website `/Account/Reset` (máy này: `https://localhost:44360/Account/Reset`), không phải API. Không đưa mật khẩu SMTP vào Git.

## Yêu thích

Thêm/bỏ ngay tại trang hiện tại bằng POST có CSRF; không chuyển sang danh sách. Trạng thái tim được hiển thị khi tải trang và cập nhật trên các nút cùng sản phẩm. Bấm lặp lúc đang gửi không tạo thêm thao tác. Khách chưa đăng nhập vẫn đăng nhập/đăng ký rồi quay lại đúng URL, bộ lọc, số trang và vị trí. Lưu lại sau đăng nhập là thao tác thêm có tính lặp an toàn, không đổi thứ tự sản phẩm đã có.

## Kiểm thử

Chạy native-18 trên database PostgreSQL tạm với `FASHIONSTORE_POSTGRES_TEST_CONNECTION`. Thêm `-p:PartnerSourceRoot=<đường dẫn QLIShopThoiTrang>` khi chạy `dotnet test` để gọi nguyên `UpdateDeliveryStatusAsync` của form. `node verification/Verify-Wishlist.js` kiểm tra thao tác yêu thích tại chỗ, bấm lặp, lỗi mạng và chuyển đăng nhập. Build MVC kèm Razor views để kiểm tra giao diện.
