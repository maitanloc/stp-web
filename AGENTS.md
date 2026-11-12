# Quy tắc dự án (người dùng xác nhận — ghi nhớ)

## Sheet dữ liệu (Luật import nhiều sheet theo năm — người dùng chốt 11/11/2026)
- **Năm 1990–1995**: Dữ liệu nằm ở sheet **"file nhập"**.
- **Từ năm 1996 trở đi**: Lấy **sheet mang tên năm** (ví dụ "1996", "1997", "1998", "1999"...) — **TUYỆT ĐỐI không lấy sheet "file nhập" cho các năm ≥1996 khi file có sheet năm**.
- "file nhập" chỉ giữ các dòng có `Year == null` hoặc năm không được bất kỳ sheet năm nào phủ.
- Sheet "file dán", "form", hoặc sheet có K1="BANG_IN_HANG_LOAT" tự động bị loại bỏ.
- Trường hợp không có sheet năm nào chứa dòng trích yếu hợp lệ: fallback hành vi cũ 1 sheet.

## Ý nghĩa cột trong file DK KET HON (người dùng xác nhận)
- **Đối với sheet "file nhập"**:
  - Cột B = hồ sơ số (số lọc lấy phần đầu dạng số, ví dụ "36A" tính là 36).
  - Cột D = trích yếu nội dung.
  - Cột E = Thời gian tài liệu.
  - Cột F = số tờ (thực chất là ký hiệu QĐ, ví dụ 93/QD.UBT).
  - Cột G = ghi chú (thực chất là số/khoảng trang, ví dụ 01-20).
- **Quy ước cột CỐ ĐỊNH cho sheet năm (người dùng chốt 11/11/2026 — nhớ mãi)**:
  - Tiêu đề cột trong bảng bị SAI nên **không tin tên cột**.
  - Ánh xạ cố định theo vị trí cột:
    - **Cột B**: Hồ sơ số.
    - **Cột D**: Trích yếu nội dung.
    - **Cột E**: Thời gian tài liệu.
    - **Cột F**: Ghi chú (bỏ qua).
    - **Cột G**: Số tờ (lấy làm SheetNumber).
    - Cột A và C là cột phụ, không dùng.

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

## Trạng thái thực hiện Plan (ĐÃ HOÀN THÀNH 12/11/2026)
- **Nhập trích yếu siêu tốc**:
  - GĐ 0 (C1–C5, commit `5439c55`): Chip 1-chạm trong editor dòng, phím `C` ("như dòng trên"), bộ mặc định theo loại + nút "Áp dụng cho dòng lọc còn thiếu", chạy hàng tự nhảy dòng thiếu (C4), xuất nhanh 1 click (C5, nhớ tên + phạm vi).
  - GĐ 1 (I1–I2, commit `65c378d`): Mic Web Speech API (`webkitSpeechRecognition`), parser catalog vi-VN chuẩn hóa, phiên đọc liên tục, lệnh miệng ("xong", "kế tiếp", "bộ bảy", "lọc..."), học lỗi nghe tự động vào localStorage.
  - GĐ 2 (Whisper server) và GĐ 3 (OCR camera, bot chat): Bỏ qua theo đúng thỏa thuận "chỉ làm khi cần".
- **Workspace 2.0**:
  - Pha 0 (Nhóm B, commit `e4ca10c` + `f72cc54`): Modal & route transition, prefers-reduced-motion toàn cục, lazy fetch cho dock panel.
  - Trụ A - Presence 2.0 (Nhóm F, commit `3c6a5c6`): SignalR `/hubs/team` + hub-first ping, cursors viền + cục tròn avatar theo focus (cap 8), chip hành động đáng kể realtime (bulk, export...).
  - Trụ B - Avatar (Nhóm D `31f3bd2` & Nhóm K `4ba3e8c`): `renderAvatar` chung với palette 14 màu Google Workspace, pipeline upload avatar cắt client 96×96 webp -> `POST /api/team/avatar` -> cache ETag version -> broadcast hub realtime.
  - Trụ C - Notification & Messenger (Nhóm G, commit `7190345`): Bảng `Notifications`, Bell topbar 2 tab (Hệ thống / Tin nhắn), chat seen server-side (`LastReadChatAt`), typing indicator 3-dot bounce, optimistic message, cursor pagination `?before=`, mention @tên, emoji picker 24 ô trong chat (emoji chỉ ở chat, cấm ở UI ngoài). Riêng react message bỏ qua theo thỏa thuận "chỉ làm khi cần".
  - Trụ D - Bảng sống kho hồ sơ (Nhóm J, commit `8abd843`): `DossierLiveGrid.vue` virtualized 67k dòng (ROW_H 44px, fetch windowed), filter share realtime ("Áp dụng theo"), cursor chip đồng đội, comment theo dòng (`DossierComments` + composer + realtime broadcast), history hover giá rẻ.
  - Trụ E - Shell (Nhóm H, commit `2d642dd`): Tool registry 8 tool, badge động sidebar, ghim tool ưa thích, Ctrl+K command palette tìm kiếm hồ sơ và chạy lệnh.
  - Trụ G - Lazy loading & Data safety (Nhóm L, commit `4a6c39f`): Sweep artifacts 30 ngày (`IFileStorage.SweepOldArtifacts`), dọn dẹp localStorage hết hạn lúc boot app.
- **Trích yếu Multiplayer (Google Sheets cho toàn phòng - Nhóm M)**:
  - Active Team Session: Admin tải file lên, cả phòng tự động nạp chung đúng 1 file mà không ai phải import lại; chuông rung báo toàn phòng + banner chỉ dẫn tại Kho hồ sơ.
  - Realtime Multiplayer: Con trỏ ô đổi màu theo từng cộng tác viên + tag tên nổi bật; chữ gõ chạy thời gian thực (`SummaryCellTypingEvent`); khi lưu ô/áp chip thì ô chớp sáng xanh dịu (`is-remote-flashing`).
  - Shared Edits Store: Lưu tập trung tại `SummaryToolService._sessionEdits` trong RAM server (Singleton). Người vào sau hoặc F5 đều lấy đủ 100% sửa đổi của đồng đội qua `GET /api/summary/{sessionId}/edits`.
  - Nạp thẳng vào Kho hồ sơ: 1-click `POST /api/summary/{sessionId}/sync-to-archive` nạp trực tiếp toàn bộ dữ liệu file + shared edits vào bảng `Dossiers` CSDL SQL Server (tái sử dụng `IArchiveSyncService.ApplyAsync`), ghi `AuditLogs` và thông báo toàn phòng.
- **Data safety (P0, P1, P2)**:
  - P0.1 (Nhóm A2, commit `fb1226c`): `MigrateAsync()` tự động lúc startup + health báo trạng thái migrations.
  - P0.2 (Nhóm E1, commit `d72f99c`): Bảng `SummaryDrafts` backup draft lên server theo `(SessionId, UserName)`, tự phục hồi thông minh khi đổi máy/mất mạng.
  - P1: Cursor pagination cho chat (G4), seen server-side (G3).
  - P2: Quét dọn tệp tạm 30 ngày (L1), dọn dẹp localStorage (L2).
- **Kết quả tổng thể**:
  - 89/89 tests passed (100%).
  - Dữ liệu thật 67.718 hồ sơ trong SQL Server `StpArchive` bảo toàn nguyên vẹn.
  - Service API và tunnel ngrok `https://mangle-tastiness-french.ngrok-free.dev` hoạt động liên tục, ổn định 200 OK.
