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
- [x] E1. Draft backup server `PUT /api/summary/{id}/draft` + restore
  (bảng `SummaryDrafts` (migration `AddSummaryDrafts`): khoá (SessionId +
  UserName) unique, PayloadJson nvarchar(max) nguyên trạng phía client;
  PUT = upsert theo người đăng nhập/tự khai (`TeamAudit.ActorFromAsync`),
  GET trả bản nháp hoặc 204, DELETE xoá; frontend: sau khi localStorage lưu
  → đẩy server sau 8s lặng, chuyển tab (visibilitychange) flush ngay, rời
  trang flush nốt; khôi phục: so `savedAt` local vs server, lấy bản mới hơn,
  dialog ghi rõ nguồn "(từ server)"; xuất xong hoặc "Bỏ thay đổi" → DELETE
  server; guard chống lặp PUT/DELETE cùng trạng thái; E2E thật: curl PUT →
  restore toast "1 dòng… (từ server)" → client PUT cập nhật savedAt → reset
  → GET 204; tests 86/86)

### Nhóm F — Trụ A presence 2.0
- [x] F1. A1: SignalR hub `/hubs/team` + presence realtime (fallback polling)
  (`Hubs/TeamHub.cs`: invoke `ping` → `TeamPresence.Ping` (TTL 75s/65s dùng
  lại) + broadcast "presence" cho mọi client; Program.cs AddSignalR +
  MapHub("/hubs/team"); client `useTeamPresence`: HubConnectionBuilder +
  withAutomaticReconnect, hub-first (invoke 'ping'), REST poll vẫn chạy
  song song — hub live thì poll giãn 20s→40s; dock có dot ⚡ live / xám
  poll; verify: negotiate 200 + WebSocket 101 trên cả 2 tab qua app thật;
  frame ws tool không capture được nên broadcast wire-level ghi nhận qua
  negotiate/ws + code path, test thật khi nhóm có máy thứ 2)
- [x] F2. A2: cursors viền + cục tròn avatar theo focus (throttle 300ms, cap 8)
  (tab C quan sát thấy tab B giữ dòng 1 → sau mở/đóng editor + ping REST
  cross-user "Lan" giữ dòng 2: tag "La · Lan đang nắn", nền #d93025 palette,
  viền trái 3px `--mark-color` theo màu actor; teamMarkRows sắp xếp theo
  lastSeenSecondsAgo rồi cap 8; setTeamEditingState throttle 300ms khi nhảy
  nhanh qua dòng; self-filter giữ nguyên — 2 tab cùng user không thấy dấu
  nhau là đúng thiết kế)
- [x] F3. A3: chip hành động đáng kể (export/sync/bulk…) realtime + feed
  (TeamAudit.Write ghi AuditLogs xong broadcast "activity" qua IHubContext
  fire-and-forget — lỗi hub không rớt hành động chính; `POST /api/team/action`
  (TeamActionRequest Verb+Detail) cho hành động client: bulk áp dòng đã
  chọn + C3 áp dòng còn thiếu đều announce; client: liveActivities merge
  vào feed activities (không trùng ±3s), SIGNIFICANT_ACTIONS (export*,
  sync, bulk) nổi chip `action-chip` góc phải dưới, tự tan 8s, TransitionGroup
  chip; actionLabel 'bulk' = "Áp giấy tờ hàng loạt")

### Nhóm G — Trụ C notification + Messenger
- [x] G1. Bảng Notifications (migration) + API list/unread/mark-read
  (bảng `Notifications` (migration `AddNotifications`, gộp luôn
  `StpUsers.LastReadChatAt`): ForUser+IsRead index, Kind system/chat,
  Title/Body/Link; `NotificationService.NotifyAllExcept` sinh cho mọi
  tài khoản khác + broadcast hub "notification" fire-and-forget;
  TeamAudit.Write gọi cho SIGNIFICANT (export*/sync/bulk — thật: xuất
  bản lọc của user 11/11 sinh notification); PostChat sinh kind chat;
  API: GET /api/team/notifications (items + unreadCount), POST
  /api/team/notifications/read ({all} hoặc {ids}, ExecuteUpdateAsync);
  curl verify: lan chat → admin unread 3→4, read all → 0)
- [x] G2. Bell topbar 2 tab (Hệ thống / Tin nhắn) + deep link
  (`NotificationBell.vue` gắn đầu topbar-actions: badge đỏ đếm unread,
  poll 20s + realtime qua hub (badge 1→2 ngay sau bulk của lan —
  đã verify), panel 340px, tab Hệ thống/Tin nhắn bật theo tab còn unread,
  bấm item → markRead(id) + router.push(link) (verify /trich-yeu),
  "Đọc hết tab này"; item có dot theo kind + thời gian relative)
- [x] G3. Chat Messenger: seen server-side (LastReadChatAt), typing, optimistic
  (POST/GET /api/team/chat/seen — mốc seen trên tài khoản, badge đúng trên
  mọi máy (verify: seen → unread 0, lastReadAt ghi); dock đóng chỉ dò seen
  12s, mở tải tin 3s; typing qua hub `typing` (Clients.Others) — gõ throttle
  2s, "X đang gõ…" 3 dot bounce, tự tan 5s; optimistic: tin hiện ngay `tmp-*`,
  server xác nhận fetchChat thay bản thật, lỗi thì gỡ + trả khung soạn)
- [x] G4. Chat cursor pagination (>100 tin) + mention @tên + emoji picker trong chat
  (GET ?before=<iso> — cursor = min CreatedAt trong page, parse
  InvariantCulture + AdjustToUniversal (fix 2 bug: items[0] là tin MỚI nhất
  trong list gốc; curl test phải encode %2B); verify 2 page không trùng +
  hasMore đúng; mentionParts tô đậm @mention (không v-html); emoji grid
  24 ô chèn vào draft; nút "Xem tin cũ hơn" nối page cũ lên đầu)

### Nhóm H — Trụ E shell
- [x] H1. Tool registry + badge đếm trên sidebar
  (`useToolRegistry.ts` nguồn DUY NHẤT khai báo 8 tool — sidebar + palette
  cùng dùng; badge động 60s/lần dùng API sẵn có: Kho hồ sơ = tổng hồ sơ
  67,7k, Bổ sung trích yếu = số phiên inspect, Đồng bộ = job đang chạy
  (ẩn khi 0); ghim tool ưa thích: nút Pin hiện khi hover trên từng item,
  lưu localStorage `stp.pinnedTools`, tool ghim nổi lên ĐẦU đúng nhóm
  của nó — verify: ghim "Bổ sung trích yếu" → lên đầu Quy trình, bỏ ghim
  → trả lại thứ tự)
- [x] H2. Ctrl+K command palette (tìm hồ sơ, nhảy tool, chạy lệnh)
  (`CommandPalette.vue` mount trong AppShell, mở bằng Ctrl+K/Cmd+K hoặc
  nút "Tìm nhanh Ctrl K" trên topbar; KHÔNG Transition — v-if thuần như
  fix dock; tìm hồ sơ: GET /api/dossiers/search?q= — token AND kiểu
  Google, MỌI từ phải khớp (số hồ sơ OR trích yếu gốc OR ghi chú), ưu
  tiên khớp số hồ sơ, mới nhất trước, debounce 250ms, token chặn stale;
  nhảy tool: lọc label không dấu (NFD strip); chạy lệnh: đổi theme,
  chữ lớn, ghim/bỏ ghim tool đang mở; bàn phím ↑↓/Enter/Esc + click,
  footer gợi ý phím; verify browser: gõ "kết hôn" → nhóm Hồ sơ 22614·2016
  → bấm → /ho-so/:id; gõ "kho" → bấm Kho hồ sơ → /kho; Ctrl+K mở, Esc
  đóng; lệnh theme dark↔light thật (data-theme)

### Nhóm I — GĐ 1 voice (trích yếu siêu tốc)
- [x] I1. Mic + Web Speech + parser catalog (loại + giấy tờ + số đọc từng chữ số)
  (Web Speech API `webkitSpeechRecognition` Chrome/Edge — 0 phí, 0
  dependency, vi-VN, chạy trên tunnel HTTPS; `useVoiceInput.ts` chế độ đọc
  liên tục: Chrome tự ngắt sau lặng → tự nối lại 350ms, lỗi quyền →
  toast + ngừng hẳn không vòng lặp; `voiceParser.ts` thuần 100%:
  normalize NFD-strip + tokenize tách dấu câu/gạch ngang, match 2 HƯỚNG
  contiguous (đọc đủ OR nói ngắn "hộ khẩu" ⊆ "sổ hộ khẩu"), levenshtein
  ≤1 cho từ ≥5 ký tự, số đọc TỪNG chữ số ("không ba lẻ năm"="035", số 0
  đầu giữ nguyên), số lượng "hai ảnh"→"ảnh 3×4 ×2", học lỗi nghe thay
  vào câu trước khi parse; 17/17 unit chạy esbuild+node: câu chuẩn,
  4 lệnh, xong-cuối-câu, lọc năm đọc chữ=2025, số tờ chữ+digits,
  items-only, learned, câu vô nghĩa=null)
- [x] I2. Phiên đọc liên tục + lệnh (xong/kế tiếp/bộ bảy/lọc…) + học lỗi nghe
  (nút "Đọc giọng nói" trong header tool + Ctrl+M; thanh sticky khi nghe:
  dot nhấp nháy + live transcript + câu vừa nghe + tắt mic; lệnh:
  "xong"=lưu + tự nhảy dòng thiếu (C4), "kế tiếp"/"quay lại"=mở dòng
  thiếu/trước, "bộ bảy"=điền Bộ 7, "lọc <loại> năm <4 chữ số>"=đặt filter
  (types thật của phiên + năm đọc chữ), "lọc tất cả"=bỏ lọc; prefill CHỜ
  DUYỆT: điền items vào dòng đang mở (hoặc dòng thiếu kế tiếp), mở
  editor + badge "Điền từ giọng nói — nắn rồi lưu", KHÔNG lưu tự động,
  số tờ điền riêng; HỌC LỖI NGHE: khi lưu khác bản voice điền → ghi
  {nghe sai → người sửa} vào localStorage `stp.voiceLearn` (tối đa 100
  cặp, debounce 700ms), lần sau parser tự thay trước khi match; verify:
  mic bấm → bắt recognition, máy automation chặn mic → toast đúng câu,
  không kẹt bar; parser 17/17; build + robocopy ok

### Nhóm J — Trụ D bảng sống
- [x] J1. Grid virtualized kho hồ sơ + presence
  (component `DossierLiveGrid.vue` tự viết virtualized — KHÔNG thư viện:
  ROW_H 44px, OVERSCAN 6, PAGE_SIZE 200 = clamp backend; spacer `<tr>` top/
  bottom + sticky thead, table-layout fixed + colgroup px, KHÔNG horizontal
  scroll; windowed fetch khi cuộn — nhảy 800.000px → số 18298 hiện đúng tức
  thì; resize từng cột kéo được + persist `stp.liveGridCols` + nút "Vừa
  khung" reset — verify kéo +350px, reload phục hồi, reset về 150px; mode
  "Bảng sống/Phân trang" seg-toggle lưu `stp.gridMode` mặc định live, bảng
  classic giữ nguyên làm mode 2, live mode bỏ fetch phân trang, subtitle
  "Đang nạp bảng sống…" khi liveTotal=0; presence "cả phòng nhìn một bảng":
  đổi lọc → debounce 800ms broadcast hub `GridFilter` → đồng đội cùng
  workbook thấy chip "X đang lọc: năm Y · 'từ khoá'" + nút "Áp dụng theo"
  áp lọc; trỏ dòng → throttle 700ms `GridCursor` → đồng đội thấy chip màu
  "Lan đang xem" (palette D1) trên dòng + nền highlight, TTL 6s sweep 2s;
  chip chỉ hiện với actor KHÁC mình — event của chính mình bị bỏ đúng luật)
- [x] J2. Comment theo dòng (bảng DossierComments) + history hover
  (entity `DossierComment` (DossierId, Actor 100, Body 1000, CreatedAt,
  index DossierId/CreatedAt, cascade) + nav `Dossier.Comments` + index
  `SyncChanges.EntityKey` cho tra history — migration `AddDossierComments`
  `20261112021706`; API: GET/POST `/api/dossiers/{id}/comments` (POST
  broadcast hub `dossierComment` cho ALL + audit `dossier.comment`), GET
  `/api/dossiers/{id}/history` trả sync gần nhất theo EntityKey
  `{year}|{NumberLabel-trim-UPPER hoặc Number}` + actor từ audit phiên sync
  + comment gần nhất; lưới đếm bong bóng 💬n qua `Comments.Count()` trong
  select WorkbooksController; UI: bong bóng cạnh dòng → panel inline mở
  rộng có composer (Enter gửi) + history line "X vừa ghi chú giờ"; realtime
  qua hub: comment của đồng đội hiện ngay, dedupe theo id (broadcast tới
  trước POST response — fix nhân đôi, verify sau reload 2 comment đúng);
  bump đếm ưu tiên bump đã có, thiếu thì tra cache trang; verify curl:
  POST/GET tiếng Việt nguyên vẹn, history trả lastComment, commentCount=1,
  page 1/22.5k = 667ms; verify UI: panel dòng 36 hiện history + gửi từ UI
  ✓ bubble 2 + draft xoá; tests 86/86)

### Nhóm K — Trụ B-upload (Pha 7)
- [x] K1. Pipeline avatar upload (cắt client → POST → cache theo version)
  (StpUser.AvatarVersion + migration AddAvatarVersion; API: GET /api/team/avatars, POST /api/team/avatar (cắt vuông canvas 96×96 webp, lưu data/avatars/{userId}.webp qua FileStorage), GET /api/team/avatar/{userId} trả ETag + Cache-Control 7 ngày; broadcast hub 'teamAvatar' realtime; FE: useAvatarUpload composable, renderAvatar chung hỗ trợ imageUrl, menu tài khoản có nút 'Đổi ảnh đại diện' + input file, phủ ảnh trong TeamDock, DossierLiveGrid, SummaryToolView; tests 87/87)

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
- Commit theo mốc: A `fb1226c` · B `e4ca10c` · A4 `5aa7dad` · C `5439c55` · D `31f3bd2` · E (draft server) · F `3c6a5c6` · G (notification+messenger) · fix-dock `f72cc54` · H `2d642dd` · I `65c378d` · J (commit riêng sau khi tick).
