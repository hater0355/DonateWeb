# Áp dụng database migrations an toàn

Ứng dụng DonateWeb chỉ kiểm tra migration history khi khởi động; ứng dụng không tự sửa schema. Các script ở thư mục này cần được xem xét và chạy riêng.

## Database mới

1. Tạo database rỗng và sao lưu/cấu hình kết nối theo quy trình của máy chủ.
2. Mở `upgrade-idempotent.sql`, kiểm tra migration ID và nội dung DDL.
3. Chạy script trên đúng database. Có thể dùng `sqlcmd -S <server> -d <database> -U <user> -C -b -i scripts/database/upgrade-idempotent.sql`; bỏ `-P` để không ghi mật khẩu vào câu lệnh và nhập khi được hỏi.
4. Khởi động web và xác nhận `/health/ready` trả HTTP 200.

## Database cũ đang có dữ liệu

1. Tạo bản backup SQL Server và xác nhận có thể khôi phục backup.
2. Xuất schema-only từ VPS, không gồm dữ liệu người dùng, CCCD, giao dịch hay secrets. So sánh tất cả bảng, cột, kiểu dữ liệu, nullability, default, index và khóa ngoại với `upgrade-idempotent.sql`. Không chạy file schema export: SSMS có thể đưa vào đó lệnh `CREATE USER`.
3. Schema VPS đã xuất ngày 21/10/2026 được đối chiếu với migration hiện tại: 25 bảng, định nghĩa cột và 28 khóa ngoại khớp về cấu trúc/hành vi. Database có thêm default constraints, hai check constraints và index hết hạn nạp tiền; chúng được giữ nguyên. Có ba index cùng tác dụng nhưng khác tên. Thiếu hai index `IX_ShopOrders_DiscountCouponId` và `IX_StreamerEffectActivations_SpecialEffectCampaignId`; script baseline sẽ tạo hai index này (nếu chưa có index tương đương) rồi mới ghi migration ID. Không có câu lệnh sửa dữ liệu nghiệp vụ.
4. File baseline được khóa để chỉ chạy trên database `DonateWebDB` mà bạn vừa xuất và backup đã kiểm tra. Nếu tên database hiện tại khác, script sẽ dừng trước khi sửa schema.
5. Chạy baseline trên đúng database cũ. Script kiểm tra bảng/cột, dừng nếu index trùng tên có cấu trúc bất ngờ, chỉ thêm hai index còn thiếu rồi mới ghi migration history. Nếu thiếu bảng/cột hoặc đã có migration history khác, dừng lại và không sửa/xóa history thủ công.
6. Chạy `upgrade-idempotent.sql` sau baseline để kiểm tra trạng thái; script sẽ nhận migration đã được đánh dấu và không tạo lại bảng.
7. Khởi động phiên bản web mới; xác nhận `/health/ready`, đăng nhập, ví, lịch sử giao dịch và trang quản trị. Giữ backup tới khi hoàn tất kiểm tra.

## Khôi phục

Không tự động downgrade migration nền. Nếu triển khai lỗi, dừng web, khôi phục SQL backup cùng các volume `uploaded-images`, `private-identity-documents` và `dataprotection-keys`, sau đó chạy lại phiên bản ứng dụng trước.

## Test migrations

Các test `SqlServerFact` tạo database ngẫu nhiên và xóa nó sau khi chạy. Chỉ đặt `DONATEWEB_TEST_SQLSERVER` thành connection string của SQL Server thử nghiệm có quyền tạo/xóa database. Không dùng connection string production. Integration tests cần chạy để xác nhận cả database mới lẫn đường baseline legacy.
