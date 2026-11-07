# VBA donor — tạo và kiểm tra

Donor là một workbook `.xlsm` sạch dùng để lấy `xl/vbaProject.bin` cho đường
WPS/OpenXML. Donor **không** chứa dữ liệu hồ sơ; chỉ có:

- `ThisWorkbook`: mã `StpArchiveMacro.bas` (223 dòng, có `StpSelfTest`).
- Một sheet module rỗng (`Sheet1`).
- Không standard module, không sheet nghiệp vụ.

## 1. Vì sao donor chỉ có 1 sheet module

Khi chèn `vbaProject.bin` vào file đích:

- Nếu donor có **nhiều** sheet module hơn số sheet của file đích, các module
  không gắn được sẽ thành module mồ côi. Excel vẫn mở file nhưng macro có thể
  lỗi `0x800A0009` (Subscript out of range).
- Nếu donor có **ít hơn hoặc bằng**, Excel tự tạo module cho các sheet còn lại
  khi mở file. Macro chạy bình thường.

Vì vậy donor production cố định 1 sheet module. `VbaProjectInjector` gán module
này cho sheet đầu tiên (hoặc sheet trùng tên), các sheet còn lại bỏ `codeName`
để Excel tự xử lý.

## 2. Tạo donor trên máy có Microsoft Excel bản quyền

1. Mở Excel, tạo workbook mới có **1 sheet**.
2. `Alt + F11` → chọn `ThisWorkbook`.
3. Dán mã từ `src/Stp.Infrastructure/Excel/Templates/StpArchiveMacro.bas`
   (có thể dùng bản ASCII để tránh lỗi code page).
4. Đảm bảo có `Public Sub StpSelfTest()`.
5. `File > Save As` → `Excel Macro-Enabled Workbook (*.xlsm)` →
   `stp-donor-clean.xlsm`.
6. Kiểm tra:
   - `VBProject.VBComponents.Count = 2` (`ThisWorkbook` + `Sheet1`).
   - `ThisWorkbook.CodeModule.CountOfLines = 223`.
   - `Application.Run("ThisWorkbook.StpSelfTest")` chạy không lỗi.
7. Giải nén `.xlsm`, lấy `xl/vbaProject.bin`.
8. Chép đè `src/Stp.Infrastructure/Excel/Templates/vbaProject.bin`.
9. Cập nhật `vba-donor.json` nếu codeName/số dòng thay đổi.

## 3. Tạo donor trên máy Excel đã hết license (máy hiện tại)

`Workbook.SaveAs` bị chặn nhưng `Workbook.SaveCopyAs` vẫn chạy. Quy trình đã
dùng:

1. Tạo một `.xlsm` 1 sheet sạch bằng WPS (không macro):
   - WPS mở `.xlsx` 1 sheet → `SaveAs(..., 52)`.
2. Dùng Microsoft Excel COM mở file `.xlsm` đó.
3. Ghi mã macro vào `ThisWorkbook` qua `VBProject` (đọc/ghi trong bộ nhớ được).
4. `Workbook.SaveCopyAs(stp-donor-clean.xlsm)`.
5. Kiểm tra bằng cách mở lại donor: 2 components, `SelfTest RUN OK`.

Lưu ý: mã VBA lưu theo code page ANSI của Windows. Trên máy không phải locale
tiếng Việt, dấu tiếng Việt trong **comment** có thể thành `?`. Mã lệnh vẫn
ASCII nên không ảnh hưởng chức năng. Vì vậy donor hiện tại dùng bản ASCII của
`StpArchiveMacro.bas`.

## 4. Kiểm tra sau khi thay donor

Chạy test:

```powershell
dotnet test tests\Stp.Tests\Stp.Tests.csproj
```

Kiểm tra thủ công trên máy có Excel:

1. Convert một file thật bằng WPS (engine sẽ tự chèn donor).
2. Mở file `.xlsm` bằng Microsoft Excel.
3. Xác nhận:
   - Số sheet giữ nguyên.
   - `VBProject.VBComponents.Count = số sheet + 1`.
   - `ThisWorkbook.CodeModule.CountOfLines = 223`.
   - `Application.Run("ThisWorkbook.StpSelfTest")` chạy được.
   - Không có hộp thoại repair.

## 5. Cấu hình ghi đè

`appsettings.json`:

```json
{
  "ExcelVba": {
    "Enabled": true,
    "DonorWorkbookPath": "D:\\STP\\stp-donor-clean.xlsm"
  }
}
```

Nếu `DonorWorkbookPath` trống hoặc không tồn tại, engine dùng donor đóng gói
trong assembly. Đường dẫn ghi đè hữu ích khi developer muốn thử donor mới mà
không rebuild app.
