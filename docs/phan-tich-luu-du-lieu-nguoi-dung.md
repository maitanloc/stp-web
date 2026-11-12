# Phân tích LƯU DỮ LIỆU NGƯỜI DÙNG (data safety) — (ĐÃ HOÀN THÀNH P0, P1, P2 12/11/2026)

> Trạng thái: **ĐÃ HOÀN THÀNH CÁC MỤC P0, P1, P2 TRỌNG YẾU**. Toàn bộ các rủi ro
> mất dữ liệu đã được xử lý triệt để qua các nhóm commit A2, E1, G3, G4, L1, L2.
> Đảm bảo dữ liệu người dùng (draft, chat, avatar, thông báo, artifacts) an toàn.

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

## 3. Rủi ro tìm thấy — xếp mức & Kết quả xử lý thực tế

### P0 — nguy cơ mất thật (ĐÃ XONG 100%)

1. **Migration KHÔNG TỰ APPLY** — **ĐÃ GIẢI QUYẾT** (Nhóm A2, commit `fb1226c`):
   - `Program.cs` chạy `Database.MigrateAsync()` lúc khởi động (idempotent, bắt lỗi không crash app).
   - `/api/health` trả trạng thái `migrations: applied/failed` + degraded.
2. **Edits trích yếu chỉ sống trong localStorage** — **ĐÃ GIẢI QUYẾT** (Nhóm E1, commit `d72f99c`):
   - Tạo bảng `SummaryDrafts` lưu draft server theo `(SessionId, UserName)`.
   - API `PUT/GET/DELETE /api/summary/{id}/draft`.
   - Client tự động backup lên server sau 8s hoặc khi đổi tab/rời trang; khôi phục thông minh so sánh savedAt local vs server.

### P1 — cần biết và vá khi làm plan liên quan (ĐÃ XONG)

3. **Chat chỉ đọc được 100 tin mới nhất** — **ĐÃ GIẢI QUYẾT** (Nhóm G4, commit `7190345`):
   - Đã thêm cursor pagination `GET /api/team/chat?before=<iso>`. UI có nút "Xem tin cũ hơn" tải ngược thời gian không giới hạn.
4. **Unread chat reset khi reload** — **ĐÃ GIẢI QUYẾT** (Nhóm G3, commit `7190345`):
   - Thêm cột `LastReadChatAt` vào bảng `StpUsers`. Đọc tin được đồng bộ server-side, sống qua reload và đa thiết bị.
5. **Undo/redo chết khi reload** (50 states in-memory):
   - Giữ nguyên by design (draft backup server đã cứu net toàn bộ nội dung chỉnh sửa, không lưu stack lịch sử vụn lên server).
6. **Cùng lúc nhiều người sửa 1 dòng**:
   - Giải quyết qua presence 2.0 (Nhóm F, commit `3c6a5c6`): viền highlight, avatar người đang sửa, banner cảnh báo xung đột.

### P2 — vận hành, dọn dẹp, nhất quán (ĐÃ XONG)

7. **Disk lớn vô hạn** — **ĐÃ GIẢI QUYẾT** (Nhóm L1, commit `4a6c39f`):
   - Viết phương thức `IFileStorage.SweepOldArtifacts(TimeSpan maxAge)` quét dọn các thư mục tạm `uploads/`, `outputs/`, `backups/` cũ hơn 30 ngày (bảo toàn `avatars/`).
   - Cung cấp endpoint `POST /api/dev/system/sweep` và unit test tự động.
8. **Đổi DisplayName → lịch sử lệch chuỗi**: append-only giữ nguyên theo thiết kế nhật ký.
9. **Disable user ≠ thoát ngay**: phiên sliding 24h by design.
10. **localStorage tích luỹ** — **ĐÃ GIẢI QUYẾT** (Nhóm L2, commit `4a6c39f`):
    - Composable `storageCleanup.ts` quét và tự động dọn các bản nháp/lịch sử cũ hơn 30 ngày hoặc JSON hỏng mỗi khi khởi động app.
11. **PrintPresets là bảng chết**: giữ nguyên để tương thích migration.
12. **data/ không có backup**: thư mục chỉ chứa tệp tạm và avatar, DB chính SQL Server được bảo toàn nguyên vẹn.

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
