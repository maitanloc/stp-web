# Kiến trúc STP Suite

Tài liệu này chốt các quyết định kỹ thuật cho giai đoạn nâng cấp STP thành bộ công cụ
Excel + Web API + SPA. Bản Razor Pages cũ vẫn được giữ ở thư mục gốc để tham chiếu
và chạy song song cho đến khi SPA hoàn thiện.

## 1. Mục tiêu

- Xử lý hàng loạt file Excel cùng cấu trúc:
  - Loại 1: có `ML` + các sheet in `K1 = BANG_IN_HANG_LOAT`, chỉ thiếu macro.
  - Loại 2: có `ML`, chưa có sheet in; hệ thống tự sinh sheet in từ `ML`.
- Tạo file `.xlsm` có macro, in A4 dọc, mỗi hồ sơ một trang, giống bản in web hiện tại.
- Nhúng bộ công cụ vào Excel: setup trang in, cột/dòng/font, tiêu đề/header/footer,
  ghi chú/trích yếu, toàn văn, dashboard nhiều năm, lọc/xem trước/in, macro editor.
- Excel là bản dữ liệu chính; web đồng bộ hai chiều với quy tắc last-write-wins có nhật ký.
- Lưu dữ liệu, job, lịch sử, phiên bản macro trong SQL Server.
- Giao diện non-tech rõ ràng; giao diện developer chi tiết, có khóa riêng.
- Giữ teal/indigo, bổ sung dark mode.

## 2. Cấu trúc repository

```text
/
  StpSuite.sln
  src/
    Stp.Api/              ASP.NET Core Web API (controllers)
    Stp.Core/             Domain, abstractions, filter/diff/password logic
    Stp.Contracts/        DTO dùng chung cho API và SPA
    Stp.Infrastructure/   EF Core SQL Server, Excel engine, job worker, storage
  tests/
    Stp.Tests/            Unit + integration tests
  src/Stp.Web/            Vue 3 + TypeScript + Vite SPA
  docs/
  data/                   File upload/xuất/backup (gitignored)
  Pages/, Models/, Services/, StpWeb.csproj   Bản Razor Pages cũ (legacy)
```

- `data/` và mọi file `.xlsx`, `.xlsm`, `.pdf`, `App_Data/archive.json` không được commit.
- Macro template `.bas` và template `.xlsm` sạch (không dữ liệu cá nhân) được commit.

## 3. Stack

| Thành phần | Lựa chọn |
|---|---|
| API | ASP.NET Core 10, controllers, JWT cho developer |
| SPA | Vue 3 + TypeScript + Vite |
| ORM | EF Core 10 + SQL Server |
| Excel đọc dữ liệu | ClosedXML |
| Excel mở/lưu/in | WPS COM ưu tiên, Microsoft Excel COM fallback |
| Excel tạo/chèn macro | Microsoft Excel COM (khi có license) hoặc VBA donor + OpenXML |
| Excel không cần cài | Template `.xlsm` + OpenXML post-processing |
| Job nền | `BackgroundService` + `Channel` + bảng job SQL |
| Lưu file | Local storage có cấu hình, metadata trong SQL |
| Auth | Không login user thường; developer dùng password hash + JWT |

## 4. SQL Server

- Instance: `localhost` / `MSSQLSERVER`.
- Database: `StpArchive`.
- Xác thực: Windows Authentication (`Trusted_Connection=True`).
- Connection string đặt trong `appsettings.Development.json`, có thể ghi đè bằng
  biến môi trường `ConnectionStrings__StpArchive`.
- Migration đặt trong `src/Stp.Infrastructure/Persistence/Migrations`.

### Bảng chính

- `ArchiveWorkbooks`: file nguồn, hash, phiên bản, trạng thái, lần đồng bộ cuối.
- `ArchiveYears`: năm, tên sheet, thứ tự.
- `Dossiers`: hồ sơ, số, nhãn, nhóm, loại, hộp, trích yếu, toàn văn, ghi chú, `rowversion`.
- `DocumentLines`: các dòng văn bản của hồ sơ.
- `PrintPresets`: preset A4 dọc, lề, font, header/footer, watermark.
- `BatchJobs`, `BatchJobItems`, `JobLogs`: job, item, log/retry.
- `MacroVersions`: mã macro, version, hash, diff, trạng thái active.
- `SyncSessions`, `SyncChanges`: phiên đồng bộ, thay đổi, xung đột.
- `AuditLogs`: thao tác developer.

## 5. Excel engine

### 5.1 Phát hiện loại workbook

- Có sheet `ML` (hoặc sheet có `A1 = Hộp số`, `D1` chứa `Trích yếu nội dung`).
- Có ít nhất một sheet in `K1 = BANG_IN_HANG_LOAT`, hoặc nhận diện mềm
  `D8` bắt đầu `MỤC LỤC VĂN BẢN` và `D9` chứa `Hồ sơ số`.
- Loại 1: có `ML` + sheet in, thiếu `vbaProject.bin`.
- Loại 2: có `ML`, không có sheet in.

### 5.2 COM engine (khi có Microsoft Excel)

- Không dùng `New-Object -ComObject Excel.Application` trực tiếp vì WPS có thể
  chiếm ProgID/CLSID `Excel.Application`.
- Đọc đường dẫn Microsoft Excel từ
  `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\EXCEL.EXE`.
- Tạo một instance riêng bằng `EXCEL.EXE /automation` với một workbook marker duy nhất.
- Dùng ROT (`GetRunningObjectTable`) để tìm đúng instance có marker, kiểm tra
  `Application.Path` chứa `Microsoft Office`.
- Bật `AccessVBOM = 1` cho đúng phiên bản Office (ví dụ `Office\16.0\Excel\Security`).
- Mở workbook với `AutomationSecurity = msoAutomationSecurityLow`,
  `DisplayAlerts = false`, `ScreenUpdating = false`, `EnableEvents = false`,
  `Calculation = xlCalculationManual`.
- Chuyển `.xlsx` → `.xlsm` bằng `SaveAs(path, 52)`.
- Chèn macro vào `ThisWorkbook` (event handler) và/hoặc standard module.
- Gọi macro kiểm tra bằng `Application.Run("ThisWorkbook.<TênMacro>")`.
- Đóng workbook, `Quit`, release COM, kill process nếu còn treo.

### 5.3 WPS + VBA donor (máy không có VBA runtime)

- WPS mở/lưu `.xlsm` tốt nhưng `VBProject` là null, không chèn/chạy macro được.
- Sau khi WPS lưu file, `VbaProjectInjector` chèn `vbaProject.bin` ở mức ZIP:
  - Khôi phục `workbookPr@codeName` và `sheetPr@codeName` từ donor.
  - Chỉ gán codeName cho số sheet module donor có; sheet còn lại để Excel tự tạo.
  - Thêm content type + relationship + entry `xl/vbaProject.bin`.
- Donor production: 1 sheet module rỗng + `ThisWorkbook` chứa macro STP.
- Không tạo module mồ côi; nếu donor nhiều module hơn file đích, macro có thể
  không chạy dù file vẫn mở được.
- Macro phải được xác nhận trên máy có Excel bản quyền.

### 5.4 Template engine (không cần Excel)

- Template `.xlsm` sạch, có sẵn macro, sheet `ML`, `SETUP`, `DASHBOARD`, `VAN_BAN`.
- Dùng OpenXML để thay dữ liệu, giữ nguyên `vbaProject.bin` và relationship.
- Sinh sheet in từ `ML` bằng ClosedXML trước, sau đó post-process để giữ macro.
- Nếu môi trường không có Excel và template chưa có, trả lỗi rõ ràng thay vì
  tạo file hỏng.

### 5.5 Bố cục in A4 dọc

- `Orientation = Portrait`, `PaperSize = A4`.
- `PrintArea = A8:G<lastRow>`.
- Mỗi block 9 dòng là một trang; chèn page break sau mỗi block.
- 7 cột theo tỷ lệ bản in web: 6% / 13% / 12% / 40% / 16% / 8% / 5%.
- Font mặc định 9–10pt, wrap text, vertical top.
- Dynamic fit: giống `PrintFitClass` của web, giảm font khi nhiều dòng/nhiều ký tự.
- Header/footer lấy từ `PrintPreset`.
- Xuất PDF kiểm tra bằng `ExportAsFixedFormat` khi cần.

## 6. Đồng bộ hai chiều

- Excel là bản chính.
- Nút `Đồng bộ lên web` trong Excel gọi API (hoặc người dùng upload file đã sửa).
- API đọc **chỉ sheet ML** (theo quyết định), tái tạo sheet in từ ML.
- Mỗi hồ sơ có `RowVersion`; khi ghi:
  - Nếu version gửi lên = version hiện tại: cập nhật, tăng version.
  - Nếu khác: last-write-wins, ghi `SyncChange` là xung đột để xem lại.
- `SyncSession` lưu số thêm/sửa/xóa/xung đột.
- Toàn văn đọc từ sheet `VAN_BAN` (nếu có), lưu vào `Dossiers.FullText`.
- Ghi chú/trích yếu bổ sung lấy từ các cột quy ước trong `ML` hoặc `VAN_BAN`.

## 7. Batch job

1. Người dùng upload nhiều file + chọn chế độ xử lý file gốc:
   - `copy`: tạo bản sao `.xlsm`, giữ file gốc.
   - `in-place`: chuyển tại chỗ, tự động backup `.bak`.
   - `output`: chỉ ghi vào thư mục đầu ra.
2. API lưu file vào `data/uploads/{jobId}`, tạo `BatchJob` + items, đẩy vào queue.
3. Worker xử lý tuần tự (một Excel COM instance) để tránh tranh chấp.
4. Mỗi file: inspect → convert/inject macro → setup in → verify → ghi output.
5. Kết quả:
   - ZIP chứa từng `.xlsm`.
   - Hoặc workbook gộp: một sheet mỗi năm, thêm cột `Nguồn file`.
6. Job có log, trạng thái, retry, download.

## 8. API

```text
GET    /api/health
GET    /api/overview                         # dashboard non-tech
POST   /api/inspect                          # kiểm tra 1 workbook

POST   /api/jobs/convert                     # nhiều file -> .xlsm (+ ZIP, GOP)
POST   /api/jobs/merge                       # gộp nhiều file -> GOP.xlsm
GET    /api/jobs?page=&pageSize=&status=     # danh sách job
GET    /api/jobs/{id}
POST   /api/jobs/{id}/cancel
GET    /api/jobs/{id}/download               # ZIP hoặc .xlsm
GET    /api/jobs/{id}/files/{itemId}         # từng file kết quả

GET    /api/workbooks
GET    /api/workbooks/{id}/years
GET    /api/workbooks/{id}/types
GET    /api/workbooks/{id}/dossiers?year=&type=&search=&page=&pageSize=
GET    /api/dossiers/{id}

POST   /api/sync                             # upload workbook, ML là nguồn chính
GET    /api/sync/sessions?take=
GET    /api/sync/sessions/{id}

GET    /api/dev/auth/status
POST   /api/dev/auth/setup                   # lần đầu, tạo mật khẩu PBKDF2
POST   /api/dev/auth/login                   # -> JWT HS256
GET    /api/dev/auth/me
GET    /api/dev/system
GET    /api/dev/macros
POST   /api/dev/macros                       # tạo phiên bản mới
GET    /api/dev/macros/{id}
POST   /api/dev/macros/{id}/activate
POST   /api/dev/macros/validate              # kiểm tra tĩnh
GET    /api/dev/macros/{id}/diff             # so với phiên bản trước
```

- Job chạy tuần tự qua `IBatchJobQueue` + `BackgroundService`; output nằm trong
  `data/outputs/{jobId}` (upload `data/uploads/{jobId}`, backup `data/backups/{jobId}`).
- `GET /api/jobs/{id}/download` trả ZIP khi job có nhiều file hoặc khi bật `CreateZip`;
  job gộp trả trực tiếp `GOP_*.xlsm`.
- Sync trả `SyncResponse` kèm số thêm/sửa/xoá/xung đột và mã phiên để tra cứu.

## 9. UI

SPA nằm trong `src/Stp.Web`, dùng Vue 3 + TypeScript + Vite, `vue-router` và
`@lucide/vue`. Không dùng Bootstrap/Tailwind; toàn bộ design system nằm trong
`src/style.css` (teal/indigo, biến CSS cho light/dark).

### Route chính

| Route | Màn hình |
|---|---|
| `/` | Tổng quan: số hồ sơ, workbook, job, phiên đồng bộ |
| `/chuyen-doi` | Chuyển đổi nhiều file, kiểm tra file đầu tiên, tuỳ chọn gốc/ZIP/GOP/macro |
| `/gop-file` | Gộp nhiều workbook thành `GOP.xlsm` |
| `/dong-bo` | Upload workbook đã sửa, xem kết quả và lịch sử phiên đồng bộ |
| `/kho` | Danh sách workbook |
| `/kho/:id` | Hồ sơ theo năm/loại/từ khoá, phân trang |
| `/ho-so/:id` | Chi tiết hồ sơ, văn bản, toàn văn, ghi chú |
| `/in/:id` | Bản in A4 dọc, đúng bố cục `sheet-page` cũ |
| `/job/:id` | Tiến độ, từng file, log, tải kết quả, huỷ |
| `/nang-cao` | Developer workspace nhúng trong `AppShell` |
| `/developer` | Developer workspace độc lập, có khóa |

### Non-tech

- Kéo thả nhiều file, hiển thị dung lượng, kiểm tra nhanh loại workbook.
- Trạng thái job rõ ràng, lỗi tiếng Việt, có nút thử lại/làm mới.
- Bộ lọc năm/loại/từ khoá; bản in giữ 7 cột và ngưỡng font như bản web cũ.
- Dark mode bằng `data-theme="dark"` trên `<html>`; lưu lựa chọn ở
  `localStorage["stp.theme"]`.

### Developer

- `/developer` (standalone) và `/nang-cao` (embedded) dùng chung
  `DeveloperWorkspace.vue`.
- Cổng đăng nhập: lần đầu tạo mật khẩu, các lần sau đăng nhập; JWT lưu ở
  `localStorage["stp.dev.token"]`, tự gắn header cho request `/api/dev`.
- Tab: Hệ thống, Phiên bản macro, Job & nhật ký, Đồng bộ, Kiểm tra file.
- Macro editor: tải `.bas`, sao chép, kiểm tra tĩnh, lưu phiên bản mới (kích hoạt ngay),
  kích hoạt phiên bản cũ, diff theo dòng.
- Không hiển thị mật khẩu/token; 401 tự xoá phiên và quay về cổng đăng nhập.

## 10. Bảo mật

- Không login user thường khi chạy nội bộ.
- Developer password hash PBKDF2-SHA256 (210.000 vòng lặp) trong `appsettings`
  (`DeveloperAuth:PasswordHash`) hoặc biến môi trường; nếu chưa có, lần đầu chạy
  tạo `data/runtime/developer-auth.json` (hash + Base64 JWT key cục bộ).
- JWT HS256, issuer `StpArchive.Developer`, audience `StpArchive.DeveloperUi`,
  thời hạn 480 phút; khóa ký lấy từ cấu hình/state file.
- Đăng nhập sai bị chặn tạm: 5 lần / 10 phút cho mỗi client.
- SPA giữ token trong localStorage (tiện dụng nội bộ); có thể chuyển sang memory +
  refresh cookie khi triển khai ngoài mạng nội bộ.
- Macro chỉ yêu cầu `Enable Content`; không ký số, không Trusted Location.
- Không commit dữ liệu hồ sơ, file Excel nguồn, file xuất, log chứa dữ liệu,
  `data/runtime`, token hay mật khẩu.

## 11. Lộ trình

1. **Phase 1 — Foundation**: solution, domain, EF Core, SQL Server, health check.
2. **Phase 2 — Excel engine**: COM host, inspector, convert/inject macro, print setup,
   type 2 generator, test với `DK_KET_HON.xlsx` + file mẫu giả lập.
3. **Phase 3 — Batch + API**: job queue, convert/merge, download ZIP/workbook,
   sync upload, dossiers API.
4. **Phase 4 — SPA non-tech**: Vue 3, teal/indigo, dark mode, luồng xuất file.
5. **Phase 5 — Developer UI**: inspector, macro version/diff, job logs.
6. **Phase 6 — Template engine + CI/CD**: chạy không cần Excel, GitHub Actions.

## 12. Ghi chú môi trường hiện tại

- Microsoft Office 16 (`C:\Program Files\Microsoft Office\root\Office16\EXCEL.EXE`)
  đã hết license: `Save`/`SaveAs` bị chặn, nhưng mở file và `SaveCopyAs` vẫn được.
- WPS Office 12.1.0.29162:
  - Chiếm `HKCU\Software\Classes\CLSID\{00024500-...}\LocalServer32`.
  - Có `et.exe` tại `%LOCALAPPDATA%\Kingsoft\WPS Office\12.1.0.29162\office6`.
  - Không có `vbe7.dll`; `VBProject` null, không chạy được VBA.
  - Vẫn mở/lưu `.xlsm` và giữ `vbaProject.bin` byte-for-byte.
- App dùng ROT + kiểm tra `Path` để chọn đúng host, không dùng ProgID mặc định.
- `AccessVBOM = 1` đã bật cho Office 16 trong lúc kiểm thử.
- SQL Server 16 Developer đang chạy, Windows Authentication hoạt động.
- File xuất `.xlsm` cần được mở trên máy có Excel bản quyền để chạy macro.
