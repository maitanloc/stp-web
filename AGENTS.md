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
  `StpTeam` **24 giờ sliding** (đổi từ 30 ngày theo yêu cầu "ghi nhớ 24h,
  khỏi phải vào"). JWT developer giữ nguyên song song.
- Default authenticate scheme là JWT → đọc identity nhóm **bắt buộc**
  `await StpTeamIdentity.FromAsync(HttpContext)`, không đọc `HttpContext.User`
  trực tiếp (nhầm JWT trống).
- Người đã đăng nhập: chat/presence/audit ghi DisplayName từ tài khoản, bỏ qua
  tên client gửi (không giả danh được).
- Khoá bắt buộc đăng nhập: appsettings `StpTeam:RequireLogin` (mặc định false).
  Gate UI hiện đã chặn mọi người chưa có phiên; flag này là khoá cứng server.
- **Màn ra vào "cuốn hồ sơ" (vòng 2, cùng ngày)**: chưa có phiên → AuthGate
  full-screen, mặt trước Đăng nhập, lật 3D sang mặt sau Tự đăng ký
  (`POST /api/auth/register`, role member, cấp cookie luôn; tắt bằng
  `StpTeam:AllowRegistration=false`). Token dev bỏ qua gate.
- **Màn chờ = cuốn hồ sơ lật từng trang** (BootSplash): tờ hồ sơ xoay 3D
  quanh gáy trái, z-index 45→60→5 trong keyframes để trang bay trên đống trang
  đã nằm; 2.6s lần đầu / 1.1s khi quay lại. Sau đó thẻ WelcomeRecap tổng kết
  việc của chính người đó từ `GET /api/auth/recap` (AuditLogs lọc Actor ==
  DisplayName, loại auth.*). Lưu ý: chuỗi `v-else-if` sau `<Transition>` làm
  Vue compiler crash — mỗi nhánh template tự mang `v-if`.
- **Ô mật khẩu có nút con mắt** (PasswordField): mặc định mắt nhắm, bấm mở
  animation như mắt người (scaleY 0.1→1) + hiện chữ thường; gắn cả 2 mặt
  AuthGate. Mật khẩu chuẩn hoá NFC trước validate/hash (login/register/admin
  CRUD) và **bị chặn ngay lúc tạo nếu có ký tự có dấu** (>127) — thông báo tắt
  bộ gõ Telex/VNI; đảm bảo mật khẩu ASCII luôn đăng nhập được ("trừ nhập
  dấu"). Tests trong `AccountRulesTests`.
- Admin quản lý tài khoản tại `/quan-ly`; seed lần đầu `admin / stp2026`
  (đổi ngay). Mọi thao tác admin ghi `AuditLogs`.
- Link cố định duy nhất: `https://mangle-tastiness-french.ngrok-free.dev`
  (ngrok free dev domain — không đổi khi restart; fetch client kèm
  `ngrok-skip-browser-warning`). trycloudflare đã bỏ.
- Hệ thống chạy Windows Service (WinSW, `service/` không commit):
  `StpApiService` + `StpNgrokService` — tự bật khi máy khởi động, gãy tự
  restart. Update code: `Stop-Service StpApiService` → build → `Start-Service
  StpApiService`; không chạy `dotnet run` tay song song với service.
  **`dotnet test` cũng phải chạy khi service đã dừng** (service khoá exe/DLL
  của Stp.Api → MSB3021/MSB3027).
- Chi tiết: `docs/phan-tich-ux-trich-yeu.md` mục 8.7, 8.8, 8.9, 8.10.

## Plan chờ duyệt (CHƯA làm — người dùng chốt 11/11/2026 "để đó ghi nhớ")
- **Nhập trích yếu siêu tốc**: plan đầy đủ tại
  `docs/ke-hoach-trich-yeu-sieu-toc.md` — GĐ 0 (chip 1-chạm, "như dòng trên",
  bộ mặc định theo loại, chạy hàng tự nhảy, xuất 1 click), GĐ 1 (voice Web
  Speech API miễn phí: phiên đọc liên tục "nghe lọc loại hồ sơ bỏ vô", parser
  thuần frontend dùng catalog sẵn có, chỉ prefill để duyệt), GĐ 2/3 (Whisper
  server, OCR camera, bot chat). **Đừng tự tiện bắt đầu** — chờ người dùng
  ra lệnh làm giai đoạn nào thì làm giai đoạn đó.
- **Workspace 2.0** (brainstorm 11/11/2026, vòng 1+2 cùng ngày — cũng CHƯA
  làm): toàn bộ suy nghĩ "Google Sheets cho hồ sơ" tại
  `docs/brainstorm-workspace-2-0.md` — 7 trụ: A presence 2.0 (SignalR +
  cursors viền + cục tròn avatar + chip hành động), B avatar kiểu Google
  Sheets mặc định (initials+color, đang có) + **upload ảnh riêng** (không
  emoji), C notification **2 kênh** (hệ thống + tin nhắn kiểu Messenger:
  typing/seen/optimistic, emoji chỉ trong chat), D bảng sống kho hồ sơ
  (virtualized + comment + history hover), E sidebar/topbar mở rộng
  (registry + badge + Ctrl+K chung với GĐ 1.5 plan trên), F hỗ trợ (tour,
  cheat sheet), G **lazy loading toàn bộ**. Chốt của chủ: KHÔNG dùng emoji
  trong UI (chỉ `@lucide/vue`), motion mới phải bám 4 curve nhà (decel
  0.22/0.9/0.26/1 · overshoot 0.3/0.7/0.35/1.25 · snap 0.34/0.06/0.22/1 ·
  ease). Kết luận: viền row-mark + avatar initials + dark mode ĐÃ CÓ SẴN —
  thiếu realtime (SignalR chưa có, polling thôi) + modal/route transition +
  reduced-motion.
- **Data safety** (phân tích vòng 3, 11/11/2026 — CHƯA sửa code): toàn bộ
  tình huống lưu/mất dữ liệu tại `docs/phan-tich-luu-du-lieu-nguoi-dung.md`.
  P0 bắt buộc làm đầu tiên khi được duyệt: (1) **migrations KHÔNG tự apply**
  — không có `Database.Migrate()` trong repo, thêm cột mới mà quên áp tay →
  API vỡ lúc runtime; cần `MigrateAsync()` ở startup; (2) edits trích yếu chỉ
  sống trong localStorage — đề xuất draft backup server theo session 12h.
  P1: chat >100 tin không đọc lại được (cần cursor pagination khi làm
  Messenger), unread reset khi reload (seen phải lên server), undo/redo chết
  khi reload (chấp nhận, draft đã cứu net edits). P2: job upload/output/backup
  dirs không bao giờ dọn (sweep 30 ngày), history localStorage tích luỹ.
  Checklist 6 câu bắt buộc cho mọi tính năng mới: reload? restart? hết hạn?
  đổi máy? 2 người cùng lúc? quota full? **Cả 2 plan chờ người dùng ra lệnh,
  không tự làm.**
