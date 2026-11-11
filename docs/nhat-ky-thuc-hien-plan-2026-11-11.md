# NHẬT KÝ THỰC HIỆN PLAN — bắt đầu 11/11/2026 (chủ AFK, agent tự làm)

> Lệnh chủ: **làm HẾT tất cả plan** (trích yếu siêu tốc + Workspace 2.0 +
> data safety), kỹ + chi tiết, chất lượng > tốc độ.
> **Luật import mới (chủ chốt 11/11/2026 — nhớ mãi):** khi import file:
> năm **1990–1995** dữ liệu nằm ở sheet **"file nhập"**; từ **1996 trở đi**
> lấy **sheet mang tên năm** (vd "1997") — TUYỆT ĐỐI không lấy "file nhập"
> cho các năm ≥1996 (từ 1997: lấy sheet 1997, không lấy file nhập).

## Checklist tổng (tick khi xong — mỗi mốc 1 commit riêng)

### Nhóm A — Nền móng
- [x] A1. Luật chọn sheet khi import (file nhập = 1990–1995; sheet năm cho 1996+)
  - `SummaryToolService.Parse` điều phối: sheet tên bắt đầu `^\d{4}` → sheet năm
    (ép mọi dòng về năm của sheet); "file nhập" giữ dòng Year=null hoặc năm
    KHÔNG được sheet năm có dữ liệu phủ. "file dán"/"form"/sheet K1=
    BANG_IN_HANG_LOAT loại; không sheet năm nào có dòng trích yếu → fallback
    hành vi cũ 1 sheet. Tests mới `SummaryMultiSheetImportTests` (4 test).
- [x] A2. P0.1: `MigrateAsync()` ở startup + health báo degraded khi hỏng
  - Program.cs chạy `Database.MigrateAsync()` lúc khởi động (log lỗi không
    crash), `/api/health` thêm `migrations: applied/failed` + degraded.
- [x] A3. Deploy + verify file thật `DK KET HON (5).xlsx`: 6 sheet nhận diện
  (file nhập 18.944 + 1995ok 394 + 1996 528 + 1997 784 + 1998 813 + 1999 1.137),
  lọc năm 1997 → 784 hồ sơ lấy từ sheet "1997" (badge sheet hiện trong UI),
  file nhập còn 1990–1994 + 2000–2025 + null; tests 86/86 xanh; tunnel 200.
- [x] A4. **Quy ước cột CỐ ĐỊNH sheet năm (anh nói 11/11/2026 — nhớ mãi):**
  tiêu đề cột trong bảng bị SAI nên không tin tên cột; ánh xạ cứng cho sheet
  năm: **B=hồ sơ số, D=trích yếu, E=thời gian tài liệu, F=ghi chú (bỏ),
  G=số tờ** — cột A/C là phụ, không dùng. Verify file thật: 1997 đọc
  num/doc=1866 (B), số tờ=01-56 (G) ✓; file nhập giữ nhận dạng cũ.

### Nhóm B — Pha 0 Workspace 2.0 (motion + lazy)
- [x] B1. Modal transition (LoginModal, UserNameModal, preview/print/export-name/save-archive modal)
  - `<Transition name="modal">` + CSS mờ dần/thẻ nổi 0.16s, class toàn cục
    `.modal-enter-*` trong style.css (root Transition = backdrop).
- [x] B2. Route transition + reduced-motion toàn cục
  - AppShell `RouterView v-slot` + `<Transition name="route" mode="out-in">`
    (key = route.name); `@media (prefers-reduced-motion: reduce)` tắt mọi
    animation/transition toàn app.
- [x] B3. Dock panel lazy (chat/activity fetch lần đầu mở)
  - onMounted không fetch nữa; mở dock mới tải (toggleOpen); khi đóng nhịp
    12s vẫn lấy ngầm để đếm tin chưa đọc; panel + pill có transition dock.

### Nhóm C — GĐ 0 trích yếu siêu tốc
- [x] C1. Chip 1-chạm trong editor dòng
  (bảng + editor: bộ 7 chuẩn + 4 mục meta hay gặp; bấm thêm/gỡ, chip đậm
  `is-on` khi dòng đã có; E2E: chip "giấy khai sinh" dòng 1 → trích yếu ghép
  đúng, chờ xuất +1)
- [x] C2. Phím "như dòng trên" (C)
  (window keydown listener riêng, bỏ qua INPUT/TEXTAREA/SELECT/contentEditable
  + Ctrl/Alt; đang sửa editor → dòng đó, không → dòng thiếu kế tiếp; E2E: bấm C
  ở body → dòng 2 chép bộ dòng 1; chip "C — như dòng trên" trong editor cùng
  lệnh)
- [x] C3. Bộ mặc định theo loại + "áp dụng cho dòng lọc còn thiếu"
  (máy học bộ phổ biến nhất của cùng loại hồ sơ ≥2 dòng → chip "Bộ loại: …";
  nút "Áp dụng cho dòng lọc còn thiếu" trên hàng Áp nhanh duyệt hết các trang
  200/req, chỉ gán cho dòng items rỗng, giữ sheetNumber/documentNumber cũ;
  E2E: 22.507 dòng còn thiếu nhận bộ trong ~30s; nút đặt ngoài bulk bar vì
  không cần tích dòng)
- [x] C4. Chạy hàng tự nhảy dòng thiếu kế tiếp
  (localStorage `summary.autoNext`, mặc định bật, toggle trong editor head; sau
  lưu/bấm chip/editor → tự mở editor + scroll tới dòng thiếu kế tiếp, hết dòng
  → toast "Hết dòng thiếu"; E2E: áp bộ loại dòng 3 → editor nhảy dòng 4; Lưu
  dòng 4 → editor nhảy dòng 5)
- [x] C5. Xuất nhanh 1 click (nhớ tên file + phạm vi)
  (nút "Xuất nhanh" cạnh "Xuất bản lọc"; gọi doExportFiltered thẳng, bỏ qua
  preview; localStorage `summary.quickName` nhớ tên lần trước, lần đầu dùng
  tên gợi ý; toast đếm số dòng đổi từ allChanges khi chưa có preview; E2E:
  1 click → tải `DK KET HON (5)_loc.xlsx` 2.8MB, "22.600 giữ · 22.510 dòng
  cập nhật"; sau xuất clearDraft tự động)
- Lưu ý kỹ thuật nổi lên khi verify:
  - `MaxChanges` controller 5000 → **30000** (C3 tạo hơn 22k dòng đổi cùng
    lúc; payload ~7.3MB < giới hạn Kestrel 30MB). Build + tests 86/86 xanh,
    service restart.
  - wwwroot KHÔNG tự nhận dist mới khi service đang chạy — phải robocopy
    `dist` → `Stp.Api\wwwroot` (lần đầu verify thấy UI cũ vì thiếu bước này).
  - Draft sau reload: dialog confirm "Tìm thấy bản nháp … Khôi phục?" → chấp
    nhận phục hồi đủ 22.510 dòng.

### Nhóm D — Trụ B avatar mặc định
- [x] D1. renderAvatar() hàm chung + palette Google-Sheets chuẩn + dark mode
  (`lib/team.ts`: 14 màu Google Workspace collaborator (#e8710a, #246fdb,
  #f4518c, #1e8e3e… xen kẽ nóng/lạnh), `colorForName` chọn palette theo hash
  tên thay vì hue ngẫu nhiên; `renderAvatar(name)` trả initials + style nền +
  chữ trắng — nền bão hoà đọc được trên sáng lẫn tối, không cần biến thể dark
  riêng)
- [x] D2. Thay toàn bộ chỗ render avatar (dock, chat, feed, cursors tương lai)
  (TeamDock: tool-avatar dock, avatar menu tài khoản, avatar chat, mini avatar
  pill, event-dot feed — hết `avatarStyle` cũ; `.who`/`.event-actor` vẫn dùng
  `colorForName` nền palette mới; verify trên pill đang mở: "Bk"=#f5386a,
  "TL"=#ff8f00 đúng palette)

### Nhóm E — P0.2 data safety
- [ ] E1. Draft backup server `PUT /api/summary/{id}/draft` + restore

### Nhóm F — Trụ A presence 2.0
- [ ] F1. A1: SignalR hub `/hubs/team` + presence realtime (fallback polling)
- [ ] F2. A2: cursors viền + cục tròn avatar theo focus (throttle 300ms, cap 8)
- [ ] F3. A3: chip hành động đáng kể (export/sync/bulk…) realtime + feed

### Nhóm G — Trụ C notification + Messenger
- [ ] G1. Bảng Notifications (migration) + API list/unread/mark-read
- [ ] G2. Bell topbar 2 tab (Hệ thống / Tin nhắn) + deep link
- [ ] G3. Chat Messenger: seen server-side (LastReadChatAt), typing, optimistic
- [ ] G4. Chat cursor pagination (>100 tin) + mention @tên + emoji picker trong chat

### Nhóm H — Trụ E shell
- [ ] H1. Tool registry + badge đếm trên sidebar
- [ ] H2. Ctrl+K command palette (tìm hồ sơ, nhảy tool, chạy lệnh)

### Nhóm I — GĐ 1 voice (trích yếu siêu tốc)
- [ ] I1. Mic + Web Speech + parser catalog (loại + giấy tờ + số đọc từng chữ số)
- [ ] I2. Phiên đọc liên tục + lệnh (xong/kế tiếp/bộ bảy/lọc…) + học lỗi nghe

### Nhóm J — Trụ D bảng sống
- [ ] J1. Grid virtualized kho hồ sơ + presence
- [ ] J2. Comment theo dòng (bảng DossierComments) + history hover

### Nhóm K — Trụ B-upload (Pha 7)
- [ ] K1. Pipeline avatar upload (cắt client → POST → cache theo version)

### Nhóm L — Data safety P2
- [ ] L1. Sweep job/sync artifacts 30 ngày
- [ ] L2. Dọn localStorage history/draft quá hạn

### Chốt phiên
- [ ] X1. Build + 82+ tests pass + deploy service + health tunnel 200
- [ ] X2. E2E browser verify các tính năng chính
- [ ] X3. Cập nhật 3 docs + AGENTS.md trạng thái "ĐÃ LÀM", commit + push
- [ ] X4. Tổng kết cuối phiên cho chủ đọc khi quay lại

## Ghi chú tiến trình (ghi thêm khi làm)
- 11/11/2026: bắt đầu theo lệnh chủ AFK. Trình tự: A → B → C → D → E → F → G → H → I → J → K → L → X.
- Các mục plan gốc ghi "chỉ làm khi cần" (Whisper server, OCR camera, bot chat, react message) — bỏ qua theo đúng plan, ghi rõ ở X3.
