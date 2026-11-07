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
  web/
    stp-spa/              Vue 3 + TypeScript + Vite
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

## 8. API sơ bộ

```text
GET    /api/health
POST   /api/workbooks/inspect
POST   /api/workbooks/convert
POST   /api/workbooks/merge
GET    /api/jobs
GET    /api/jobs/{id}
GET    /api/jobs/{id}/download
POST   /api/sync/upload
GET    /api/dossiers
GET    /api/years
GET    /api/print-presets
POST   /api/print-presets
POST   /api/developer/login
GET    /api/developer/me
GET    /api/macros
GET    /api/macros/{id}
POST   /api/macros
GET    /api/macros/{id}/diff/{otherId}
POST   /api/macros/{id}/activate
```

## 9. UI

### Non-tech

- Trang chủ: chọn file, kéo thả nhiều file, nhận diện loại, xem trước số hồ sơ.
- Luồng 3 bước: `Chọn file` → `Kiểm tra & tùy chọn` → `Xuất & tải về`.
- Trạng thái job rõ ràng, lỗi bằng tiếng Việt, có hướng dẫn sửa.
- Bộ lọc năm/loại/khoảng số/từng số giống web hiện tại.
- Dark mode bằng class `dark` trên `<html>`.

### Developer

- Trang `/developer` có khóa, hoặc tab `Nâng cao` trong màn hình xuất.
- Inspector workbook: sheet, loại, số dòng, block, macro, page setup.
- Macro editor: version, diff, activate, tải `.bas`.
- Job log, raw JSON, SQL health, Excel COM health, đường dẫn file.
- Không hiển thị mật khẩu/token.

## 10. Bảo mật

- Không login user thường khi chạy nội bộ.
- Developer password hash PBKDF2 trong `appsettings` hoặc biến môi trường
  `STP_DEVELOPER_PASSWORD_HASH`.
- JWT ngắn hạn cho developer; không lưu token trong localStorage nếu có thể,
  ưu tiên memory + refresh cookie sau này.
- Macro chỉ yêu cầu `Enable Content`; không ký số, không Trusted Location.
- Không commit dữ liệu hồ sơ, file Excel nguồn, file xuất, log chứa dữ liệu.

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
