# Khách hàng hủy đơn website

**Bản mở rộng hiện tại:** xem `ORDER_CANCELLATION_REFUNDS.md` và chạy `database/Supabase-Order-Cancellation-Refunds.sql` sau cùng. Bản này bổ sung lý do, tài khoản nhận hoàn tiền và cho phép hủy đơn online đã thanh toán hợp lệ ở hai bước đầu. Các giới hạn chỉ đơn chưa thanh toán dưới đây mô tả bản cũ trước khi cài phần mở rộng.

## Bật chức năng

Chạy toàn bộ `database/Supabase-Customer-Order-Cancellation.sql` trong Supabase SQL Editor, sau các file đơn hàng và chuyển khoản đã cài trước đó. Hai cột kiểm tra cuối file phải cùng `true`. Khởi động lại backend bằng lệnh quen thuộc rồi tải lại website.

Nếu đã cài chức năng hủy trước ngày 29/10/2026, chạy thêm `database/Supabase-Order-Cancellation-Policy.sql` để cập nhật điều kiện hủy. File chỉ thay hàm kiểm tra điều kiện, có thể chạy lại và không sửa các đơn đã có. Backend mới cũng kiểm tra bước giao hàng khi đọc và khi hủy, kể cả khi database còn dùng điều kiện cũ.

Trong **Đơn hàng của tôi → Chi tiết đơn hàng**, khách thấy thẻ **Hủy đơn hàng** nếu API xác nhận đơn đủ điều kiện. Bấm **Hủy đơn hàng**, rồi **Xác nhận hủy đơn** trong phần mở ra ngay trên trang. Chọn **Giữ đơn hàng** để đóng xác nhận mà không gửi yêu cầu. Website gửi yêu cầu có chống giả mạo và kiểm tra lại quyền sở hữu/trạng thái tại database. Đơn đã hủy vẫn có sản phẩm, giá và thời gian hủy để tra cứu, xuất hiện trong bộ lọc **Đã hủy**.

## Điều kiện

- Chỉ khách sở hữu đơn website được hủy.
- Chỉ đơn COD hoặc đơn chờ thanh toán, database còn ghi chưa thanh toán và chưa có tiền khách đưa.
- Chỉ hai bước **Chờ xác nhận** và **Đã xác nhận** được tự hủy. Từ **Chuẩn bị hàng / Chờ đơn vị vận chuyển tới lấy hàng** trở đi bị chặn, dù trạng thái tổng của hóa đơn vẫn là XAC_NHAN. Trạng thái không nhận biết không được tự hủy.
- Đơn đã thanh toán hoặc đã lấy hàng cần cửa hàng hỗ trợ; không tự hoàn tiền.
- Nếu đã chuyển khoản hoặc đang trả tiền qua cổng, khách được nhắc liên hệ cửa hàng trước khi hủy.

## Database và WinForms

Giữ `public.hoadon`, `public.chitiethoadon` và snapshot. Đổi loại đơn thành **WEB_HUY**, giao hàng **Đã hủy**, số tiền COD cần thu bằng 0; lưu dấu hủy trong `webstore.order_cancellation`. Các hàng đợi giao/COD hiện có trên bản WinForms đã đối chiếu không lấy loại WEB_HUY nữa. Hóa đơn vẫn tra cứu được trong database; nếu partner cần tab đơn hủy, truy vấn loại WEB_HUY. Các báo cáo thu tiền chỉ tính đơn đã thanh toán như quy trình hiện có.

Đơn đủ điều kiện chưa từng trừ kho/cộng điểm, nên hủy không hoàn kho hay điều chỉnh điểm. Bấm lặp chỉ ghi một lần. Hàm hủy khóa cùng dòng hóa đơn với xác nhận thanh toán: tiền hoặc hủy thắng trước sẽ quyết định kết quả.

Backend giữ khóa dòng hóa đơn từ lúc kiểm tra bước giao hàng đến khi hoàn tất hủy. Nếu WinForms cập nhật sang chờ lấy hàng trước, yêu cầu hủy bị từ chối; nếu hủy trước, trigger bảo vệ đơn WEB_HUY ngăn WinForms tiếp tục giao.

File SQL có trigger bảo vệ riêng đơn WEB_HUY. Điều này cần thiết vì bản WinForms cũ đọc trạng thái trước khi cập nhật hoặc xóa: thao tác từ màn hình cũ sẽ bị từ chối sau khi website đã hủy, yêu cầu tải lại dữ liệu. Những loại hóa đơn khác vẫn giữ cách cập nhật/xóa hiện có.

Nếu cổng thanh toán báo nhận tiền sau khi khách đã hủy, biên nhận được giữ ở REVIEW, đơn cần cửa hàng xử lý, không giao lại, không trừ kho/cộng điểm. Nhân viên phải đối soát và hoàn tiền theo quy trình của cửa hàng. Chưa có hoàn tiền tự động.

Kiểm tra chỉ đọc:

```sql
SELECT c.sohd,c.cancelledat AT TIME ZONE 'Asia/Ho_Chi_Minh' AS gio_huy_viet_nam,
       h.loaihoadon,h.trangthaigiaohang,h.trangthaithanhtoan
FROM webstore.order_cancellation c LEFT JOIN public.hoadon h USING(sohd)
ORDER BY c.cancelledat DESC LIMIT 30;
```
