# BRAINSTORM: Workspace 2.0 — "Google Sheets cho hồ sơ" (11/11/2026 — CHƯA làm)

> Trạng thái: **SUY NGHĨ TRONG ĐẦU, CHỜ DUYỆT**. Đây là brainstorm sau khi quét
> sâu toàn bộ dự án (frontend 43 file ~13.300 dòng, backend 13 controller,
> 15 bảng). Mục tiêu: **kế thừa cái có sẵn, nâng cấp lên** — không đục lại.

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

### Nâng cấp đề xuất
1. **Avatar = emoji/mascot + màu vòng** (không upload file — khỏi lo lưu blob,
   riêng tư, đẹp ngay): lưới ~24 emoji nhân vật khi **đăng ký** + đổi được sau
   trong menu tài khoản. Cột `Avatar` nvarchar(32) + `Color` nvarchar(16) vào
   `StpUsers` (migration `AddUserAvatar`).
2. Kế thừa colorForName làm vòng màu khi chưa chọn; chọn rồi thì đội emoji lên
   nền màu —Dock, chat, cursors (Trụ A), recap, notification, UsersView admin
   list **đồng loạt dùng một hàm duy nhất** `renderAvatar(user)`.
3. Optional sau: cho upload ảnh (cần endpoint + cache + giới hạn dung lượng) —
   chỉ làm nếu team muốn; emoji là bản 1.

### Nỗ lực
S–M (nửa ngày cho bản emoji; 1 ngày nếu đủ hệ render hàm chung).

---

## Trụ C — Notification (đất trống to nhất)

### Kế thừa
- `useToast` (module-level reactive list, tự dismiss 5s/9s) — pattern seed.
- Feed hoạt động `/api/team/activity` (poll AuditLogs) + recap đã làm ở vòng 2.
- Chat 3s poll + badge unread trong dock đã có khái niệm "chưa đọc" chỗ dock.

### Nâng cấp đề xuất
1. **Bảng `Notifications`** mới: (Id, AudienceUser nullable, AudienceRole,
   Type, Title, Body, DeepLink, CreatedAt, ReadAt) + API list/unread-count/
   mark-read. Nguồn sự kiện cắm tại `TeamAudit.Write` (choke point duy nhất)
   + `PostChat`.
2. **Chuông 🔔 trong topbar** (AppShell actions cluster L179–204 — chổ trống
   sẵn cho icon mới): badge số chưa đọc + dropdown feed + "đánh dấu tất cả".
   Deep link bấm vào là nhảy đúng trang/dòng.
3. **Loại thông báo mở đầu**:
   - Chat khi dock đang thu gọn (thay vì chỉ badge nhỏ);
   - **Mention `@tên`** trong chat → riêng cho người đó (S);
   - Ai vừa đồng bộ/đổ hồ sơ mới vào kho ("Lộc vừa đồng bộ 1.240 hồ sơ");
   - Dòng mình đang định sửa bị người khác chiếm trước (cursors của Trụ A);
   - Phiên trích yếu dở của mình còn 1 giờ là hết hạn (12h TTL sẵn có);
   - Admin tạo/sửa tài khoản → thông báo chính người đó.
4. Toast (thoáng) + Notification (lưu lâu) **cùng nguồn sự kiện** — toast là
   "preview", bell là "hộp thư".

### Nỗ lực
Bảng+API: M · Bell UI: S · mentions: S.

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

## Thứ tự đề xuất (khi được duyệt)

| Pha | Nội dung | Vì sao trước |
|---|---|---|
| 1 | B: Avatar emoji + migration | Nhỏ, vui, làm nền cho mọi presence UI |
| 2 | A1: SignalR nền + presence realtime | Khớp nối của A2/A3/C/D |
| 3 | A2+A3: Cursors viền + cục tròn + chip hành động | Đúng "điểm nhấn Google Sheets" |
| 4 | C: Bảng Notifications + chuông + mention | Giá trị mỗi ngày cao nhất |
| 5 | E: Registry + badge + Ctrl+K | Tổ chức lại shell cho tool tương lai |
| 6 | D: Bảng sống + comment + history | Nhãn cao nhất nhưng cần nền realtime |

## Rủi ro & nguyên tắc

- **Single-process**: presence/session/queue đều in-memory — chấp nhận (1 host
  duy nhất); SignalR cũng 1 process, không cần Redis backplane.
- **Chống ồn presence**: broadcast focus throttle 300ms, cap 8 cursor, mobile
  thu gọn — không ai muốn màn hình hoa lên 10 chấm.
- **Riêng tư**: không broadcast từng keystroke; chỉ focus (ô nào) + hành động
  đáng kể. Chat mention đúng người.
- **Đừng làm**: clone Google Sheets full (sửa ô trên 67k dòng), upload avatar
  file ở bản 1, SignalR scale-out, bỏ polling ngay lập tức (giữ fallback).
- **Z-index ladder** phải tôn trọng: sidebar 50 · dock 60 · recap 85 · modal
  150 · toast 200 · popover 300 — cursor layer đặt ~55, bell dropdown ~70.
- Mọi UI mới **định nghĩa cả 2 palette** (light/dark tokens) + tôn trọng
  `html.large-text`.
- Test: parser/hub logic unit test được; UI presence test tay + log.

## Trạng thái ghi nhận

- Ngày 11/11/2026: người dùng nêu ý tưởng, CHƯA duyệt làm gì. Plan trước đó
  ("nhập trích yếu siêu tốc" — `ke-hoach-trich-yeu-sieu-toc.md`) cũng vẫn
  đang chờ. Hai plan **không xung đột**: Ctrl+K (GĐ 1.5 plan trước) chung với
  Trụ E ở đây — làm một lần.
