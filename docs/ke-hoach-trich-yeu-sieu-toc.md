# Kế hoạch: Nhập trích yếu SIÊU TỐC (ĐÃ HOÀN THÀNH GĐ 0 & GĐ 1 12/11/2026)

> Trạng thái: **ĐÃ HOÀN THÀNH GIAI ĐOẠN 0 & GIAI ĐOẠN 1**.
> Toàn bộ các tính năng lười cấp 1 (chip 1-chạm, phím C như dòng trên, bộ mặc định theo loại,
> chạy hàng tự nhảy, xuất nhanh 1 click) và Voice Web Speech API (phiên đọc liên tục, lệnh miệng,
> lọc giọng nói, học lỗi nghe) đã được triển khai, kiểm thử thực tế và tích hợp hoàn chỉnh.
> Giai đoạn 2 (Whisper server) và Giai đoạn 3 (OCR / bot chat) bỏ qua theo đúng quy ước ban đầu ("chỉ làm khi cần").

## Mục tiêu

Bổ sung trích yếu hiện đúng mà chậm: **mỗi dòng** = mở picker → gõ tìm → tích
từng giấy tờ (3 bước), xuất file = 3 click + 2 modal. Mục tiêu: đưa các thao
tác hay gặp về **1 chạm hoặc 1 câu nói** — người dùng "lười" vẫn đổ hồ sơ đầy.

## Hiện trạng (tóm tắt khảo sát code 11/11/2026)

- Tool: `SummaryToolView.vue` (~3000 dòng) + `SummaryItemPicker.vue`, backend
  `SummaryToolController` (`api/summary/*`), session trong RAM + file Excel
  gốc, **không đụng DB** — DB chỉ nhận qua `POST /api/sync` → bảng `Dossiers`.
- Đã có sẵn nền móng tốt: **catalog tự học** (`CatalogItems` + đếm tần suất,
  badge ×N), **Bộ 7 chuẩn** (kết hôn), bulk áp dụng cho dòng đã tích, autosave
  draft, undo/redo, khoá dòng theo presence, phím tắt Ctrl+S, `/`, ←/→.
- **Chưa có**: mic/voice, OCR, template tự định nghĩa, chip 1-chạm, gợi ý nhóm,
  quick bar, lệnh tắt.
- Loại hồ sơ: free text + danh sách thứ tự 8 loại cố định (`DossierTypes.cs`).
- Stack frontend: Vue 3 + fetch thuần, **chưa có thư viện ghi âm nào** — voice
  sẽ là tính năng xanh (thêm mới hoàn toàn).

---

## Ý tưởng xếp theo "được nhiều / tốn ít"

### Giai đoạn 0 — Lười cấp 1 (KHÔNG cần mic, nỗ lực nhỏ, hiệu quả mỗi ngày)

| # | Ý tưởng | Trước → Sau | Nỗ lực |
|---|---------|-------------|--------|
| 0.1 | **Chip 1-chạm trong editor dòng**: các giấy tờ tần suất cao nhất (đọc từ `CatalogMeta` + bộ 7) render thành chip ngay cạnh ô trích yếu — bấm chip là thêm, khỏi mở picker | 3 bước → 1 click | S |
| 0.2 | **"Như dòng trên"**: phím `C` (hoặc nút) áp nguyên bộ giấy tờ + số của dòng liền trước — hồ sơ giấy lồng nhau thường giống nhau | gõ lại toàn bộ → 1 phím | S |
| 0.3 | **Bộ mặc định theo loại**: cấu hình mỗi loại 1 bộ giấy tờ mặc định (kết hôn → Bộ 7, khai sinh → …). Filter loại xong, nút "Áp dụng bộ mặc định cho các dòng lọc còn thiếu" — 1 click đổ cả trăm dòng | từng dòng → 1 click cho cả đống | S–M |
| 0.4 | **Chạy hàng tự nhảy**: sau khi áp 1 dòng, con trỏ tự nhảy sang dòng **còn thiếu kế tiếp** trong bộ lọc (như kiểu nhập liệu Google Sheets) | tìm dòng thiếu tay → tự động | S |
| 0.5 | **Xuất nhanh 1 click**: nút "Xuất luôn theo bộ lọc" ngay thanh chính, dùng tên file + phạm vi **lần trước** (nhớ localStorage) — khỏi đi qua 2 modal | 3 click + 2 modal → 1 click | S |
| 0.6 | **Ctrl+K thanh lệnh tắt**: gõ tắt đã học — `x7` = Bộ 7, `cmt` = Căn cước, `hk` = Sổ hộ khẩu… expand theo catalog; hỗ trợ "đại từ" cho từng loại hồ sơ | gõ dài → gõ tắt 2–4 ký tự | M |

### Giai đoạn 1 — VOICE trình duyệt ("nghe lọc loại hồ sơ bỏ vô" — đúng ý chính chủ)

**Công nghệ: Web Speech API** (`webkitSpeechRecognition`) — có sẵn trong
Chrome/Edge, **miễn phí, không cần API key, không thêm npm dependency**,
hỗ trợ `vi-VN`, chạy được trên tunnel HTTPS (ngrok đã là HTTPS ✓).

| Phương án | Chi phí | Offline? | Chất lượng vi-VN | Kết luận |
|---|---|---|---|---|
| Web Speech API | 0 | Không (Chrome đẩy qua Google, vô hình) | Khá | **Làm ngay** |
| Whisper server trên chính máy host (whisper.cpp) | Rác nhỏ CPU | Có | Tốt | GĐ 2, nếu cần |
| Cloud STT (Azure/Google key) | Phí + key | Không | Tốt nhất | Bỏ qua |

**UX "phiên đọc" (trợ lý miệng):**
- Nút mic nổi trong tool (hoặc giữ `Ctrl+M`). Bật = **chế độ đọc liên tục**:
  app tô sáng dòng đang nhập, nút nhấp nháy, người dùng chỉ việc nói.
- Câu nói chuẩn: *"kết hôn — căn cước, hộ khẩu, hai ảnh"* → parser đoán:
  loại = Đăng ký kết hôn; items = [Căn cước, Sổ hộ khẩu, Ảnh 3×4 ×2] → điền
  vào picker của dòng hiện tại, **không commit ngay** — hiện chờ duyệt.
- Lệnh trong lúc đọc: `"xong"` (commit dòng, tự nhảy dòng thiếu kế tiếp — ghép
  0.4), `"kế tiếp"`, `"quay lại"`, `"bộ bảy"`, `"mở đầu đúng mẫu"`.
- **Lọc bằng miệng**: *"lọc kết hôn"* / *"lọc khai tử năm hai không hai lẻ
  năm"* → đặt filter loại + năm (map số chữ → số: hai-không-hai-lẻ-năm = 2025).
- Mọi thứ chỉ **prefill để mắt duyệt** — sai thì thấy ngay, sửa tay như cũ;
  đường export và DB **không đổi một pixel**.

**Parser (100% frontend, không đụng backend):**
- Nguồn từ vựng: danh sách catalog session đã fetch sẵn cho picker (items +
  tần suất) + 8 loại cố định `DossierTypes` — match mờ (token overlap /
  Levenshtein ≤ 2), không cần AI.
- Tách câu: dấu phẩy / "và" / "kèm" / "có"; đoạn đầu = loại, các đoạn sau =
  giấy tờ; cụm "số tờ/tờ" + số → `SheetNumber` (đọc **từng chữ số** cho chắc:
  "không-ba-lẻ-năm" = 035).
- **Học lỗi nghe**: ghi `{nói đúng ý → máy nghe sai → người sửa}` vào
  localStorage (700ms debounce kiểu draft hiện có) — lần sau tự thay trước
  khi match. Về sau nhặt được mỗi lần mộc lại catalog.

**Hạn chế ghi rõ**: Chrome/Edge mới chạy tốt (Firefox/Safari thiếu) — voice là
bổ sung, flow tay không bao giờ bị khoá; cần mic + internet; phòng ồn sẽ giảm
độ chính xác (Whisper GĐ 2 khắc phục).

### Giai đoạn 2 — Voice chính xác + offline (chỉ làm nếu GĐ 1 chưa đủ đã)

- Whisper.cpp chạy trên máy host, endpoint `POST /api/voice/transcribe` —
  chính xác hơn, không phụ thuộc trình duyệt, hoạt động mất mạng.
- Hotkey toàn cục + tự động hoá "bó vocabulary" (grammar ràng buộc theo
  catalog → sai số giảm sâu).

### Giai đoạn 3 — Giấy → máy (nếu một ngày mệt tay chụp ảnh)

- **OCR camera** (tesseract.js WASM, free): chụp bìa hồ sơ/vài dòng chữ → tự
  điền số hồ sơ, số tờ, loại; trích yếu vẫn chọn từ catalog.
- **Bot trong chat nhóm** (SignalR sẵn có): nhắn "x7 dòng 12–40" là bot áp
  giúp — kiểu văn phòng lười tối đa.

---

## Thứ tự làm & Kết quả thực tế

| Giai đoạn | Nội dung | Trạng thái thực tế | Commit |
|---|---|---|---|
| GĐ 0 | 0.1→0.5 (chip, như dòng trên, bộ mặc định, chạy hàng, xuất nhanh) | **ĐÃ XONG** (Nhóm C1–C5) | `5439c55` |
| GĐ 1 | Voice Web Speech + parser + phiên đọc liên tục + học lỗi nghe | **ĐÃ XONG** (Nhóm I1–I2) | `65c378d` |
| GĐ 1.5 | Ctrl+K command palette + tìm kiếm hồ sơ + đổi theme | **ĐÃ XONG** (Nhóm H1–H2) | `2d642dd` |
| GĐ 2/3 | Whisper server / OCR camera / bot chat | **BỎ QUA THEO THỎA THUẬN** (chỉ làm khi cần) | — |

## Rủi ro & nguyên tắc

- Voice **chỉ prefill**, luôn có mắt duyệt trước khi commit — sai không lo đổ
  dữ liệu sai vào Excel/DB.
- Không thêm npm dependency nào ở GĐ 0–1 (Web Speech là native).
- Flow cũ giữ nguyên 100% — tính năng mới là lớp phủ, không thay thế.
- Test GĐ 0 dạng unit cho parser/shortcut; voice test tay + log chính xác %.
- Nếu mở voice cho người lạ tunnel: chỉ nhận mic sau khi đăng nhập (gate đã
  có sẵn), không lưu âm thanh — chỉ giữ text đã nghe.
