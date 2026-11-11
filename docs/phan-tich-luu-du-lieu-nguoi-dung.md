# Phân tích LƯU DỮ LIỆU NGƯỜI DÙNG (data safety) — 11/11/2026 (vẫn CHƯA sửa code)

> Trạng thái: **PHÂN TÍCH CHỜ DUYỆT** — mục đích: đảm bảo mọi công sức người
> dùng bỏ ra (draft, tin nhắn, avatar, thông báo…) không mất oan trong BẤT KỲ
> tình huống nào. Dùng làm nền cho Workspace 2.0
> (`brainstorm-workspace-2-0.md`) và plan trích yếu siêu tốc.

## 1. Bản đồ "dữ liệu đang sống ở đâu" (5 kho)

| Kho | Chứa gì | Dựng sẵn từ |
|---|---|---|
| **DB SQL Server** (StpArchive) | 15 bảng: Dossiers (67.718), StpUsers, CatalogItems, ChatMessages, AuditLogs, SyncSessions/SyncChanges, BatchJobs/Items/Logs, MacroVersions, PrintPresets, ArchiveWorkbooks/Years, DocumentLines | EF Core migrations (4 cái trong repo) |
| **Disk `data/`** (gitignored — không vào git) | `uploads/{jobId}/` + `uploads/{sessionId}/` (kèm marker), `outputs/`, `backups/`, `runtime/developer-auth.json` | FileStorage (root `data/` dưới Stp.Api) |
| **localStorage máy người dùng** (8 keys, all try/catch) | `stp.theme`, `stp.largeText`, `stp.user.name`, `stp.dev.token`, `stp.summary.activeSession`, `stp.summary.draft.<file>`, `stp.summary.history.<file>` ×N, (autoSession resume) | — |
| **RAM service** | presence + row marks (TTL 75s/65s), summary sessions dict, batch queue channel, dev throttle | chết khi restart (có hồi phục từng phần) |
| **RAM trình duyệt** | undo/redo stack, edits map, dock/chat state, recap card | chết khi reload (draft bù lại) |

## 2. Ma trận sống còn — từng loại dữ liệu × từng tình huống

Chú thích: ✅ sống · ⚠️ sống một phần · ❌ mất (by design ghi rõ).

| Dữ liệu | Reload tab | Đóng tab | Service restart | Reboot máy | Hết hạn/ghi chú |
|---|---|---|---|---|---|
| Tài khoản, mật khẩu hash | ✅ | ✅ | ✅ | ✅ | DB; PBKDF2 |
| Cookie phiên 24h sliding | ✅ | ✅ | ✅ | ✅ | stateless — không có server session store; sliding khi vào app |
| Kho hồ sơ 67.718 (Dossiers) | ✅ | ✅ | ✅ | ✅ | DB + rowversion chống đè |
| Tin nhắn chat | ✅ | ✅ | ✅ | ✅ | DB **nhưng >100 tin mới nhất KHÔNG đọc lại được** (API không có cursor — rủi ro 3) |
| AuditLogs / SyncSessions | ✅ | ✅ | ✅ | ✅ | DB, không prune — match recap theo **chuỗi DisplayName** (rủi ro 8) |
| **Edits trích yếu** | ⚠️ | ⚠️ | ⚠️ | ⚠️ | **KHÔNG BAO GIỜ lưu server** — chỉ draft localStorage; export là lần "lưu" duy nhất (rủi ro 2 — lớn nhất) |
| Draft (700ms debounce) | ✅ | ✅ | ✅ | ✅ | chết khi: đổi máy/thiết bị khác, clear site data, quota full, tab riêng tư; mất tối đa 0.7s chữ cuối |
| Phiên trích yếu (Excel gốc) | ✅ | ✅ | ✅ lazy-restore từ disk | ✅ | TTL 12h — hết hạn xoá cả thư mục; marker chỉ chứa `createdAt` |
| Undo/redo stack | ❌ | ❌ | — | — | in-memory 50 states (rủi ro 5) |
| Presence + row marks | — | ✅(ping lại) | ❌ (by design) | ❌ | người khác thấy bạn "offline" ~giây sau restart |
| Job + JobLogs + outputs | ✅ | ✅ | ✅ + requeue job Running lúc khởi động | ✅ | file outputs/backups sống MÃI (rủi ro 6) |
| Theme/large-text/user name | ✅ | ✅ | ✅ | ✅ | localStorage; corrupt → fallback im lặng (đã guard toàn bộ 21 chỗ) |
| Dev auth (JWT key) | ✅ | ✅ | ✅ | ✅ | file `runtime/` atomic write; corrupt → sinh key mới, JWT cũ toàn bộ chết (deliberate) |

## 3. Rủi ro tìm thấy — xếp mức, mỗi cái kèm cách xử lý đề xuất

### P0 — nguy cơ mất thật, phải xử lý trước khi làm tính năng lớn

1. **Migration KHÔNG TỰ APPLY** — không có `Database.Migrate()`/`EnsureCreated()`
   ở bất kỳ đâu trong repo; 4 migrations hiện có đã được áp vào DB bằng tay
   (cơ chế ngoài code). Nghĩa là: khi làm Workspace 2.0 thêm cột
   `AvatarVersion`/bảng `Notifications`, **deploy mà quên áp migration → API
   chạy trên schema cũ, lỗi SQL runtime mới thấy** (seed admin là chỗ duy nhất
   nuốt lỗi và retry, không đủ). **Đề xuất: gọi `MigrateAsync()` lúc startup
   (idempotent) + health báo degraded nếu migrate hỏng.** Đây là việc đầu tiên
   khi được duyệt, trước cả avatar.
2. **Edits trích yếu chỉ sống trong localStorage** — người dùng gõ cả trăm dòng
   rồi: đổi máy → mất hết nếu chưa export; clear site data → mất; quota full →
   chỉ còn trong RAM tab hiện tại. Server session = file Excel **nguyên bản**,
   chưa từng có edits nào lên server (preview/export là tính toán từ request).
   **Đề xuất (khi duyệt): draft backup nhẹ lên server**
   `PUT /api/summary/{id}/draft` (JSON vài KB, đè theo session, TTL theo
   session 12h) — đổi máy vẫn có. Chi phí nhỏ, cứu được ca nhập lớn nhất.

### P1 — cần biết và vá khi làm plan liên quan

3. **Chat chỉ đọc được 100 tin mới nhất** — API không có cursor `before/id`;
   tin cũ hơn là bất khả đạt mãi mãi (DB vẫn còn). Plan Messenger (Trụ C) bắt
   buộc thêm cursor pagination khi làm.
4. **Unread chat reset khi reload** — `lastSeenAt` chỉ trong RAM trình duyệt.
   Plan Messenger đã chốt server-side seen (StpUsers thêm `LastReadChatAt` hoặc
   presence record) — sống reload + đồng bộ đa thiết bị.
5. **Undo/redo chết khi reload** (50 states in-memory). Draft bù lại nhưng
   thành 1 snapshot — từng bước undo không hồi phục. Đề xuất: chấp nhận
   (draft đã cứu net edits), chỉ cần nhãn ghi rõ cho người dùng; KHÔNG lưu
   stack lên server (rác).
6. **Cùng lúc nhiều người sửa 1 dòng**: row mark hiện là "mềm" (chỉ cảnh báo,
   không chặn); 2 người export nhau vẫn thắng theo ai sau. Chấp nhận được vì
   export không đụng DB — nhưng plan Google Sheets (Trụ D) phải giữ nguyên tắc
   "prefill → duyệt" để không có ai ghi đè lẫn.

### P2 — vận hành, dọn dẹp, nhất quán

7. **Disk lớn vô hạn**: `data/uploads/{jobId}/`, `outputs/`, `backups/` không
   bao giờ dọn (sweeper 12h chỉ quét thư mục có marker summary). **Đề xuất:
   sweep theo tuổi 30 ngày cho job/sync artifacts** (DB giữ nguyên làm dấu
   vết; file to bỏ được).
8. **Đổi DisplayName → lịch sử lệch chuỗi**: recap/audit match theo string
   Actor — đổi tên thì việc cũ không hiện trong recap nữa. Chấp nhận được nếu
   đổi tên hiếm; đề xuất ghi chú trong UsersView "đổi tên sẽ mốc lại tổng
   kết". Chat/audit cũ giữ tên cũ (append-only — đúng chất nhật ký).
9. **Disable user ≠ thoát ngay**: cookie cũ còn hiệu lực tới 24h (claims baked
   lúc login, không recheck DB mỗi request). Nếu muốn khoá tức thì: cần thêm
   check IsActive trong `StpTeamIdentity.FromAsync` (1 query/request — cân
   nhắc hiệu năng) hoặc short-lived claims.
10. **localStorage tích luỹ**: `stp.summary.history.<file>` không bao giờ bị
    xoá (mỗi file 1 key, cap 50 entry); draft chỉ xoá theo sự kiện (export/
    close), không theo tuổi. Đề xuất: dọn key quá 30 ngày lúc boot view.
11. **PrintPresets là bảng chết** — có DbSet + migration nhưng không UI/API nào
    đọc/ghi. Chân trời: hoặc bỏ khỏi migration sau, hoặc dùng cho plan in sau
    này (không cấp bách).
12. **data/ không có backup** — chấp nhận: chỉ chứa file tạm + ảnh avatar sau
    này; dữ liệu "thật" là DB (nên nghĩ tới backup DB SQL Server định kỳ ở
    mức máy chủ — ngoài phạm vi app).

## 4. Quyết định lưu trữ cho từng tính năng Workspace 2.0 (mọi trường hợp)

| Tính năng | Lưu ở đâu | Vì sao + các trường hợp đã tính |
|---|---|---|
| **Avatar ảnh** (Trụ B) | Disk `data/avatars/{userId}.webp` + cột `AvatarVersion` int trong StpUsers | Ảnh to không vào DB; version++ phá cache; upload fail → 500 rõ ràng, giữ avatar cũ; user bị disable → giữ ảnh; không có delete user nên không có mồ côi file; nhiều thiết bị → cùng 1 URL |
| **Bộ avatar mặc định** | Không lưu gì — initials+color sinh từ chuỗi | Đổi tên → đổi màu tự nhiên như Google Sheets |
| **Notifications** | DB bảng mới + ReadAt per-user | Sống reload, đa thiết bị; prune sau 90 ngày (tránh vô hạn); khi RequireLogin=false (chưa login) → chỉ toast, không lưu per-user (không có userId) |
| **Seen chat (Messenger)** | Cột `LastReadChatAt` trong StpUsers | Sống reload + đa thiết bị; typing **không lưu** (ephemeral by design) |
| **Comment theo dòng** (Trụ D) | DB bảng `DossierComments` append-only | Không xóa — chất nhật ký; hiện theo dossier |
| **Cursor/presence** | RAM (hiện trạng) | Bản chất ephemeral — mất khi restart là đúng |
| **Draft backup server** (nếu duyệt P0.2) | Disk `data/uploads/{sessionId}/draft.json` + RAM dict | TTL theo session 12h; đè theo actor; xóa cùng session |
| **Ctrl+K / registry / badge** | localStorage (sở thích) + API đếm sẵn có | Không thêm bảng |

## 5. Kế hoạch vá khi được duyệt (thứ tự)

1. **P0.1**: `MigrateAsync()` ở startup + health check migrate (nửa buổi).
2. **P0.2**: draft backup lên server theo session (nửa ngày).
3. **P1.3+4**: chat cursor pagination + seen server-side (làm chung pha
   Messenger của Trụ C — không làm riêng).
4. **P2.7**: sweep job artifacts 30 ngày; **P2.10**: dọn localStorage quá
   hạn lúc boot view.
5. P2.8/9/11/12: ghi chú UI + cân nhắc sau.

## 6. Nguyên tắc lưu trữ nhà STP (từ nay mọi tính năng bám theo)

- **Nhật ký pháp lý** (AuditLogs, SyncChanges, ChatMessages, JobLogs):
  append-only, KHÔNG prune — chỉ dọn file nhị phân to.
- **Dữ liệu làm việc tạm** (summary session, outputs): TTL rõ ràng + báo
  người dùng "còn X giờ" (phiên 12h đã làm đúng).
- **Dữ liệu cá nhân nhẹ** (theme, draft, seen): localStorage mặc định — chỉ
  lên server khi cần đa thiết bị hoặc sống qua clear-site.
- **File nhị phân** (avatar, upload): disk + version cache + TTL sweep.
- Mỗi tính năng mới phải trả lời được: "reload? restart? hết hạn? đổi máy?
  2 người cùng lúc? quota full?" — 6 câu này là checklist bắt buộc trước khi
  code (chính là ma trận mục 2).

## Trạng thái

- 11/11/2026: phân tích xong vòng 3 (persistence). CHƯA sửa code gì cả.
  Phát hiện P0.1 (migrate) là việc đáng duyệt đầu tiên khi bắt tay làm.
- Tất cả phân tích đã commit (`git`) — không có phân tích nào chỉ nằm trong
  hội thoại.
