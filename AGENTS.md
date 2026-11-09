# Quy tắc dự án (người dùng xác nhận — ghi nhớ)

## Sheet dữ liệu
- **Chỉ lấy sheet "file nhập"** làm sheet dữ liệu trích yếu.
- Không lấy sheet "file dán" (dữ liệu đã dán sạch nhưng cột B không khớp
  cách người dùng nhìn file), không lấy sheet in theo năm (`1990`, `1994 `...)
  hay sheet `form`.
- Chỉ khi file không có sheet `file nhập` mới lấy sheet `ML`, rồi mới lấy
  sheet đầu tiên có tiêu đề "Trích yếu nội dung".

## Ý nghĩa cột trong file DK KET HON (người dùng xác nhận)
- Cột B = hồ sơ số (số lọc lấy phần đầu dạng số, ví dụ "36A" tính là 36).
- Cột D = trích yếu nội dung.
- Cột E = Thời gian tài liệu.
- Cột F = số tờ (thực chất là ký hiệu QĐ, ví dụ 93/QD.UBT).
- Cột G = ghi chú (thực chất là số/khoảng trang, ví dụ 01-20).

Note: trong file thật, tiêu đề "Hồ sơ số" nằm ở cột C và B không có tiêu đề,
nhưng người dùng làm việc theo cột B. Bộ lọc số khớp cả B lẫn cột
"Hồ sơ số" cho đúng mọi kiểu file.

## Số tờ (cột G)
- Ở sheet "file nhập", người dùng gõ "01-24" và Excel/WPS lưu thành ngày
  01/01/2024 (serial 45292). Ứng dụng hiển thị lại dạng "MM-yy" (ví dụ
  "01-24", "01-20") thay vì số serial thô.

## Lọc "Từ số – Đến số" + năm
- Số hồ sơ + điều kiện năm kết hợp với nhau (AND).
- Kiểm chứng với `DK KET HON (4).xlsx` qua API thật: lọc 775–785 +
  năm 1994 phải ra đúng 11 dòng ở dòng 764–774 của sheet `file nhập`.
- Không đổi cách chọn sheet trở lại `file dán` nếu chưa kiểm bằng file thật.
