# Excel Engine — thiết kế và kiểm chứng

## 1. Kết quả kiểm chứng trên máy hiện tại

- Microsoft Excel 16.0 (Office 16 Click-to-Run) vẫn mở được file, nhưng license đã hết hạn:
  - `Workbook.Save`, `Workbook.SaveAs` bị chặn với lỗi
    “This command is unavailable because the license to use this application has expired.”
  - `Workbook.SaveCopyAs` vẫn chạy. Đã dùng đặc điểm này để tạo donor sạch.
  - `VBProject` vẫn đọc/ghi được trong bộ nhớ (dùng cho kiểm thử trên máy này).
- WPS Office 12 chiếm ProgID `Excel.Application` và CLSID
  `{00024500-0000-0000-C000-000000000046}` trong HKCU.
- `New-Object -ComObject Excel.Application` trả về WPS, không phải Microsoft Excel.
- WPS 12.1.0.29162 **không có VBA runtime** (`vbe7.dll` không tồn tại):
  - `Workbook.VBProject` trả về null.
  - `Application.Run("ThisWorkbook.StpSelfTest")` lỗi `0x800A03EC`.
  - WPS vẫn mở và `SaveAs(path, 52)` được file `.xlsm`; giữ nguyên `vbaProject.bin`
    có sẵn byte-for-byte.
- Đã chuyển bản sao `DK_KET_HON.xlsx` (5,8 MB, 41 sheet) sang `.xlsm` bằng WPS
  trong ~24–34 giây.
- Đã chèn VBA donor bằng OpenXML/ZIP, không cần Excel:
  - File kết quả mở bằng Microsoft Excel: 41 sheet, **42 VBA components**
    (`ThisWorkbook` + 41 sheet module), `ThisWorkbook` 223 dòng.
  - `Application.Run("ThisWorkbook.StpSelfTest")` chạy thành công.
  - Không bị Excel repair, không có module mồ côi.
- Lưu ý: máy này không chạy thử được macro trong WPS. Việc xác nhận macro phải
  thực hiện trên máy có Excel bản quyền (hoặc WPS có plugin VBA).

## 2. Luồng COM và WPS

```text
1. Resolve host:
   - Auto: ưu tiên WPS (máy hiện tại), fallback Microsoft Excel.
   - Wps: bắt buộc WPS.
   - Microsoft: bắt buộc Microsoft Excel.
2. Tạo marker workbook rỗng (.xlsx) trong thư mục tạm.
3. Process.Start(host, "/Automation \"marker.xlsx\"").
4. Chờ host đăng ký ROT.
   - WPS launcher có thể thoát ngay; engine tiếp tục poll ROT thay vì fail.
5. Enumerate ROT:
   - Bind từng moniker.
   - Tìm object có Workbooks chứa marker.FullName.
   - Lấy Application, kiểm tra Path đúng host:
     - WPS: chứa "Kingsoft" hoặc "WPS Office".
     - Microsoft: chứa "Microsoft Office" và không chứa Kingsoft/WPS.
6. Cấu hình Application (best-effort; WPS không hỗ trợ mọi tùy chọn):
   - Visible = false
   - DisplayAlerts = false
   - AutomationSecurity = 1 (Low)
   - AskToUpdateLinks = false
   - ScreenUpdating = false
   - EnableEvents = false
   - Calculation = xlCalculationManual (-4135)
7. Mở từng workbook cần xử lý bằng Workbooks.Open(path).
8. Xử lý:
   - Microsoft Excel: SaveAs(.xlsm, 52), chèn macro qua VBProject, setup in, verify.
   - WPS: SaveAs(.xlsm, 52), đóng file; sau đó chèn VBA donor bằng OpenXML.
9. Đóng workbook, đóng marker, Application.Quit().
10. Release COM, kill process nếu chưa thoát sau timeout.
```

### Vì sao dùng marker + ROT

- `GetActiveObject("Excel.Application")` trả về WPS nếu WPS đang chạy.
- `AccessibleObjectFromWindow` không ổn định khi Excel đang ở màn hình
  `Unlicensed Product`/splash.
- ROT cho phép bind trực tiếp tới từng instance và kiểm tra `Application.Path`.
- Marker file duy nhất đảm bảo không lấy nhầm instance Excel khác của người dùng.

### Vì sao vẫn dùng WPS

- Máy phát triển hiện tại không có license Microsoft Excel.
- WPS mở/lưu `.xlsm`, giữ nguyên dữ liệu và `vbaProject.bin` ổn định.
- Các thao tác nặng (mở, SaveAs, in, PDF) dùng được WPS.
- Phần VBA được xử lý ngoài WPS bằng OpenXML, nên không phụ thuộc VBA runtime.

## 3. Chèn macro

### 3.1 Microsoft Excel COM (khi có license)

- Mở file `.xlsx` bình thường.
- `SaveAs(path.xlsm, 52)` trước khi chèn macro.
- Lấy `Workbook.VBProject.VBComponents("ThisWorkbook")`.
- Xóa code cũ nếu có, `CodeModule.AddFromString(macroCode)`.
- Macro phải giữ:
  - `Workbook_Open`
  - `Workbook_SheetActivate`
  - `Workbook_SheetFollowHyperlink`
  - `Workbook_SheetChange`
  - `NhayNam`, `LocBangIn`, `IsBangIn`
- Nếu cần macro tiện ích, thêm standard module riêng, không trộn vào `ThisWorkbook`.
- Kiểm tra compile/run:
  - Thêm tạm `Public Sub StpSelfTest()`.
  - `Application.Run("ThisWorkbook.StpSelfTest")`.
  - Xóa test sub, lưu lại.
- Macro code nguồn đặt tại `src/Stp.Infrastructure/Excel/Templates/StpArchiveMacro.bas`.

### 3.2 WPS/không có VBA runtime — chèn `vbaProject.bin` bằng OpenXML

WPS không có `vbe7.dll` nên không thể tạo VBA project. Cách làm:

1. WPS mở file nguồn, `SaveAs(output.xlsm, 52)`, đóng file.
2. `VbaProjectInjector` mở `.xlsm` như ZIP/OpenXML:
   - Đọc `xl/workbook.xml` và relationships để biết từng sheet.
   - Đọc donor `vbaProject.bin` + bản đồ sheet module.
   - Gán `sheetPr@codeName` cho các sheet khớp donor (ưu tiên khớp tên, sau đó
     theo thứ tự).
   - Sheet không có module donor: **bỏ** `codeName` để Excel tự tạo module khi mở.
   - Đặt `workbookPr@codeName = ThisWorkbook`.
   - Thêm content type `application/vnd.ms-office.vbaProject`,
     relationship `.../vbaProject`, và entry `xl/vbaProject.bin`.
3. Ghi file tạm rồi thay thế file đích (atomic trên cùng ổ đĩa).

Donor đóng gói trong assembly:

- `Excel/Templates/vbaProject.bin` — 25.600 byte, gồm `ThisWorkbook` (223 dòng)
  và một sheet module rỗng `Sheet1`.
- `Excel/Templates/vba-donor.json` — workbook codeName + danh sách sheet module.
- Donor sạch, không chứa dữ liệu hồ sơ. Có thể ghi đè bằng
  `ExcelVba:DonorWorkbookPath` trỏ tới một `.xlsm` donor khác.

Quy tắc quan trọng:

- **Không để module mồ côi.** Nếu donor có nhiều sheet module hơn file đích,
  Excel có thể mở được nhưng macro không chạy (`0x800A0009`). Donor production
  chỉ có 1 sheet module; các sheet còn lại do Excel tự tạo.
- **Không chèn macro bằng cách chỉ thêm `vbaProject.bin`.** Phải khôi phục
  `workbookPr@codeName` và `sheetPr@codeName`, nếu không Excel tạo module trùng
  và macro không chạy.
- WPS không chạy thử được macro. Trường `MacroVerified` trả `false`; cần mở
  bằng Excel bản quyền để xác nhận (file mẫu đã được xác nhận `SelfTest OK`).

### 3.3 Khi sửa macro

- Sửa `StpArchiveMacro.bas`.
- Chạy lại công cụ tạo donor trên máy có Excel (xem `docs/vba-donor.md`).
- Thay `vbaProject.bin` + cập nhật `vba-donor.json` nếu số dòng thay đổi.
- Với máy có Microsoft Excel bản quyền, engine vẫn chèn trực tiếp qua COM nên
  không cần rebuild donor; donor chỉ dùng cho đường WPS/OpenXML.

## 4. Macro hiện có

Macro gốc `D:\STP\Macro_chon_in_hang_loat.txt` gồm:

- Hằng số `FIRST_ROW = 8`, `BLOCK = 9`.
- Nhận diện sheet in qua `K1 = BANG_IN_HANG_LOAT`.
- Lọc theo `A3`:
  - `Tất cả hồ sơ`
  - `Từ số - đến số` (`A5`, `C5`)
  - `Từng số` (`D5`)
- Ẩn block bằng `AutoFilter` cột `L`.
- Chuyển năm qua `E5`.
- Xem trước qua hyperlink `E3`.
- Bấm hyperlink cột D sheet `ML` để mở `FORM`.

Macro mới cần bổ sung:

- `SETUP` sheet: khổ giấy, hướng, lề, scale, font, cột/dòng, header/footer, watermark.
- `DASHBOARD`: chọn năm, loại, khoảng số, thống kê, nút xuất.
- `VAN_BAN`: toàn văn theo hồ sơ, in tùy chọn.
- Nút `Đồng bộ lên web`: gọi API qua `MSXML2.ServerXMLHTTP` hoặc `WinHttp.WinHttpRequest.5.1`.
- Ghi log đồng bộ vào sheet `SYNC_LOG`.
- Giữ tương thích Excel 2007: không dùng API mới hơn, không dùng `WorksheetFilter` mới.

## 5. In A4 dọc

### 5.1 Thông số

```text
Orientation = xlPortrait (1)
PaperSize = xlPaperA4 (9)
PrintArea = "$A$8:$G$<lastRow>"
Margins: Left/Right 0.4", Top/Bottom 0.5", Header/Footer 0.3"
CenterHorizontally = true
Header = preset.HeaderText
Footer = preset.FooterText
```

### 5.2 Cột và cỡ chữ

Độ rộng cột lấy theo tỷ lệ bản in web (A4 dọc, 7 cột):

| Cột | Nội dung | Độ rộng Excel |
|---|---|---|
| A | STT | 5,5 |
| B | Số ký hiệu | 12,5 |
| C | Ngày tháng | 11,5 |
| D | Trích yếu | 40 |
| E | Tác giả | 15,5 |
| F | Tờ số | 7,5 |
| G | Ghi chú | 4,5 |

- Wrap text, căn giữa, font Times New Roman.
- Cỡ chữ theo ngưỡng giống web:
  - `< 550` ký tự và `< 4` dòng → normal (bảng 9,5pt, tiêu đề 15pt).
  - `>= 550` hoặc `>= 4` dòng → compact (9pt / 15pt).
  - `>= 850` hoặc `>= 8` dòng → tight (8,5pt / 14pt).
  - `>= 1200` hoặc `>= 12` dòng → tiny (8pt / 13pt).
- Chiều cao dòng dữ liệu ước lượng theo số dòng wrap. Nếu cả block vượt
  `PrintLayout:MaxBlockHeightPoints` (mặc định 740pt), cỡ chữ tiếp tục giảm
  theo bước 0,5pt đến sàn 6pt; không cắt dữ liệu.
- Mỗi block 9 dòng, page break trước mỗi block kể từ block thứ 2.
- Tiêu đề `D8`, phụ đề `D9` (công thức trỏ `ML!$B<row>`), header bảng dòng 10,
  dữ liệu dòng 11, tổng số tờ dòng 12, 4 dòng trống.
- Vùng in `A8:G<last>`, `PagesWide = 1`, `PagesTall = 0`, lề trái/phải 0,4",
  trên/dưới 0,5", header/footer 0,3", căn giữa ngang.

### 5.3 Kiểm tra

- `ExportAsFixedFormat(0, pdfPath)` cho một sheet/năm.
- Đọc `/MediaBox` của PDF: phải là A4 dọc (595 × 842 pt).
- Đếm số trang = số hồ sơ.
- Kiểm tra trực quan 1–2 hồ sơ có trích yếu dài nhất.

## 6. Sinh sheet in cho file loại 2

File loại 2 chỉ có `ML`. `Type2SheetGenerator` sinh sheet in bằng ClosedXML,
không cần Excel:

```text
Với mỗi năm trong ML:
  - Tạo sheet tên năm (hoặc "1999 (1)" nếu trùng).
  - K1 = BANG_IN_HANG_LOAT; K2:K4 = danh sách cách chọn; K6 = số hồ sơ;
    K7 = COUNTIF(D8:Dlast,"Hồ sơ số:*"); K10:K13 = thông báo;
    K15/K16 = số nhỏ nhất/lớn nhất; L7 = LOC; L8... = 1 cho từng dòng block.
  - D8 = "MỤC LỤC VĂN BẢN"; D9 = công thức "Hồ sơ số: " & ML!$B<row>.
  - Header 7 cột tại dòng 10; dòng 11 là công thức trỏ ML!B/E/F/G; dòng 12 tổng số tờ.
  - 4 dòng trống; lặp block 9 dòng cho mỗi hồ sơ.
  - PageSetup A4 dọc, print area A8:G<last>, page break trước mỗi block.
  - Ẩn cột H:L (cột trợ giúp K/L vẫn có dữ liệu cho macro).
  - Hyperlink E3 trỏ về chính sheet; dropdown A3 (K2:K4) và E5 (DS!A1:A<count>).
```

- Tạo sheet `DS` (ẩn) chứa danh sách tên sheet năm để dropdown E5 hoạt động.
- Nếu file đã có sheet in, generator giữ nguyên bố cục và báo `Generated = false`.
- Nếu file không có ML, trả `Success = false` kèm cảnh báo tiếng Việt.
- `ComExcelEngine` gọi generator trước khi mở COM; file tạm được xoá sau khi
  convert xong.

Kiểm chứng end-to-end (workbook ML-only giả lập 2 năm, 3 hồ sơ):
- WPS chuyển `.xlsx` → `.xlsm` trong ~6 giây, chèn donor 25.600 byte, 223 dòng macro.
- File kết quả có 4 sheet (`ML`, `DS`, `1990`, `1991`), loại 1, macro = true.
- PDF từng sheet: `1990` = 2 trang, `1991` = 1 trang, MediaBox 595,44 × 841,68 pt
  (A4 dọc), đúng một hồ sơ một trang.

## 7. File gộp

- Một sheet mỗi năm.
- Cột thêm `Nguồn file` (cột H hoặc cột ẩn).
- Giữ tất cả hồ sơ trùng; báo cáo trùng trong `JobLog`.
- Với file gộp, sheet in cần thêm dòng nguồn dưới tiêu đề hoặc cột ẩn.

## 8. Kiểm thử tối thiểu

| Test | Kỳ vọng | Trạng thái |
|---|---|---|
| Inspect `DK_KET_HON.xlsx` | 41 sheet, 36 năm, 22.567 hồ sơ, loại 1 | Đạt |
| Inspect file ML-only giả lập | loại 2, đúng số năm/hồ sơ, 0 sheet in | Đạt (test `Type2SheetGeneratorTests`) |
| Convert 1 file loại 1 bằng WPS | `.xlsm` mở được, có `vbaProject.bin` 25.600 byte | Đạt (23,5 giây) |
| Mở file convert bằng Microsoft Excel | 41 sheet, 42 VBA components, `SelfTest RUN OK` | Đạt |
| Convert file loại 2 bằng WPS + donor | 4 sheet (`ML`, `DS`, 2 năm), macro 223 dòng | Đạt (6,1 giây) |
| Convert 10 file | ZIP đủ 10 file, job success | Chưa làm |
| Merge 3 file | 36 sheet năm, có cột nguồn, không mất hồ sơ | Chưa làm |
| Print setup | PDF A4 dọc 595×842pt, 1 hồ sơ/trang | Đạt (2 hồ sơ = 2 trang) |
| Unit test layout | Ngưỡng fit, chiều cao dòng, cỡ chữ, print area | Đạt (25 test) |
| Sync upload | cập nhật đúng ML, tạo SyncSession/SyncChange | Chưa làm |
| Macro version/diff | lưu version, diff đúng, activate đúng | Chưa làm |
