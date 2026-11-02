# Lý do hủy và cập nhật đơn hiện tại

Ngày cập nhật: 03/11/2026.

- Form hủy yêu cầu chọn một trong bốn lý do: không còn nhu cầu, thay đổi địa chỉ nhận hàng, thay đổi phương thức thanh toán, lý do khác. Lý do khác yêu cầu nội dung 5–500 ký tự. Áp dụng cả COD; thông tin nhận hoàn tiền vẫn được yêu cầu cho đơn đã thanh toán theo luồng cũ.
- Chọn địa chỉ/thanh toán hiện lời mời cập nhật mà không hủy. Nút tương ứng mở `/Orders/Update/{id}?section=delivery` hoặc `section=payment`. Khách vẫn có thể chọn **Tiếp tục hủy đơn**. Chọn lý do hoặc mở trang cập nhật không tự hủy đơn.
- Trang cập nhật dùng bố cục Checkout, dữ liệu lấy từ đơn đã mua, nút **Cập nhật thông tin**. Không dùng giỏ hàng để tạo đơn mới; không sửa sản phẩm, số lượng, giá, ưu đãi, tồn kho, điểm hoặc hồ sơ địa chỉ mặc định. Cập nhật xong về chi tiết cùng mã đơn.
- Sửa người nhận/số điện thoại/địa chỉ khi đơn website còn ở bước chờ xác nhận hoặc đã xác nhận, trước chuẩn bị/giao hàng. Đơn đã thanh toán vẫn có thể sửa địa chỉ nếu còn ở giai đoạn này. Địa chỉ được cập nhật đồng thời tại `public.order_snapshot` và các trường nhận hàng của `public.hoadon` để form quản lý đọc cùng dữ liệu.
- Đổi thanh toán chỉ cho đơn chưa thanh toán, chưa nhận tiền, chưa chuẩn bị/giao và chưa có bất kỳ `gateway_attempt` nào. Giao dịch online đã khởi tạo, kể cả chờ/hết hạn, cần cửa hàng hỗ trợ vì kết quả thanh toán có thể tới muộn. Phương thức mới phải được cấu hình/bật. Khi đổi, hóa đơn về hàng đợi chờ xác nhận phù hợp (`COD` hoặc `WEB_CHO_THANH_TOAN`); tiền COD được cập nhật, tổng tiền giữ nguyên. Chọn online chỉ đổi phương thức, khách tiếp tục thanh toán từ chi tiết đơn.
- Backend đọc quyền sửa theo đơn của khách, trả `CanUpdateDelivery`, `CanUpdatePayment`, `EditVersion`, `SupportsBankTransfer` và thông báo nếu phương thức bị khóa. `PUT /api/orders/{id}/information` chỉ nhận section/version và thông tin tương ứng; không nhận mã khách hay số tiền từ trình duyệt.
- Khóa hóa đơn giống luồng thanh toán/hủy; sau khi có khóa mới đọc điều kiện bằng snapshot truy vấn mới. Phiên bản được tính từ `xmin` của hóa đơn/snapshot và sự tồn tại giao dịch online. Form cũ bị từ chối khi dữ liệu thay đổi; trang giữ dữ liệu nhập và có liên kết tải lại. Endpoint có xác thực Customer, MVC POST có anti-forgery. SQL Server cũ chưa bật cập nhật đơn, nên không hiện lựa chọn cập nhật khả dụng ở backend đó.

Không có bảng/schema/migration mới. Khởi động lại backend bằng lệnh Supabase hiện dùng để nạp API mới, sau đó chạy lại web nếu cần:

```powershell
.\scripts\Start-SupabaseCatalog.ps1 -EnableOrders -OrderEmployeeId "NV-001" -EnableBankTransfer
```

## Kiểm tra

- Backend build: 0 lỗi, 0 cảnh báo. MVC build với `MvcBuildViews=true`: đạt.
- Bộ kiểm thử PostgreSQL localhost: 204 đạt, 15 kiểm thử SQL Server được bỏ qua. Không dùng Supabase thật để sửa/hủy đơn thử.
- `OrderInformationIntegrationTests`: chặn người khác/chưa đăng nhập; đồng bộ địa chỉ; giữ cùng mã đơn/sản phẩm/số tiền/tồn kho; chặn form cũ, đã chuẩn bị/giao/hủy; sửa địa chỉ đơn đã trả tiền; chặn đổi phương thức đã trả tiền/chưa cấu hình/đã tạo giao dịch. Kiểm tra trường hợp khởi tạo giao dịch giữ khóa hóa đơn trước khi yêu cầu đổi phương thức tới.
- `CustomerMvcClientTests`: chỉ gửi các trường được phép sửa; không gửi dữ liệu đơn hoặc tổng tiền từ view model.
- `node verification/Verify-OrderCancellation.js`: bốn lý do, lời mời cập nhật, nhập lý do khác, không tự gửi hủy khi chọn lý do, khóa nút sau khi gửi.
- HTTP qua IIS Express riêng/API giả: bốn lý do lưu đúng cả COD; chặn lý do thiếu/quá ngắn; cập nhật địa chỉ và thanh toán giữ nguyên đơn, không tạo/hủy đơn mới; chặn form cũ/đơn không còn sửa được; giữ biểu mẫu hoàn tiền cho đơn đã trả tiền. Kết quả cục bộ: `tmp/order-update-runtime-results.json`.
- Chưa kiểm tra trực quan trên trình duyệt vì local browser use đang tắt trong Work settings.
