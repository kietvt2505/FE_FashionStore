# Thống nhất website và partner trong schema public

**Cập nhật 03/11/2026:** sau khi partner xóa các bảng tiếng Anh, source hiện dùng tên bảng tiếng Việt và dùng trực tiếp cấu trúc khách hàng/sản phẩm hiện tại. Xem [VIETNAMESE_SCHEMA_SETUP.md](VIETNAMESE_SCHEMA_SETUP.md). Nội dung dưới đây giữ lịch sử lần chuyển schema ngày 02/11.

## Phạm vi

Bản source này dùng `public` cho toàn bộ bảng, view, sequence và hàm Supabase
của website. Cùng dùng một cấu hình kết nối `DatabaseMode.SupabaseConnection`;
không đổi Supabase project, database, mật khẩu hoặc schema mặc định của partner.

Các script `Supabase-*.sql` cài mới đã trỏ về `public`. Script cài tài khoản/giỏ
hàng không thu hồi quyền toàn schema public hoặc toàn bộ bảng/sequence của
partner; chỉ áp dụng quyền cho các đối tượng website cụ thể.

## Database đã có webstore

1. Dừng backend website cũ trong lúc chuyển. Form có thể tiếp tục giữ kết nối,
   nhưng nên tạm ngừng thao tác ghi trong thời gian ngắn để tránh lock timeout.
2. Kiểm tra đúng project Supabase. Bản đã kiểm kê: `Khoa Luan Cu Nhan`, ref
   `oagcfzcslkkiokbmkrlg`, database `postgres`.
3. Chạy `database/Supabase-Move-Webstore-To-Public.sql` bằng role chủ database.
   Không chạy lại seed hoặc các script tạo bảng thay cho migration này.
4. Script chạy trong một transaction, kiểm tra trùng tên rồi chuyển cùng các
   object OID, giữ dữ liệu, FK, index, trigger và trạng thái sequence. Nó sửa các
   hàm còn nhắc `webstore`, kiểm tra số dòng/checksum và RLS trước khi thực hiện
   `DROP SCHEMA webstore RESTRICT`. Không có `DROP ... CASCADE`.
5. Journal `public.web_schema_migration_20261102` giữ định nghĩa hàm gốc, object
   OID, quyền ban đầu, số dòng và checksum. Journal không sao chép nội dung hàng
   của khách. Giữ journal phục vụ đối chiếu và rollback.
6. Chạy backend đã build từ source mới. Kiểm tra tài khoản, giỏ, catalog, đơn,
   hủy đơn, đánh giá, yêu cầu đổi/trả và trạng thái thanh toán. Không tạo giao
   dịch hoặc gọi hoàn tiền thật chỉ để kiểm thử.

Script dừng và rollback toàn transaction khi gặp trùng tên/đối tượng chưa hỗ trợ,
thiếu bảng partner, lock timeout, dữ liệu không khớp hoặc schema chưa rỗng.
Không ghép/ghi đè các bảng public cùng tên. Chạy lại sau khi chuyển thành công
là no-op; nếu đã rollback, cần kiểm tra journal trước khi chuyển lần nữa.

## Quyền truy cập

Các bảng website giữ RLS hiện có. Migration thu hồi quyền của PUBLIC, anon và
authenticated trên các đối tượng website vừa chuyển; không đổi quyền bảng gốc
của partner. Các hàm được chuyển cũng không tự trở thành API công khai.
Backend tiếp tục dùng kết nối PostgreSQL được cấu hình, không dùng Supabase
anon key từ trình duyệt để đọc tài khoản, reset token hoặc thông tin hoàn tiền.

## Rollback

Dừng backend mới, chạy `database/Supabase-Move-Webstore-To-Public-Rollback.sql`
rồi dùng lại source cũ. Script dùng journal để chuyển đúng các object OID về
webstore, phục hồi thân hàm và quyền ban đầu. Những bản ghi được ghi thêm sau
chuyển đổi vẫn được giữ; không phục hồi database bằng cách xóa dữ liệu mới.
Journal vẫn ở public sau rollback để kiểm tra, không tự xóa dấu vết migration.

## Đổi/trả và các công việc đang tạm dừng

Di chuyển schema giữ nguyên nghiệp vụ: yêu cầu chờ duyệt nằm ở
`public.return_request`, phiếu đã xử lý của partner nằm ở `public.phieudoitra`
và `public.chitietdoitra`. Form đang chỉ đọc phiếu HOAN_TAT; việc bổ sung màn
duyệt/liên kết yêu cầu vào phiếu là công việc tích hợp riêng, không được tự
đánh dấu yêu cầu chờ duyệt là hoàn tất khi chuyển schema.

Thay đổi hiển thị đánh giá ngay dưới chi tiết sản phẩm vẫn đang tạm dừng theo
yêu cầu trước đó; migration này chỉ cập nhật schema cho luồng đánh giá hiện có.

## Kiểm thử

Kiểm thử PostgreSQL chỉ chạy trên localhost với database `fashionstore_test`.
`PublicSchemaMigrationTests` tạo database test riêng, kiểm tra chuyển/rollback,
va chạm tên, giữ OID, dữ liệu, identity, sequence, enum, view/materialized view,
FK, RLS, hàm/trigger và quyền. `PublicSchemaMigrationScenarios` dựng layout
legacy từ toàn bộ bộ dữ liệu nghiệp vụ test rồi chạy migration thật và đọc các
API tài khoản, catalog, giỏ, đơn, review, địa chỉ, wishlist, đổi/trả, khuyến mãi.

Tài liệu PostgreSQL: https://www.postgresql.org/docs/current/sql-altertable.html
và https://www.postgresql.org/docs/current/ddl-depend.html.

## Kết quả áp dụng ngày 02/11/2026

- Đã được người dùng xác nhận thực thi trên project `Khoa Luan Cu Nhan`.
- Kiểm kê trước chuyển: 21 bảng, 3 view, 6 sequence, 13 hàm; không trùng tên
  relation/routine với public, chưa có journal migration.
- Đã chạy migration thật qua Supabase SQL Editor. Kết quả trả về:
  `database_name=postgres`, `old_schema_removed=true`, `accounts_in_public=true`,
  `returns_in_public=true`. Các bước so khớp số dòng/checksum/RLS nằm trước
  DROP SCHEMA trong cùng transaction, nên chỉ commit sau khi kiểm tra đạt.
- Source backend đã build thành công, 0 cảnh báo/0 lỗi.
- PostgreSQL tạm: 197 kiểm thử đạt, 15 SQL Server integration test được bỏ qua.
  Đã kiểm thử đầy đủ layout legacy có dữ liệu rồi chuyển lại public và đọc API.
- Hậu kiểm thật đã đạt: toàn bộ object đã chuyển public, không còn hàm tham
  chiếu webstore, RLS được giữ nguyên; không có quyền PUBLIC trên các relation
  đã chuyển và không có quyền PUBLIC EXECUTE trên các hàm đã chuyển.
- Backend mới đang chạy tại http://127.0.0.1:5000; health/ready trả
  database-connected. Catalog SP-092, reviews và promotions trả 200; API hồ sơ,
  giỏ và yêu cầu đổi/trả vẫn trả 401 khi chưa đăng nhập.
- IIS Express đang chạy web chính http://localhost:58472. Đã đọc HTTP 200 và
  kiểm tra trên trình duyệt trang /Product?q=SP-092 hiển thị sản phẩm test 123.
  Đây là site chính, không dùng API giả lập ở các cổng preview trước đây.
- Chuỗi kết nối được người dùng nhập kín ở PowerShell, không lưu trong báo
  cáo/source. Giữ cửa sổ backend đang chạy để phục vụ website.
- Minh chứng: evidence/schema-public-live-preflight.json,
  evidence/schema-public-live-verification.json, evidence/schema-public-live-api.json,
  evidence/schema-public-tests.log, evidence/schema-public-tests.trx,
  evidence/schema-public-sp092-live.png.
