# Quy tắc dự án (người dùng xác nhận — ghi nhớ)

## Sheet dữ liệu
- **Chỉ lấy sheet "file nhập"** làm sheet dữ liệu trích yếu.
- Không lấy sheet "file dán" (dữ liệu đã dán sạch nhưng cột B không khớp
  cách người dùng nhìn file), không lấy sheet in theo năm (`1990`, `1994 `...)
  hay sheet `form`.
- Chỉ khi file không có sheet `file nhập` mới lấy sheet `ML`, rồi mới lấy
  sheet đầu tiên có tiêu đề "Trích yếu nội dung".

## Ý nghĩa cột trong file DK KET HON (người dùng xác nhận)
- Cột B = hồ sơ số (số lọc lấy phần đầu dạng số, ví dụ "36A" tính là 36).
- Cột D = trích yếu nội dung.
- Cột E = Thời gian tài liệu.
- Cột F = số tờ (thực chất là ký hiệu QĐ, ví dụ 93/QD.UBT).
- Cột G = ghi chú (thực chất là số/khoảng trang, ví dụ 01-20).

Note: trong file thật, tiêu đề "Hồ sơ số" nằm ở cột C và B không có tiêu đề,
nhưng người dùng làm việc theo cột B. Bộ lọc số khớp cả B lẫn cột
"Hồ sơ số" cho đúng mọi kiểu file.

## Số tờ (cột G)
- Ở sheet "file nhập", người dùng gõ "01-24" và Excel/WPS lưu thành ngày
  01/01/2024 (serial 45292). Ứng dụng hiển thị lại dạng "MM-yy" (ví dụ
  "01-24", "01-20") thay vì số serial thô.

## Lọc "Từ số – Đến số" + năm
- Số hồ sơ + điều kiện năm kết hợp với nhau (AND).
- Kiểm chứng với `DK KET HON (4).xlsx` qua API thật: lọc 775–785 +
  năm 1994 phải ra đúng 11 dòng ở dòng 764–774 của sheet `file nhập`.
- Không đổi cách chọn sheet trở lại `file dán` nếu chưa kiểm bằng file thật.

## Danh mục học được (công cụ trích yếu — người dùng xác nhận 2026-11-10)
- **Mục giấy tờ người dùng tự gõ = tự lưu database ngay**: gõ Enter thì vừa
  thêm chip vào dòng vừa `POST /api/summary/catalog/items` (bảng `CatalogItems`).
  Không có kịch bản "thêm vào session rồi mất" — không thêm nút/bước lưu riêng.
- Không ép cứng bộ 7 chuẩn: chỉ là nút gợi ý "Bộ 7 chuẩn" trong bulk bar, người
  dùng có thể tự chọn; áp hàng loạt phải qua preview người dùng xác nhận.
- Thiết kế nhận định thống kê: 92,4% hồ sơ là "Đăng ký kết hôn" (gấp ~19 lần loại
  kế tiếp); bộ 7 giấy tờ chiếm 98,3% hồ sơ có danh sách — mọi gợi ý mặc định
  xoay quanh nhóm này.
- Mục năng động thứ tự hiển thị theo tần suất thật (`UsageCount`): item hay gặp
  ("giấy khai sinh" ×21.102) đứng đầu picker với badge ×N.

## Nền tảng nhóm (người dùng xác nhận "Cả 3 luôn" 2026-11-10)
- Tên người dùng tự khai 1 lần (`localStorage stp.user.name`), mọi request kèm
  header `X-Stp-User`; tên tiếng Việt phải `encodeURIComponent` phía client và
  backend `Uri.UnescapeDataString()` — header HTTP chỉ nhận Latin-1, trả nguyên
  văn sẽ văng lỗi fetch (đã thực tế gặp với tên "lộc").
- Chat tổng lưu bảng `ChatMessages`; Nhật ký hoạt động dùng bảng `AuditLogs`
  có sẵn, writer fire-and-forget (lỗi ghi không được làm rớt thao tác thật).
- Presence chỉ in-memory (`TeamPresence` singleton): TTL online 75s, dấu dòng
  65s; client tick 3 giây, ping 8s khi đang sửa dòng / 20s idle; gọi ngay khi
  mở/đóng bảng sửa.
- Hai người sửa cùng 1 dòng v1 chỉ banner cảnh báo, chưa khoá cứng.
- Chi tiết đầy đủ: `docs/phan-tich-ux-trich-yeu.md` mục 8.
- Biến thể không dấu ("GIAY TAM TRU CUA VO") luôn gộp về canonical có dấu qua
  `CatalogKey.Normalize` — nhưng **không sửa lỗi chính tả** ("đơn xi" ≠ "đơn xin").
- Seed từ Kho mặc định nạp mục tần suất ≥50 (17 mục Learned). Band 2–49 đã
  nạp lần đầu (23 mục kèm lọc từ khóa giấy tờ `LabelKeywords`); 5 mục rác
  thuật toán bị nhận ra seed đã soft-disable (IsDisabled, còn trong DB);
  count ≤1 là nhiễu (tên người) không nạp. Chạy lại seed idempotent.

## Tài khoản nhóm + link cố định (người dùng chốt 2026-11-11)
- Mỗi người một tài khoản: bảng `StpUsers` (migration `AddStpUsers`), mật khẩu
  hash PBKDF2 qua `PasswordHasher<StpUser>`, cookie `.stp.team` scheme
  `StpTeam` 30 ngày sliding. JWT developer giữ nguyên song song.
- Default authenticate scheme là JWT → đọc identity nhóm **bắt buộc**
  `await StpTeamIdentity.FromAsync(HttpContext)`, không đọc `HttpContext.User`
  trực tiếp (nhầm JWT trống).
- Người đã đăng nhập: chat/presence/audit ghi DisplayName từ tài khoản, bỏ qua
  tên client gửi (không giả danh được). Chưa đăng nhập: vẫn tự khai tên như cũ.
- Khoá bắt buộc đăng nhập: appsettings `StpTeam:RequireLogin` (mặc định false —
  không gián đoạn ca nhập; bật sau khi tạo đủ tài khoản).
- Admin quản lý tài khoản tại `/quan-ly`; seed lần đầu `admin / stp2026`
  (đổi ngay). Mọi thao tác admin ghi `AuditLogs`.
- Link cố định duy nhất: `https://mangle-tastiness-french.ngrok-free.dev`
  (ngrok free dev domain — không đổi khi restart; fetch client kèm
  `ngrok-skip-browser-warning`). trycloudflare đã bỏ.
- Hệ thống chạy Windows Service (WinSW, `service/` không commit):
  `StpApiService` + `StpNgrokService` — tự bật khi máy khởi động, gãy tự
  restart. Update code: `Stop-Service StpApiService` → build → `Start-Service
  StpApiService`; không chạy `dotnet run` tay song song với service.
- Chi tiết: `docs/phan-tich-ux-trich-yeu.md` mục 8.7, 8.8.
