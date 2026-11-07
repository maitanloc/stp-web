# Roadmap triển khai

## Trạng thái hiện tại

- [x] Khảo sát workbook `DK_KET_HON.xlsx` (41 sheet, 22.567 hồ sơ, 36 năm).
- [x] Xác nhận cấu trúc block in 9 dòng, công thức trỏ `ML!B/E/F/G`.
- [x] Kiểm chứng Microsoft Excel 16 + COM + chèn macro + chạy macro.
- [x] Kiểm chứng WPS 12: mở/lưu `.xlsm`, không có VBA runtime.
- [x] Chốt yêu cầu và quyết định kiến trúc.
- [x] Tạo solution `StpSuite.sln` + 5 project.
- [x] Domain model + EF Core `StpArchive` + migration + `/api/health`.
- [x] `ExcelInspector` + `ExcelArchiveParser` trên file thật.
- [x] `ComExcelEngine` WPS-first + convert `.xlsx` → `.xlsm`.
- [x] `VbaProjectInjector` (OpenXML) + donor sạch + test.
- [x] `PrintLayoutService` A4 dọc + test ngưỡng thu gọn.
- [x] `Type2SheetGenerator` (ML-only) + test bằng workbook giả lập.
- [x] Convert end-to-end file loại 2 bằng WPS + donor, PDF A4 dọc.
- [x] API batch/convert/merge/jobs/download/sync + archive query.
- [x] `MergedWorkbookService` + ZIP + sao lưu file gốc + job worker tuần tự.
- [x] Đồng bộ Excel → SQL Server (thêm/sửa/xoá, last-write-wins, conflict log).
- [ ] Vue 3 SPA non-tech.
- [ ] Developer UI + macro version/diff.
- [ ] Template engine không cần Excel.
- [ ] CI/CD.

## Phase 1 — Foundation

1. Domain model:
   - `ArchiveWorkbook`, `ArchiveYear`, `Dossier`, `DocumentLine`.
   - `BatchJob`, `BatchJobItem`, `JobLog`.
   - `MacroVersion`, `SyncSession`, `SyncChange`, `PrintPreset`.
2. EF Core SQL Server:
   - `StpDbContext`, cấu hình bảng, index, `rowversion`.
   - Migration khởi tạo `StpArchive`.
3. API health + DB health.
4. `PasswordHasher`, `DeveloperAuth` JWT.

**Done khi**: `dotnet build` sạch, migration tạo DB thành công, `/api/health` trả DB OK.

## Phase 2 — Excel engine

1. `ExcelComHost`:
   - Resolve WPS/Microsoft, marker workbook + ROT.
   - Cấu hình Application, cleanup.
2. `ExcelInspector` (ClosedXML):
   - Loại workbook, sheet, năm, số hồ sơ.
3. `ComExcelEngine`:
   - Convert `.xlsx` → `.xlsm` bằng WPS hoặc Microsoft Excel.
   - Microsoft Excel: chèn macro từ `.bas` + self-test.
   - WPS: chèn VBA donor bằng OpenXML, không cần VBA runtime.
4. `PrintLayoutService`:
   - A4 dọc, cột/font, page break.
   - Export PDF kiểm tra.
5. `Type2SheetGenerator`:
   - Sinh sheet in từ ML.
6. Test với `DK_KET_HON.xlsx` + file ML-only giả lập.

**Done khi**: 1 file loại 1 + 1 file loại 2 tạo `.xlsm` mở được, macro chạy, PDF A4 dọc.

## Phase 3 — Batch + API

1. `FileStorage` (data/uploads, data/outputs, data/backups).
2. `BatchJobQueue` + `BatchJobWorker` (chạy tuần tự, khôi phục job dở khi restart).
3. API:
   - `POST /api/inspect`
   - `POST /api/jobs/convert`
   - `POST /api/jobs/merge`
   - `GET /api/jobs`, `GET /api/jobs/{id}`, `GET /api/jobs/{id}/download`
   - `POST /api/jobs/{id}/cancel`
4. ZIP + merged workbook + chế độ file gốc (copy/in-place + backup/output folder).
5. Sync:
   - `POST /api/sync`
   - `GET /api/sync/sessions`, `GET /api/sync/sessions/{id}`
   - đọc ML, cập nhật DB, ghi `SyncSession`/`SyncChange`.
6. Archive:
   - `GET /api/workbooks`, `/years`, `/types`, `/dossiers`
   - `GET /api/dossiers/{id}`.
6. Dossiers/years API cho SPA.

**Done khi**: upload 3 file, nhận ZIP + workbook gộp, job log đầy đủ; upload lại `.xlsm` cập nhật DB.

## Phase 4 — SPA non-tech

1. Vue 3 + TS + Vite + Tailwind.
2. Layout teal/indigo, dark mode.
3. Trang:
   - Tổng quan.
   - Xuất Excel (3 bước).
   - Hồ sơ / lọc / in.
   - Job của tôi.
   - Hướng dẫn.
4. API client, trạng thái loading/error rõ ràng.
5. Responsive, keyboard, aria-label.

**Done khi**: người không rành kỹ thuật tự upload và tải file trong < 5 phút.

## Phase 5 — Developer UI

1. Trang `/developer` có login.
2. Inspector workbook.
3. Macro editor + version + diff + activate.
4. Job log, raw JSON, SQL/Excel health.
5. Audit log.

**Done khi**: xem/sửa/khôi phục macro, xem log job, không lộ dữ liệu nhạy cảm.

## Phase 6 — Template engine + CI/CD

1. Template `.xlsm` sạch + OpenXML.
2. Chạy không cần Excel.
3. GitHub Actions: build, test, publish artifact.
4. Đóng gói `start-api.bat`, `start-spa.bat`.

**Done khi**: build/test tự động trên GitHub, chạy được trên máy không có Excel.

## Quy ước chất lượng

- Mỗi service có interface + implementation.
- Không trả stack trace/raw exception ra API production.
- Mọi thao tác file dùng đường dẫn tuyệt đối trong `data/`, kiểm tra path traversal.
- Mọi job có log, trạng thái, lỗi tiếng Việt.
- Không commit dữ liệu hồ sơ hoặc file Excel nguồn.
