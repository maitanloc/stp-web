# BRAINSTORM: Workspace 2.0 — "Google Sheets cho hồ sơ" (11/11/2026 — CHƯA làm)

> Trạng thái: **SUY NGHĨ TRONG ĐẦU, CHỜ DUYỆT**. Đây là brainstorm sau khi quét
> sâu toàn bộ dự án (frontend 43 file ~13.300 dòng, backend 13 controller,
> 15 bảng). Mục tiêu: **kế thừa cái có sẵn, nâng cấp lên** — không đục lại.
> Vòng 2 (cùng ngày): quét thêm animation inventory, lazy-loading status,
> icon census, chat chi tiết — đủ bộ dữ liệu để làm khi được duyệt.

## Chốt bổ sung của chủ (vòng 2, 11/11/2026 — vẫn CHƯA làm)

1. **Avatar kiểu Google Sheets**: bộ avatar mặc định = tròn màu + chữ cái đầu
   (hệ thống đang có sẵn) + **cho phép upload ảnh riêng**; từ đó cục tròn
   cursor/chấm presence dùng đúng hình đó để nhận ra người.
2. **Notification 2 kênh**: (1) hệ thống báo — sự kiện nhóm/kỹ thuật;
   (2) tin nhắn chat — trải nghiệm kiểu **Messenger của Meta**: badge, typing,
   seen. Chuông topbar gom cả hai kênh.
3. **Lazy loading áp dụng toàn bộ**: component, dữ liệu, ảnh — chi tiết Trụ G.
4. **KHÔNG dùng emoji trong UI** (avatar, icon, label): chỉ được dùng trong
   **nội dung chat**. Mọi icon dùng thư viện `@lucide/vue` như các thứ sẵn có.
5. **UI/animation cùng đẳng cấp** với con mắt PasswordField, cuốn hồ sơ
   BootSplash, gate 3D — bám "hệ animation nhà STP" ở mục cuối để mọi phần
   mới có cùng chất.

## Kết luận nhanh nhất

Những gì anh mơ **60% đã có sườn sẵn**:

- Viền highlight người đang sửa + tag "… đang nắn" + banner xung đột → **đã
  tồn tại** ở tool trích yếu (`SummaryToolView.vue`, style.css L2748–2770) qua
  hệ thống "row mark" mềm. Thiếu: realtime, phủ các trang khác, avatar tròn.
- Avatar màu theo tên → **đã có** procedural (`lib/team.ts`: colorForName +
  initialsFor) — dock, chat, feed đều đang render chữ cái đầu. Thiếu: chọn
  avatar lúc đăng nhập + cột trong DB.
- Dark mode + large-text → **đã có** (useTheme, tokens style.css L40–112).
- Notification → **chưa có gì** (chỉ toast thoáng qua) — đất trống màu mỡ.
- Realtime → **không có SignalR nào**, mọi thứ là polling (presence 8s/20s,
  chat 3s, job 2s) — đây là "khớp nối" quyết định mọi tính năng multiplayer
  mượt như Google Sheets.

---

## Trụ A — Presence 2.0: "ai đang làm gì" hiện hình (Google Sheets/Figma style)

### Kế thừa
- `TeamPresence.cs` (singleton): online theo `(Actor, Where)`, row marks theo
  `(SessionId, RowNumber, Actor)`, TTL 75s/65s, prune khi ping.
- `useTeamPresence.ts`: ping 8s (đang sửa) / 20s (idle), expose module-level
  `teamOnline` + `teamRowMarks`; `setTeamEditingState/clearTeamEditingState`.
- Render sẵn: `.is-team-marked` viền + `.team-lock-tag` "Lộc… đang nắn"
  (SummaryToolView L2236–2258), banner xung đột khi mở dòng người khác đang sửa
  (L2370–2373), dock pill 3 avatar chồng + tooltip "actor · where".
- Audit `TeamAudit.Write` — choke point duy nhất của mọi hành động nhóm.

### Nâng cấp đề xuất
1. **SignalR nền móng (A1)**: hub `/hubs/team` — presence, chat, marks, activity
   broadcast theo nhóm `session:{id}` + nhóm `user:{id}`. Polling giữ làm
   fallback khi hub rớt. Backend SignalR nằm sẵn trong shared framework (không
   thêm package); app chạy same-origin qua tunnel nên không đụng CORS
   AllowCredentials. Gate bypass list thêm `/hubs`.
2. **Cursors thật (A2)**: broadcast **focus** (trang + sessionId + rowNumber +
   field) 300ms throttle — client vẽ viền màu user + **cục tròn nhỏ avatar**
   nổi trên mép ô đang chọn, kèm nhãn tên. Mở rộng từ "row" xuống "field":
   đang gõ ô trích yếu / ô số tờ / picker nào.
3. **"Đang bấm nút gì" (A3)**: KHÔNG broadcast từng cú click (ồn + nhạy cảm) —
   chỉ hành động đáng kể: mở phiên, áp dụng bulk, xuất file, đồng bộ, đổi
   filter lớn. Đưa lên chip nổi "Lộc vừa Xuất bản lọc · 1 phút trước" và vào
   feed hoạt động (audit sẵn có chính là nguồn — chỉ cần push realtime thay vì
   đợi poll 12s).
4. **Phủ hết các trang**: presence không chỉ ở trích yếu — Kho hồ sơ ("anh em
   đang tra cùng quyển 2020"), Quản lý, Developer. `whereLabel` đã map 8 route
   — chỉ cần UI hiển thị.
5. **Chống ngập**: cap hiển thị ~8 cursor gần nhất, còn lại gộp "+N"; mobile
   thu về dot sát mép màn hình.

### Nỗ lực
A1: M · A2: M · A3: S. Tổng nhánh A ~4–5 ngày, chia 3 lần deploy nhỏ.

---

## Trụ B — Avatar khi đăng nhập (cá nhân hoá)

### Kế thừa
- `colorForName`/`initialsFor` (lib/team.ts L4–22) + 6 chỗ render avatar sẵn
  trong TeamDock (header, menu, chat, feed, pill, fallback "?" khi chưa tên).
- AuthGate 3D đã hoàn thiện (mặt đăng ký có sẵn) — chỗ gắn "chọn avatar" tự
  nhiên. `StpUsers` không có cột avatar (snapshot L646–687 xác nhận) — sạch,
  thêm migration không đụng gì.

### Nâng cấp đề xuất (theo chốt vòng 2: kiểu Google Sheets + upload, KHÔNG emoji)
1. **Mặc định = bộ kiểu Google Sheets** — tròn màu + chữ cái đầu: hệ thống
   đang có sẵn (`colorForName` hash → hsl + `initialsFor`). Chỉ cần nâng chuẩn
   theo **palette cố định bảng màu Google** (các hue đủ tương phản, có biến thể
   dark mode) và làm đậm chữ — kế thừa 100%, không viết mới.
2. **Upload ảnh riêng (optional, mỗi user)**: trong menu tài khoản (hoặc sau
   đăng ký) → chọn ảnh → **cắt vuông client** (canvas resize 96×96 webp) →
   `POST /api/team/avatar` → lưu `data/avatars/{userId}.webp` qua FileStorage
   sẵn có → `GET /api/team/avatar/{userId}` trả ảnh kèm Cache-Control; cột mới
   `StpUsers.AvatarVersion` (int) để phá cache theo ETag — **không cần cột ảnh
   trong DB**. Đây là pipeline ảnh ĐẦU TIÊN của app (hiện không có một `<img>`
   nào) — làm nền luôn cho các ảnh sau này.
3. **Cục tròn nhận diện**: cursor dot / chấm presence / dock / chat / recap /
   notification / UsersView admin — tất cả đi qua **một hàm duy nhất**
   `renderAvatar(user)`: có ảnh → `<img loading="lazy">`, không → vòng chữ
   cái đầu. Cursor của Trụ A cũng ăn theo tự động.
4. Riêng tư: endpoint ảnh chỉ phục người trong team (check cookie), không
   upload khi chưa đăng nhập, giới hạn dung lượng ~2MB trước khi nén.

### Nỗ lực
Bản mặc định Google-Sheets initials: S. Upload pipeline (cắt client +
endpoint + cache): M. Hàm renderAvatar chung: nằm luôn trong phần này.

---

## Trụ C — Notification (đất trống to nhất)

### Kế thừa
- `useToast` (module-level reactive list, tự dismiss 5s/9s) — pattern seed.
- Feed hoạt động `/api/team/activity` (poll AuditLogs) + recap đã làm ở vòng 2.
- Chat 3s poll + badge unread trong dock đã có khái niệm "chưa đọc" chỗ dock.

### Nâng cấp đề xuất (theo chốt vòng 2: 2 kênh — hệ thống + tin nhắn kiểu Messenger)
1. **Bảng `Notifications`** (Id, AudienceUser nullable, AudienceRole, Type,
   Title, Body, DeepLink, CreatedAt, ReadAt) + API list/unread-count/mark-
   read. Nguồn sự kiện: `TeamAudit.Write` (choke point duy nhất) cho **kênh
   hệ thống**; `PostChat` cho **kênh tin nhắn**.
2. **Chuông trong topbar** (AppShell actions cluster L179–204 — chỗ trống sẵn
   cho icon mới, icon dùng lucide `Bell`): badge số chưa đọc + dropdown với
   2 tab "Hệ thống" / "Tin nhắn", deep link bấm vào nhảy đúng trang/dòng,
   "đánh dấu tất cả đã đọc".
3. **Chat nâng cấp kiểu Messenger** (TeamDock hiện: poll 3s khi mở/12s đóng,
   không optimistic, không typing, không seen):
   - Typing indicator "Lộc đang soạn…" (realtime từ Trụ A1);
   - Seen/đã đọc server-side (hiện `lastSeenAt` chỉ ở RAM — mất khi reload,
     phải đưa vào DB hoặc presence record);
   - Optimistic message (tin hiện ngay, đánh dấu gửi dở);
   - **Emoji picker trong ô chat** — emoji CHỈ được phép ở đây;
   - React/đáp lại tin nhắn: optional sau;
   - 3 SVG tay trong TeamDock (chevron/send/chat-bubble) chuyển về lucide
     cho chuẩn hoá icon.
4. **Loại thông báo hệ thống mở đầu**:
   - Ai vừa đồng bộ/đổ hồ sơ mới vào kho ("Lộc vừa đồng bộ 1.240 hồ sơ");
   - Dòng mình định sửa bị người khác chiếm trước (cursors Trụ A);
   - Phiên trích yếu dở của mình còn ~1 giờ là hết hạn (12h TTL sẵn có);
   - Admin tạo/sửa tài khoản → thông báo chính người đó;
   - Mention `@tên` trong chat → riêng cho người đó.
5. Toast (thoáng) + Notification (lưu lâu) **cùng nguồn sự kiện** — toast là
   "preview", bell là "hộp thư"; tin nhắn vẫn có kênh dock riêng như Messenger.

### Nỗ lực
Bảng+API: M · Bell 2 kênh: M · Chat Messenger (typing/seen/optimistic/
picker): M · mention: S.

---

## Trụ D — "Biến hóa thành 1 bản Google Sheet" (bảng sống kho hồ sơ)

### Kế thừa
- Dữ liệu nền: `Dossiers` (67.718 dòng, index (YearId, Number), rowversion) +
  API phân trang chuẩn `PagedResponse` (WorkbooksController L90–156).
- ArchiveDetailView đã có filter năm + loại + query; DossierView có bản chi
  tiết + full text; PrintView/PrintBatch cho in.
- Presence/audit sẵn — bảng sống chỉ cần lớp phủ.

### Nâng cấp đề xuất
1. **Grid "bảng sống" mới** trong Kho hồ sơ: virtualized (render chỉ ~40 dòng
   trong khung nhìn cho 67k dòng), header đóng băng, resize cột, filter
   realtime **dùng chung cho cả nhóm** (ai đang cùng filter thấy nhau qua
   Trụ A) — chế độ "cả phòng nhìn một bảng".
2. **Ghi chú/comment theo dòng** (kiểu comment Google Sheets): bảng
   `DossierComments` (DossierId, Actor, Body, CreatedAt) + hiển thị bong bóng
   cạnh dòng. Cực hợp cho "hồ sơ này thiếu số tờ, nhờ Lộc check".
3. **Lịch sử ô giá rẻ**: AuditLogs đã ghi export/sync — hover dòng → "sửa gần
   nhất bởi X, lúc Y" (đọc từ sync changes + audit, không cần bảng mới).
4. Chỉnh sửa trực tiếp trên grid? **Chưa vội** — bản 1 là xem + comment + pres-
   ence; sửa vẫn qua tool trích yếu (chống đụng nhau trên 67k dòng thật).

### Nỗ lực
Grid sống: L (chia 2–3 lần) · Comment: M · History hover: S.

---

## Trụ E — Sidebar / Topbar mở rộng (đúng ý "thêm các tool")

### Kế thừa
- `AppShell.vue` `sections` computed (L54–79) — nav là **mảng khai báo**, thêm
  tool = thêm 1 object. Topbar actions cluster L179–204 đã có: nút Đồng bộ
  nhanh, large-text, theme.
- Sidebar đã nhóm "Quy trình / Tra cứu / Hệ thống" + health chip tự poll.

### Nâng cấp đề xuất
1. **Tool registry + badge đếm**: mỗi nav item gắn badge động (số phiên dở,
   số dòng còn thiếu trích yếu, số job đang chạy) — dùng lại API sessions/
   jobs sẵn có. Ghim tool ưa thích lên đầu (localStorage).
2. **Ctrl+K global search** (nối với plan "trich-yeu-sieu-toc" GĐ 1.5): tìm
   hồ sơ theo số/tên, nhảy tool, chạy lệnh ("xuất theo bộ lọc", "đổi theme")
   — command palette kiểu Google Sheets help.
3. **Topbar**: chuông notification (Trụ C) + chip avatar người dùng + chip
   "phiên dở" (resume 1 click) + đồng hồ ca làm việc. Mọi icon mới nằm trong
   cluster sẵn — không đụng layout.
4. Tool mới nếu cần sau (đề xuất, không làm ngay): so sánh 2 quyển (diff),
   thống kê năm, nhật ký nhóm tổng hợp.

### Nỗ lực
Registry+badge: S · Ctrl+K: M · Topbar chips: S.

---

## Trụ F — Hỗ trợ (onboarding + a11y có sẵn)

### Kế thừa
- Dark mode + large-text + health chip + EmptyState + StatusBadge + ToastHost
  đã tạo "bộ khung chăm lo UI" — thiếu chỗ hướng dẫn.

### Nâng cấp đề xuất
1. **Tour lần đầu** (5 bước, chỉ chạy 1 lần — localStorage flag): lật hồ sơ →
   vào app → dock nhóm → tool trích yếu → xuất.
2. **Cheat sheet phím** (nhấn `?`): bảng mọi shortcut (Ctrl+S/Z/Y/Enter, `/`,
   ←/→, Esc, Ctrl+K tương lai, C "như dòng trên" theo plan trước).
3. Tooltip chuẩn hoá + title tiếng Việt cho mọi icon (a11y aria đã tốt).

### Nỗ lực
Mỗi món: S.

---

## Trụ G — Lazy loading toàn bộ (chốt vòng 2)

### Đã lazy sẵn (kế thừa, không phải làm)
- **100% route code-split**: cả 15 route đều `() => import(...)` (router.ts),
  Vite cắt chunk riêng cho từng view (đã thấy `SummaryToolView-*.js` trong
  dist) — mở trang nào nạp trang đó.
- **Dữ liệu**: mọi view fetch-on-mount kèm skeleton/spinner; phân trang
  SummaryToolView (20/50/100/200), PrintBatch fetch tuần tự có tiến trình.
- Dock chat: poll 3s khi mở / 12s khi đóng.

### Cần lazy thêm (khi làm)
1. **defineAsyncComponent cho panel nặng**: chat panel của dock lần đầu mở mới
   fetch lịch sử (take=100); notification feed, bảng sống, comment panel —
   MỌI panel/popup mới phải lazy mount + fetch lần đầu mở ("mở tới đâu nạp
   tới đó" — nguyên tắc bắt buộc cho Trụ C/D/E).
2. **Gộp unread vào presence ping**: badge tin nhắn khi dock đóng đang nhờ
   poll 12s riêng — cho presence response kèm unreadCount là tốn 0 request.
3. **Ảnh avatar** `loading="lazy"` + Cache-Control (Trụ B) — đồng thời là
   pipeline ảnh đầu tiên của app.
4. **Virtualization**: bảng sống 67k dòng (Trụ D) bắt buộc virtual scroll
   (chỉ render ~40 dòng trong khung nhìn); chat dài thì mới cần, hiện take=100
   vẫn ổn.
5. **prefers-reduced-motion**: kill-switch toàn cục (hiện app không có — gap
   a11y) đặt trong style.css, mọi keyframe mới phải tôn trọng.

## Hệ animation nhà STP (house motion — chuẩn cho mọi phần mới)

Kết quả census vòng 2: đúng 10 @keyframes, 5 `<Transition>`, 4 cubic-bezier
đặc trưng — UI mới phải bám vào này, không chế curve lạ:

| Curve nhà | Giá trị | Đang dùng ở | Dành cho |
|---|---|---|---|
| **Decel mượt** | `cubic-bezier(0.22, 0.9, 0.26, 1)` | gate lật 3D 1.05s, recap 0.45s | mở panel, dropdown, notification card, cursor chạy tới |
| **Overshoot blink** | `cubic-bezier(0.3, 0.7, 0.35, 1.25)` | con mắt 0.4s scaleY 0.1→1 | pop-in cursor dot, badge nảy, avatar xuất hiện |
| **Snap-in** | `cubic-bezier(0.34, 0.06, 0.22, 1)` | leaf-flip cuốn hồ sơ | lật/đổi mặt, xoay 3D |
| **Ease thường** | `ease` 0.12–0.4s | hover, toast 0.22s | micro-interaction |

Thời lượng chuẩn: **micro 150–250ms · component 300–450ms · signature 1.05s**.
Hover nhà: `translateY(-1px)` + shadow, `:active translateY(1px)`, focus ring
`0 0 0 3px var(--brand-soft)`.

### Lỗ hổng phải vá để "đẳng cấp đều tay" (Pha 0)
- **Modal chưa có transition nào** — LoginModal, UserNameModal, preview modal
  trích yếu đều `v-if` trần (backdrop hiện cứng) → thêm enter/exit scale+fade
  0.22s chuẩn toast, dùng TransitionGroup pattern sẵn có.
- **Route chưa có transition** — `<RouterView>` trần → thêm fade/slide 0.25s
  (đề phòng view nặng chuyển trang giật, chỉ animate opacity là đủ).
- **Chưa có `appear`** ở Transition nào, chưa có reduced-motion (Trụ G.5).
- `will-change` chỉ dùng 1 chỗ (leaf-flip) — giữ kỷ luật này, cursor dot mới
  là nơi thứ hai hợp lệ.

### Áp cho phần Workspace 2.0
- Cursor pop-in: overshoot; cursor chạy khi người đổi ô: decel (transition
  transform cho mượt "trôi" kiểu Figma).
- Bell rung khi có tin: keyframes rotate ±8° 0.4s — decel.
- Notification card vào: slide+fade decel 0.3s; ra: nhanh 0.18s.
- Avatar upload modal: kế thừa flip nhẹ nhà (không cần 3D 1.05s — 0.35s).
- Chat Messenger: typing dots 3 chấm nhảy (keyframes pulse có sẵn), seen ✓✓
  hiện bằng decel opacity.

---

## Thứ tự đề xuất (khi được duyệt)

| Pha | Nội dung | Vì sao trước |
|---|---|---|
| 0 | Motion + lazy nền móng: vá modal/route transition, reduced-motion, dock panel lazy | Phần mới bám chuẩn nhà ngay từ đầu |
| 1 | B: Avatar kiểu Google Sheets mặc định + hàm renderAvatar chung | Nhỏ, làm nền cho mọi presence UI |
| 2 | A1: SignalR nền + presence realtime | Khớp nối của A2/A3/C/D |
| 3 | A2+A3: Cursors viền + cục tròn (ăn avatar) + chip hành động | Đúng "điểm nhấn Google Sheets" |
| 4 | C: Notifications 2 kênh + bell + chat Messenger (typing/seen/emoji) | Giá trị mỗi ngày cao nhất |
| 5 | E: Registry + badge + Ctrl+K | Tổ chức lại shell cho tool tương lai |
| 6 | D: Bảng sống + comment + history (virtualized) | Nhãn cao nhất nhưng cần nền realtime |
| 7 | B-upload: pipeline ảnh avatar | Khi bản mặc định chạy ổn |

## Rủi ro & nguyên tắc

- **Single-process**: presence/session/queue đều in-memory — chấp nhận (1 host
  duy nhất); SignalR cũng 1 process, không cần Redis backplane.
- **Chống ồn presence**: broadcast focus throttle 300ms, cap 8 cursor, mobile
  thu gọn — không ai muốn màn hình hoa lên 10 chấm.
- **Riêng tư**: không broadcast từng keystroke; chỉ focus (ô nào) + hành động
  đáng kể. Chat mention đúng người. Ảnh avatar chỉ người trong team thấy.
- **Icon/emoji**: chỉ `@lucide/vue` (~65 icon đang dùng đều qua prop `:size`,
  màu currentColor); emoji CHỈ trong nội dung chat; inline SVG tay chỉ hợp lệ
  khi cần animation riêng (con mắt là chuẩn mực); 3 SVG tay trong TeamDock
  chuyển dần về lucide.
- **Đừng làm**: clone Google Sheets full (sửa ô trên 67k dòng), SignalR
  scale-out, bỏ polling ngay lập tức (giữ fallback).
- **Z-index ladder** phải tôn trọng: sidebar 50 · dock 60 · recap 85 · modal
  150 · toast 200 · popover 300 — cursor layer đặt ~55, bell dropdown ~70.
- Mọi UI mới **định nghĩa cả 2 palette** (light/dark tokens) + tôn trọng
  `html.large-text` (L7–38) + `prefers-reduced-motion` sau khi vá Pha 0.
- Motion mới: bắt buộc dùng 1 trong 4 curve nhà + bảng thời lượng ở mục
  "Hệ animation nhà STP".
- Test: hub logic unit test được; UI presence test tay + log; pipeline ảnh
  test resize/nén bằng unit (canvas logic tách composable thuần).

## Trạng thái ghi nhận

- 11/11/2026 vòng 1: người dùng nêu ý tưởng → brainstorm 6 trụ.
- 11/11/2026 vòng 2: chủ chốt 5 điều mới (avatar Google-Sheets + upload,
  notification 2 kênh Messenger-style, lazy loading toàn bộ, cấm emoji ngoài
  chat — icon lucide, UI/animation cùng đẳng cấp house motion). Đã quét thêm
  vòng 2: animation inventory (10 keyframes / 5 Transition / 4 curve nhà),
  lazy status (route 100% split; chưa defineAsyncComponent nào; không một
  `<img>` nào trong app), icon census (~65 lucide, 4 file có SVG tay), chat
  chi tiết (poll 3s/12s, không optimistic/typing/seen). **Bộ dữ liệu coi như
  đầy đủ — làm khi được duyệt.**
- Plan trước ("nhập trích yếu siêu tốc" — `ke-hoach-trich-yeu-sieu-toc.md`)
  vẫn đang chờ. Ctrl+K là điểm chung 2 plan — làm một lần.
