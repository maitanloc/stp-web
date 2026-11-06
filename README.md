# STP Web - Lọc, kiểm tra và in mục lục hồ sơ

Bản web hóa quy trình Excel + macro `BANG_IN_HANG_LOAT`:

- Import **toàn bộ sheet ML** của file `DK_KET_HON.xlsx` / `.xlsm`, không chỉ các sheet in.
- Phân hồ sơ theo **năm** và **loại hồ sơ**; mỗi năm hiển thị đủ loại, số lượng từng loại và tổng số.
- Các sheet in năm được dùng để bổ sung tác giả, ghi chú, tiêu đề block; hồ sơ chỉ có trong ML vẫn được giữ.
- Lọc theo năm, loại, khoảng số hoặc danh sách số tùy chọn giống macro.
- Xem chi tiết từng hồ sơ, kiểm tra nhanh dữ liệu, điều hướng hồ sơ trước/sau.
- Thống kê theo năm/loại, ma trận năm × loại, kiểm tra dữ liệu thiếu và **xuất CSV**.
- In mỗi hồ sơ thành **một trang A4 dọc riêng**, trắng đen, đúng bố cục `MỤC LỤC VĂN BẢN`.
- Giữ nguyên các số hồ sơ đặc biệt như `346/1`, `350/1`, `355/1`, `/A`.

## 1. Yêu cầu

- Windows có cài **.NET 10 SDK**.
- Trình duyệt web (Chrome, Edge, Firefox...).
- File Excel nguồn có sheet `ML`. Các sheet năm có ô `K1 = BANG_IN_HANG_LOAT` là phần bổ sung, không bắt buộc.

Kiểm tra .NET:

```powershell
dotnet --version
```

## 2. Chạy nhanh

Mở thư mục dự án (thư mục chứa `StpWeb.csproj`), chạy:

```powershell
dotnet run --project StpWeb.csproj --urls "http://localhost:5080"
```

Sau đó mở trình duyệt tại:

```text
http://localhost:5080
```

Hoặc nhấp đúp `start.bat` (nếu có) để tự mở server và trình duyệt.

## 3. Cách sử dụng

1. Vào **Nhập Excel**.
2. Chọn file `DK_KET_HON.xlsx` rồi bấm **Bắt đầu import**.
3. Vào **In hồ sơ**, chọn **năm** và **loại hồ sơ** cần in.
4. Chọn một trong ba cách:
   - **Cả năm / loại**: in toàn bộ hồ sơ trong phạm vi đang chọn.
   - **Từ số – Đến số**: nhập khoảng, ví dụ `545` đến `550`.
   - **Các số tùy chọn**: nhập danh sách, ví dụ `85, 90, 100-105`.
5. Bấm **XEM TRƯỚC KHI IN**.
6. Trong cửa sổ in của trình duyệt:
   - Khổ giấy: **A4**.
   - Hướng giấy: **Portrait (dọc)**.
   - Màu: **Đen trắng** nếu máy in hỏi.
   - Tắt header/footer của trình duyệt nếu không cần.
   - Chọn **Save as PDF** nếu muốn lưu file PDF.

### Kiểm tra và xuất CSV

Vào **Thống kê**:

- Xem tổng hợp từng năm: số hồ sơ, số loại, danh sách loại kèm số lượng, sổ in gốc.
- Xem ma trận năm × loại hồ sơ.
- Lọc các trường hợp cần kiểm tra: thiếu ngày, thiếu số ký hiệu, không xác định tổng tờ, dòng tổng hợp, số hồ sơ cần suy luận, chưa có bản in Excel.
- Tìm kiếm theo số hồ sơ, trích yếu, số ký hiệu, tác giả...
- Bấm **Xuất CSV kết quả đang lọc** để đối chiếu với Excel.

### Xem chi tiết hồ sơ

Bấm vào số hồ sơ ở bảng xem trước hoặc trang Thống kê:

- Xem đầy đủ hộp số, hồ sơ số, loại, nhóm, dòng ML, nguồn in, tổng số tờ.
- Xem toàn bộ dòng văn bản, trích yếu gốc và ghi chú.
- Checklist kiểm tra nhanh dữ liệu.
- Điều hướng hồ sơ trước/sau trong cùng năm hoặc cùng loại.
- In riêng hồ sơ, in cả loại hoặc in cả năm.

## 4. Định dạng bản in

Mỗi hồ sơ được in trên một trang A4 dọc, chỉ dùng màu đen/trắng:

- Tiêu đề căn giữa: **MỤC LỤC VĂN BẢN**.
- Dòng căn giữa: **Hồ sơ số: ...** (giữ đúng số gốc, kể cả `346/1`, `/A`).
- Bảng 7 cột đúng thứ tự Excel: STT, Số ký hiệu, Ngày, Trích yếu, Tác giả, Tờ số, Ghi chú.
- Dòng cuối bảng: **Tổng số tờ: ... tờ** (hiện `… tờ` nếu không xác định được).
- Dòng kẻ đen, nền trắng, font Times New Roman.
- Mỗi hồ sơ tự động ngắt sang trang mới.
- Nội dung dài tự xuống dòng; các mức chữ thu gọn chỉ giảm cỡ chữ/khoảng đệm, không cắt bớt văn bản.

## 5. Dữ liệu

- Dữ liệu import được lưu tại `App_Data/archive.json`.
- `App_Data/archive.json` và file Excel nguồn **không được đưa lên Git** vì chứa dữ liệu hồ sơ cá nhân; mỗi máy tự import file Excel gốc.
- Mỗi lần import file mới sẽ **thay thế toàn bộ** dữ liệu cũ.
- Có thể xóa dữ liệu trong trang **Nhập Excel** bằng nút **Xóa toàn bộ dữ liệu đã import**.
- Số liệu hiện tại của workbook gốc: **36 năm (1990–2025), 8 loại hồ sơ, 22.567 hồ sơ**.
- Trong đó **20.836 hồ sơ** có block in Excel (các sheet 1990–2016); **1.731 hồ sơ** chỉ có trong ML (gồm các năm 2017–2025 và phần bổ sung của ML).
- Các năm có số lượng lớn: 2004 = 1.525; 2003 = 1.269; 2002 = 1.267; 2015 = 1.174; 2001 = 1.150; 1999 = 1.137.
- 8 loại: Đăng ký kết hôn, Thay đổi tên & khai sinh, Đăng ký nuôi con nuôi, Con ngoài giá thú, Đăng ký khai sinh, Đăng ký khai tử, Xác nhận Quốc tịch, Ghi chú Kết hôn.

### Ghi chú dữ liệu đặc biệt

- `1999 (1)`, `1999 (2)`... được gộp vào cùng năm 1999; tên các sổ in gốc vẫn lưu trong `SourceVolumes`.
- Dòng ML 687 (năm 1994) có cột B trống, nằm giữa 699 và 701: giữ nhãn in theo dữ liệu gốc, gán số `700` để lọc/sắp xếp.
- Dòng ML 1095 (năm 1995) có cột B = `/A`: giữ nhãn `/A` khi in, gán số `1103` để lọc/sắp xếp.
- Các dòng tiêu đề `NĂM ...` hoặc `* Loại hồ sơ` có thể vẫn có giá trị ở cột B; bộ đọc kiểm tra cột D trước để không nhầm thành hồ sơ.
- Dòng tổng hợp (`C` bắt đầu bằng `HS`, không có số ký hiệu) được giữ lại và đánh dấu `IsAggregate`.

## 6. Cấu trúc chính

```text
StpWeb/
├─ Models/ArchiveModels.cs          # Dossier, DocumentLine, ArchiveBook, thống kê loại...
├─ Services/ExcelImportService.cs   # Đọc toàn bộ ML + quét block in các sheet năm
├─ Services/DossierFilter.cs        # Mô phỏng logic lọc của macro
├─ Services/ArchiveStore.cs         # Lưu/đọc App_Data/archive.json
├─ Pages/Index.cshtml               # Chọn năm/loại, lọc, xem danh sách
├─ Pages/Print.cshtml               # Bản in A4 dọc từng hồ sơ
├─ Pages/Statistics.cshtml          # Thống kê năm × loại, kiểm tra, xuất CSV
├─ Pages/Dossier.cshtml             # Chi tiết hồ sơ, checklist, điều hướng
├─ Pages/Import.cshtml              # Upload Excel
├─ Pages/Guide.cshtml               # Hướng dẫn sử dụng
├─ wwwroot/css/site.css             # CSS bản in A4 dọc, trắng đen
└─ App_Data/archive.json            # Dữ liệu đã import
```

## 7. Build / publish

Build:

```powershell
dotnet build StpWeb.csproj
```

Publish bản Release:

```powershell
dotnet publish StpWeb.csproj -c Release -o publish
```

Chạy bản publish:

```powershell
dotnet "publish\StpWeb.dll" --urls "http://localhost:5080"
```

## 8. Ghi chú kỹ thuật

- Dùng **ASP.NET Core Razor Pages (.NET 10)** và **ClosedXML** để đọc Excel, không cần cài Excel hay bật macro.
- Giao diện dùng **Tailwind CSS** + **Font Awesome**.
- Bản in dùng CSS `@page { size: A4 portrait; }` và `break-after: page` cho từng hồ sơ.
- File upload được cấu hình tối đa 100 MB; workbook gốc khoảng 5,5 MB.
- Các ô công thức trong sheet in trỏ sang `ML!$B<row>` được đọc trực tiếp từ sheet `ML` để lấy dữ liệu gốc.
- Trang Thống kê xuất CSV UTF-8 có BOM để mở đúng tiếng Việt trong Excel.
