# Phân tích UX & đề xuất phát triển — Công cụ trích yếu (StpWeb)

> Ngày phân tích: 2026-11-10 (cập nhật cùng ngày: thêm mục 6 — danh mục tự học
> cho bộ hồ sơ lạ) · Nguồn số liệu: SQL Server `StpArchive` (kho đã đồng bộ
> 3 workbook thật), file thật `DK KET HON (4).xlsx` (22.707 hồ sơ), code UI
> `SummaryToolView.vue` + backend `SummaryToolService.cs`.
>
> **Trạng thái: đã triển khai tầng 1–2 (10/11/2026)** — bộ 7 chuẩn + badge ×N gợi ý
> tích sẵn + "danh mục học được" (mục tự gõ = tự lưu DB, seed ≥50 từ Kho) đã code
> xong, test 68/68, đã chạy thật end-to-end. Chi tiết ở mục 7. Phần chưa làm: preset
> `CatalogSet`, piggyback UsageCount khi export, panel dev-review band 2–49.

---

## 1. Bức tranh dữ liệu thật (không phỏng đoán)

### 1.1 Loại hồ sơ → cái nào "hay ra nhất"

| Loại hồ sơ              | Số hồ sơ | Tỷ lệ    |
|-------------------------|---------:|----------|
| Đăng ký kết hôn         |   62.579 | **92,4%**|
| Ghi chú Kết hôn         |    3.267 | 4,8%     |
| Thay đổi tên & khai sinh|      630 | 0,9%     |
| Đăng ký nuôi con nuôi   |      576 | 0,8%     |
| Đăng ký khai sinh       |      453 | 0,7%     |
| Con ngoài giá thú       |       96 | 0,1%     |
| Xác nhận Quốc tịch      |       66 | 0,1%     |
| Đăng ký khai tử         |       51 | 0,1%     |

Tổng: 67.718 hồ sơ (sqlite sang SQL Server chuẩn hoá tên loại đóng dấu chuẩn `DossierTypes.Preferred`).

**Kết luận 1:** Mọi thứ thiết kế "mặc định" phải xoay quanh **Đăng ký kết hôn** — nó gấp
~19 lần loại nhị. Ghi chú kết hôn là nhóm nhị, còn lại tổng <2%.

### 1.2 Danh sách giấy tờ nằm trong ngoặc cuối trích yếu

Chỉ **21.132 / 67.718 (31,2%)** hồ sơ có dạng `Câu (item1, item2, ...)` — **100% trong đó
thuộc Đăng ký kết hôn**. Các loại khác trích yếu chỉ là câu thuần (kiểu
"Hồ sơ trích lục ghi chú kết hôn có yếu tố nước ngoài của BÀ… và Ông… . Năm 2012").

### 1.3 Bộ giấy tờ chuẩn 7 (chiếm 98,3% các dòng có danh sách)

Phân phối số items mỗi dòng: **7 items = 20.798 hồ sơ**; 8–16 items chỉ 334 dòng;
1–6 items ~28 dòng. Tần suất từng item:

| Item                        | Số lần xuất hiện |
|-----------------------------|-----------------:|
| giấy khai sinh              |           21.102 |
| sơ yếu lý lịch              |           21.090 |
| bản công hàm                |           21.060 |
| phiếu đề xuất               |           21.042 |
| hộ khẩu                     |           20.954 |
| giấy khai đăng ký kết hôn   |           20.842 |
| chứng chỉ chuyên khoa       |           20.818 |
| (ngoài bộ): đơn xin đăng ký kết hôn |      308 |
| phiếu khám sức khỏe         |           288    |
| CMND                        |           250    |
| phiếu nhập xuất cảnh        |           226    |
| đơn xin xác nhận độc thân   |           166    |
| hộ chiếu                    |           136    |
| giấy xác nhận độc thân      |           116    |
| giấy chứng nhận đăng ký nhân khẩu |     106    |
| giấy chứng nhận không có trú ngụ (kết hôn) | 72 |
| thể thực                    |            60    |

**Kết luận 2:** Có một **bộ chuẩn 7** luôn cùng xuất hiện — đây chính là "template".
Bộ phụ (độc thân, hộ chiếu, nhập xuất cảnh, CMND…) phủ <2% và mang tính ngoại lệ per-case.

**Kết luận 3 (cơ hội lớn):** 41.447 hồ sơ ĐK kết hôn (**66%+ của loại này**) hiện
*chưa có* danh sách giấy tờ trong ngoặc — chính là nhóm người dùng cần chuẩn hoá hàng loạt.

### 1.4 Cấu trúc câu trích yếu

Nguyên thức lặp lại rất ổn định:
`Hồ sơ [về việc / trích lục] … của [BÀ <tên nữ>] và [Ông <tên nam>]. Năm <NNNN>`
— cùng một câu sát nghĩa sẽ không trùng (tên khác nhau), nên giá trị UI nằm ở **mẫu câu**
chứ không phải gợi ý câu nguyên văn.

### 1.5 Chất lượng dữ liệu nhập

- `TrichYeu` trống: **0 dòng** — nền dữ liệu sạch.
- Có `Tác giả` (TacGia): **100%** dòng.
- Có `Số ký hiệu`: **89%** (60.319/67.718); phổ biến dạng `NNN/QD.UBT` (83/QD.UBT = 1.575).
- File (4): `missingSheetNumberCount` = 7.801, `normalizableCount` = 20.691 — cột số tờ /
  ghi chú cần chuẩn hoá MM-yy là bài toán số 2 sau danh mục.

---

## 2. Hành vi người dùng (mô hình hoá)

Đối tượng: cán bộ văn thư/lưu trữ làm trích yếu theo **đợt/lô file theo thời kỳ**,
đơn vị công việc là *khoảng năm + nhóm hồ sơ*, không phải dòng lẻ.

Chuỗi việc tự nhiên:

1. **Nhận file** (xlsm công văn xuất từ hệ thống cũ) → upload một lô.
2. **Lọc theo đợt**: năm (khoảng 1991–2026), số hồ sơ 775–785 dạng "hộp số + hồ sơ số",
   loại hồ sơ (thường cả file là một loại).
3. **Quét lỗi**: thiếu số tờ, thiếu số hồ sơ, trích yếu chưa chuẩn hoá.
4. **Chuẩn hoá danh mục**: gán bộ giấy tờ — thực chất là **đưa về bộ 7** cho ĐK kết hôn.
5. **Xuất** (toàn bộ hoặc lọc) → **lưu Kho** (SQL Server) → in bằng sheets năm.

Điểm cần tối ưu theo hành vi: **bước 4 phải là 1 click cho cả lọc, không phải chọn
từng dòng**; và **bước 3 phải cho thấy "ông còn thiếu bao nhiêu, thiếu gì"** ngay đầu màn.
Draft/undo/restore đã có sẵn — giữ nguyên, chúng đúng nhu cầu (file 22k đợt làm dở).

---

## 3. Đề xuất tính năng (recommendations, ưu tiên theo tác động/công sức)

### P0 — làm ngay tuần tới (tác động cao, công sức thấp)

1. **Nút "Áp dụng bộ 7 chuẩn" theo kết quả lọc** (không chỉ theo selection).
   Đưa 41k hồ sơ ĐK kết hôn thiếu danh sách về chuẩn trong vài click, có preview
   số dòng sẽ thay đổi trước khi áp. Backend: mở rộng bulk-API (đang có BatchJobProcessor
   sẵn, chạy async). *Đây là tính năng số 1 theo dữ liệu.*
2. **Gợi ý tích sẵn khi mở editor / bulk**: nếu dòng ĐK kết hôn chưa có danh sách
   → mặc định tick sẵn bộ 7 chuẩn, bỏ tick dễ dàng; danh mục dropdown sắp xếp theo
   tần suất thật (MergeCatalog đã sort theo observed count — chỉ cần **trả count** ra UI).
3. **Badge "×N" trên mỗi item** trong danh mục: `giấy khai sinh ×21.102` —
   người dùng thấy ngay "cái này ai cũng dùng", giảm phân vân chọn sai.
4. **Mặc định "Loại hồ sơ" = Đăng ký kết hôn** khi phiên thuộc file DK kết hôn
   (92% thời gian đúng ngay từ đầu); đổi loại vẫn 1 click.

### P1 — kế tiếp

5. **Chuẩn hoá số tờ MM-yy toàn phiên** (20.691 dòng có thể chuẩn hoá) — 1 click,
   có preview và chỉ áp những dòng confidence cao; trình bày "còn X dòng phải làm tay".
6. **Báo cáo hoàn thiện theo năm** ngay trong panel xuất bản in: mỗi năm hiện
   `Tổng / Sẵn sàng / Thiếu danh mục / Thiếu số tờ`. Backend `PreviewPrint` đã có
   missing-number per year — mở rộng thêm thiếu danh mục.
7. **Filter presets có tên** ("Làm 2004", "Đợt hộp 88–90") — người dùng làm theo đợt,
   lặp lại cùng điều kiện nhiều giờ liền.
8. **Bulk áp bằng hàng đợi việc (job)** cho lọc lớn: chạy nền, tiến trình, không
   khoá tab; dùng lại BatchJobProcessor — tránh timeout `RequestSizeLimit`/request body
   22k changes.

### P2 — sau khi P0/P1 chạy ổn

9. **Gợi ý tên cặp từ câu**: auto-extract `BÀ … và Ông …` thành 2 chip để compose
   câu chuẩn hoá nhanh (100% dòng có TacGia nên dữ liệu tăng trợ).
10. **Chế độ kiểm tra chất lượng (QA)**: phát hiện trích yếu lệch chuẩn của cùng nhóm
    (`Hồ sơ về việc …` vs `Hồ sơ liên quan đến …`), câu thiếu dấu chấm trước năm,
    lặp tên chính xác — báo cáo CSV đã có `exportIssues` để mở rộng.
11. **Auto-handle dòng ngoại lệ** (334 dòng với 8–16 items, 28 dòng <7): giới thuật
    phát hiện "thiếu/thừa so với bộ 7" và bôi màu từng item chênh lệch.

### Không làm (hoặc chặn lại nếu có ai đề xuất)

- **Ép cứng bộ 7**: 334 dòng ngoại lệ chứng minh bộ này không phải định luật —
  chỉ dùng như *gợi ý mặc định*, luôn cho phép sai khác.
- Tự động áp dụng hành loạt **không có preview/xác nhận** — rủi ro ghi đè hồ sơ người
  khác sửa tay rồi.
- Thêmzwój UI cho các loại hồ sơ hiếm (<2%) trước khi dòng chính chuẩn hoá xong.

---

## 4. Đánh giá UX hiện tại (điểm mạnh cần giữ)

- Draft tự lưu + khôi phục phiên từ server: đúng cho đợt làm dở file 22k.
- Filter AND: năm × loại × số hồ sơ (B hoặc C) đã chuẩn hoá hành vi Excel cũ.
- `scopeReady` chặn gửi yêu cầu thiếu số: tránh lỗi 400, còn hiển thị hướng dẫn.
- Undo/redo + history cục bộ: an toàn khi sửa tay.
- Catalog đã sắp theo tần suất observed — khớp hướng "hay gặp lên trên",
  chỉ thiếu hiển thị **con số** để người dùng tin.

## 5. Câu hỏi cần bạn chốt trước khi code

1. "Bộ 7 chuẩn theo lọc" áp cả **hồ sơ Ghi chú kết hôn** (loại này không có danh sách
   giấy tờ về mặt nghiệp vụ) — mình loại ra ngoài áp hàng loạt đúng không? (Đề xuất: loại.)
2. Áp bulk có cần **hai nút** ("áp cho trang đang xem" vs "áp cho cả kết quả lọc")
   hay chỉ một nút + phạm vi chọn từ dropdown?
3. Có giữ cơ chế **job nền** cho bulk 22k dòng, hay chặn phía UI chọn phạm vi nhỏ hơn
   (≤5.000 dòng) rồi chia lượt? (Đề xuất: job nền.)

---

## 6. Bộ hồ sơ lạ & danh mục tự học (addendum 10/11 — theo yêu cầu)

Nhu cầu: khi xuất hiện **giấy tờ/bo hồ sơ lạ** mà hệ thống chưa có, người dùng
tự thêm và **lưu lên database**, phiên sau/file sau **không phải gõ lại**.

### 6.1 Hiện trạng — vì sao item lạ bây giờ "mất" luôn

Đọc code đã xác nhận:

- `SummaryItemPicker.vue` **có sẵn ô "+ thêm"** (`addCustom`, dòng 164): giá trị chỉ
  được đẩy vào `modelValue` của **dòng hiện tại** — không vào session catalog,
  không xuống DB. Dòng khác muốn dùng → gõ lại; file khác → gõ lại từ đầu.
- Catalog sống theo **phiên đọc file**: `MergeCatalog(headerCatalog, observed, DefaultCatalog)`
  + `Take(200)` — khi file nàoREST nhiều giấy tờ nhỏ, item người dùng tự thêm có thể
  bị văng khỏi kèo 200. Item chuẩn từ `DefaultCatalog` (92 mục) vẫn ưu tiên cũ..
- Backend `NormalizeItems` (dòng 2277) **chấp nhận item lạ** (không chặn, chỉ set
  order cuối) → không có blocker kỹ thuật nào cho việc lưu trữ persistent.
- Bulk-apply dialog cũng là picker đó → gõ item lạ cho 100 dòng vẫn phải gõ 1 lần/dòng
  nếu muốn cẩn thận.

Kho SQL đã có 67.718 trích yếu thật → nguyên liệu **seed** có sẵn.

### 6.2 Thiết kế đề xuất: "Danh mục học được" (persistent dictionary + preset)

**Bảng mới (EF migration):**

```csharp
// Stp.Core.Domain / Stp.Infrastructure.Persistence
public sealed class CatalogItem
{
    public Guid Id { get; set; }
    public string NormalizedKey { get; set; }   // chuẩn hoá theo Logic NormalizeKey có sẵn — unique index
    public string DisplayName { get; set; }     // cách viết hiển thị (chữ thường đúng chuẩn)
    public string Source { get; set; }          // User | Learned | Default
    public int UsageCount { get; set; }         // tăng mỗi lần áp/expor
    public DateTime LastUsedAt { get; set; }
    public string? DossierTypeHint { get; set; } // null = gợi ý tất cả loại
    public bool IsDisabled { get; set; }        // soft delete — không bao giờ hard-delete
    public DateTime CreatedAt { get; set; }
}

public sealed class CatalogSet  // "bộ chỉ định" có tên — dùng nhanh như template
{
    public Guid Id { get; set; }
    public string Name { get; set; }            // "Bộ 7 chuẩn", "Bộ nuôi con nuôi 8", "Bộ ngoại lệ hộ chiếu"
    public Guid OwnerId { get; set; }
    public string? DossierTypeHint { get; set; }
    public List<CatalogSetItem> Items { get; set; }  // Ordered list
}
```

**Thứ tự merge catalog ở `ReadSession` (5 lớp, ưu tiên cao → thấp):**

1. Header catalog của sheet hiện tại (in sẵn trong file)
2. `CatalogItem.Source=User` từ DB — **never evicted** (không cho chiếm slot một khi
   đã Enter Enter bởi người dùng), sắp theo `UsageCount desc, LastUsedAt desc`
3. Observed trong file hiện tại (count desc) — như hiện tại
4. `Learned` từ DB đạt ngưỡng (`UsageCount >= 3`, `!IsDisabled`)
5. `DefaultCatalog` (92 mục cố định) — như hiện tại, giữ như floor

API sketch:

```
GET    /api/summary/catalog                    → list item + preset (cached 5-10 phút)
POST   /api/summary/catalog/items              → upsert; nếu key đã có: bump UsageCount/LastUsedAt
PATCH  /api/summary/catalog/items/{id}         → đổi tên hiển thị / disable (AuditLog)
GET    /api/summary/catalog/sets, POST, DELETE → quản lý preset
POST   /api/summary/{sessionId}/export*        → gru harvest các items xuất — tăng UsageCount (piggyback)
```

**UX với nguyên tắc "tự lưu mặc định" (chốt 11/11):**

- Gõ mục mới + Enter → **tự lưu ngay vào DB** (`POST catalog/items`, Source=User),
  không hiện panel hỏi. Chip hiện badge nhỏ "đã lưu danh mục" mờ đi sau 2s.
- Keyword "**tránh thêm vào rồi không lưu**": đây là lỗi hiện tại — `addCustom`
  (SummaryItemPicker.vue:164) chỉ đẩy vào `modelValue` của dòng, bản thân nó
  **không bao giờ chạm DB**. Bản mới gõ 1 lần = có vĩnh viễn cho mọi file sau.
- Nếu gõ biến thể đã có (vd "đơn xi hộ khẩu" ~ "đơn xin hộ khẩu"): server trả
  canonical theo `NormalizeKey` → chip tự nhảy về cách viết chuẩn, không sinh dòng dư.
- `DossierTypeHint` **bắt tự theo filter loại hồ sơ đang mở** — mục thêm lúc đang lọc
  "Đăng ký kết hôn" sẽ ưu tiên gợi ý trong ngữ cảnh loại đó (vẫn xuất hiện ở mọi loại,
  chỉ là thứ tự cao hơn trong đúng ngữ cảnh).</think><tool_call>edit<arg_key>newString</arg_key><arg_value>**UX phiền tối thiểu (cập nhật theo chốt "tự lưu mặc định" — xem 6.2bis):**

- Gõ item Enter → chip xuất hiện và **tự lưu DB ngay**, không panel lựa chọn
  (đã bỏ ý "Ghi nhớ vĩnh viễn / Chỉ dùng lần này" của nháp đầu).
- Nhóm mới trong picker: "Đã lưu (người dùng)" nằm ngay trên "Danh mục file",
  mỗi item vẫn có badge ×N (dùng `UsageCount` thật).
- Bulk dialog: dropdown "Bộ chỉ định" từ `CatalogSet` + "Lưu bộ đang chọn thành preset…"
  → khi gặp bộ lạ (vd 8-item nuôi con nuôi) chỉ cần tạo preset 1 lần, các lần sau 1 click.
- Workspace dev: trang quản lý catalog — merge biến thể (`đơn xi hộ khẩu` ~ `đơn xin hộ khẩu`),
  rename, disable. `NormalizeKey` có sẵn (bỏ dấu + chuẩn whitespace) đã xử lý đa phần.

**Chống "rác" thấm vào danh mục (chứng minh bằng dữ liệu thật):**

Query DB hôm nay cho thấy split thô lậu bắt cả **tên người** làm item
("NGUYEN TRI" ×2, "CHU MINH QUANG" ×2, "LE PASCAL" ×2…) — chứng minh auto-harvest
không kiểm soát sẽ ô nhiễm. Cơ chế:

- Chỉ item **người dùng chủ động chọn "Ghi nhớ"** mới lên danh mục ngay (Source=User).
- Auto-harvest (từ export/save-Kho) chỉ là **bộ đếm UsageCount**; item learned chỉ
  được *gợi ý* khi đạt ngưỡng (đề xuất ≥3), luôn xếp dưới User.
- **Seed 1 lần từ Kho**: harvest toàn bộ trích yếu, lấy item có count ≥50 → ra đúng
  ~17 item chuẩn sạch (như bảng mục 1.3); rác tên người (count ≤2) tự lọc hết.
  Đây là điểm cực mạnh: baseline vừa dựng xong đã đúng 100%.
- Soft delete + admin review; không tự xoá.

### 6.2bis Đa dạng hóa danh mục hộ tịch (chốt 11/11 — "thêm vài mục hộ tịch, trích nọ kia")

Ba tầng đưa mục mới vào hệ thống — **người dùng chỉ làm tầng 1, còn lại tự động:**

**Tầng 1 — người dùng thêm trực tiếp (tự lưu DB ngay):**
- Mục mới gõ trong picker → `POST /api/summary/catalog/items` **thể hiện trong commit
  của dòng** (không phải 1 bước riêng, không đợi nút "lưu"), không có đường nào cho phép
  "thêm vào session mà không lưu". Đây là nguyên tắc thiết kế số 2 của mục này.
- Dev dọn rác về sau; người dùng không bao giờ phải băn khoăn "có được ghi không".

**Tầng 2 — seed 2 mức từ Kho (làm 1 lần + định kỳ):**

| Mức | Điều kiện | Xử lý |
|---|---|---|
| Auto-accept | UsageCount ≥ 50 (17 mục như bảng 1.3) | Nạp thẳng vào DB, Source=Learned |
| Dev-review | 2 ≤ count ≤ 49 (≈ 20 mục thật như bên dưới) | Hiện trong Workspace dạng checkbox "Nạp danh mục đề xuất" — dev chọn lick |
|- Noise | count ≤ 1 (tên người, sai chính tả đơn lẻ) | Không nạp; hiện trong report QA (P2) |

Danh sách đề xuất lấy được ngay từ DB thực (đếm trên 67.718 trích yếu), nhóm theo
ngành hộ tịch — phần lớn **chưa thấy trong 92 mục DefaultCatalog**:

- *Kết hôn:* đơn xin đăng ký kết hôn (308), đơn xin cấp giấy chứng nhận độc thân (6),
  giấy chứng nhận không có trú ngụ trong việc kết hôn (72), giấy chứng nhận đăng ký
  nhân khẩu (106)
- *Đính hôn / ly hôn:* đơn xin xác nhận đính hôn (6), quyết định công nhận
  thuận tình ly hôn (6)
- *Khai sinh / khai tử:* giấy khai sinh (21.102), tờ khai danh dạ khai sinh (22),
  bản khai danh dạ khai sinh (2), giấy báo tử (10), giấy chứng nhận sức khỏe (4)
- *Nuôi con nuôi / tờ trình:* bản tường trình (2), tờ khai tình trạng gia đình (16),
  tờ khai sanh danh dạ (4)
- *Xuất nhập cảnh / quốc tịch:* phiếu nhập xuất cảnh (226), hộ chiếu (136),
  giấy thông hành (12), chứng chứng nhập tích (2 = "chứng chỉ nhập tịch")
- *Chung:* CMND (250), giấy tạm trú  (giấy lưu trú — để khai tự nhiên), quyết định
  nuôi con nuôi, giấy xin (đơn xin) — các dạng "giấy lưu trú", "giấy tờ tạm trú",
  "bản công hàm công chứng" (biến thể công hàm)

Chú ý: seed đi qua `NormalizeKey` nên biến thể ("đơn xi hộ khẩu" vs "đơn xin hộ khẩu",
"chứng chứng nhập tích" vs "chứng chỉ nhập tịch") gộp tự động — khi dev nạp bằng
checkbox, DB trả canonical luôn.

**Tầng 3 — tự học từ export (UsageCount piggyback):** mỗi lần áp/xuất/lưu Kho,
server cộng UsageCount — không ai phải làm gì, "loại nào hay ra" tự nhích lên đầu.

### 6.3 Phân khúc công việc & effort (cập nhật sau chốt 11/11)

| Bước | Việc | Effort |
|---|---|---:|
| 1 | Migration `CatalogItem`/`CatalogSet` + API list/add/rename/disable | 0,5–1 ngày |
| 2 | `ReadSession` merge 5 lớp + picker nhóm "Đã lưu" + badge ×N | 0,5–1 ngày |
| 3 | Script seed từ Kho (count ≥50) chạy 1 lần | 0,5 ngày |
| 4 | Preset CRUD + áp nhanh trong bulk dialog | 1–2 ngày |
| 5 | Harvest usage từ export/import govn (piggyback có sẵn) | 0,5 ngày |
| 6 | Panel "Nạp danh mục đề xuất" (tầng 2) trong Workspace dev | 0,5 ngày |
| — | Tests: merge, seed threshold, upsert dedup — như pattern MlSheetDetectionTests | 0,5 ngày |

Ước tổng: **3,5–6 ngày dev**. Xin lưu ý: là companion trực tiếp với **P0 (bộ 7 chuẩn +
badge tần suất)** của mục 3 — nên làm liền nhau một đợt.

### 6.4 Câu hỏi chốt thêm (mục 6 — cập nhật sau chốt 11/11)

4. Chốt: **mọi người dùng đăng nhập** được thêm mục, tự lưu DB ngay (có AuditLog)?
   (Đề xuất: đúng vậy — khớp yêu cầu "tự lưu lên database luôn".)
5. Tầng 2: auto-accept ngưỡng ≥50, band 2–49 để dev review qua checkbox? Hay
   auto-accept luôn cả band đó? (Đề xuất: giữ review — band nhỏ dễ dính sai chính tả.)
6. `DossierTypeHint` bắt theo filter loại hồ sơ đang mở khi thêm mục này — OK hay
   để trống hết? (Đề xuất: bắt tự.)

---

## 7. Trạng thái triển khai 10/11/2026 — "bộ 7 chuẩn + danh mục học được" (P0 xong)

### 7.1 Đã code xong, đã chạy thật

**Backend:**

- Bảng `CatalogItems` (migration `AddCatalogItems`, đã apply vào DB `StpArchive`):
  `NormalizedKey` unique, `Source` (User/Learned), `UsageCount`, `LastUsedAt`,
  `DossierTypeHint`, `IsDisabled` (soft delete), `CreatedAt`.
- `CatalogKey.Normalize` (`Stp.Core/Summary/CatalogKey.cs`): gộp đ→d, bỏ dấu, chữ
  thường, gộp space — **không sửa lỗi chính tả** ("đơn xi" ≠ "đơn xin");
  `SummaryToolService.NormalizeKey` giờ delegate sang đây (một nguồn duy nhất).
- `CatalogStore` (`Stp.Infrastructure/Persistence`): `ICatalogDictionary` qua
  `IDbContextFactory<StpDbContext>` (vì `ISummaryToolService` là Singleton);
  upsert giữ canonical cũ + tăng UsageCount; rename chặn trùng key.
  DI chuyển `AddDbContext` → `AddDbContextFactory<StpDbContext>`.
- `CatalogSeeder`: đếm tần suất item trong toàn bộ `DocumentLines.TrichYeu`,
  nạp mục có count ≥ `minCount` (mặc định 50) Source=Learned; idempotent.
  Split ngoặc replicate đúng `SplitTrailingGroup` của service.
- `SummaryCatalogController` (`api/summary/catalog`): GET list (sắp UsageCount
  desc), POST items (upsert, trả canonical), PATCH items/{id} (rename/disable),
  POST seed (minCount ≥2). Endpoint mở như mọi endpoint SummaryTool.
- Merge catalog 5 lớp trong `ReadSession`: header sheet → **User (usage desc,
  never evicted)** → observed (count desc) → Learned (đã trg GetActive) →
  DefaultCatalog; trần tổng 400 (tăng từ 200, mục user không bao giờ bị cắt).
- `SummaryWorkbook.CatalogMeta` + `SummaryInspectResponse.CatalogMeta`:
  usage = max(observed theo key chuẩn, entry.UsageCount), source
  user/learned/file/default.

**Frontend (build OK, 74,94 kB):**

- `SummaryItemPicker`: mới nhận `counts` (map key chuẩn hóa → số lần gặp) →
  badge `×N` bên phải mỗi mục; khi gõ mục mới **vừa thêm chip vừa emit**
  `custom-added` → không còn kịch bản "thêm vào session rồi mất".
- `SummaryToolView`: `STANDARD_SEVEN` (7 giấy tờ chiếm 98,3% hồ sơ kết hôn có
  danh sách), nút **"Bộ 7 chuẩn"** trong bulk bar chọn sẵn cả 7 vào picker;
  `persistCatalogCustom` POST `/api/summary/catalog/items` tự lưu + cập nhật
  catalog/catalogMeta local; `catalogCounts` Feer counts cho picker (bulk +
  từng dòng); thất bại lưu chỉ toast cảnh báo, chip vẫn dùng được.

**Test + chạy thật:**

- `CatalogDictionaryTests` 5 test: merge user trên observed, meta từ dictionary,
  meta không dictionary (nguồn file/default), gộp dấu/space. Toàn suite
  **68/68 pass**.
- End-to-end thật qua API port 5215:
  - `POST seed {minCount:50}` → đúng **17 mục Learned** (khớp dự đoán bảng 1.3):
    giấy khai sinh ×21.102, sơ yếu lý lịch ×21.090, bản công hàm ×21.060,
    phiếu đề xuất ×21.042, hộ khẩu ×20.954, giấy khai đăng ký kết hôn ×20.842,
    chứng chỉ chuyên khoa ×20.818 — 7 mục đầu chính là bộ 7 chuẩn; sau đó
    đơn xin đăng ký kết hôn ×308, phiếu khám sức khỏe ×288, CMND ×250…
  - `POST items "giấy tạm trú của vợ"` → id `d609e515…` source=user usage=1;
    gõ tiếp biến thể `GIAY TAM TRU CUA VO` → **cùng id**, canonical giữ nguyên,
    usage 1→2. Đây chính là hành vi "tự lưu + gộp biến thể" mong đợi.
  - `inspect DK KET HON (4).xlsx` → catalog 106 mục, meta learned đứng đầu;
    mục user ×2 có trong meta.

**Đợt mở rộng 10/11 (chiều — "đủ kiểu theo họ tiền tố" + fix dropdown):**

- `DefaultCatalog` mở rộng +37 mục phủ đủ họ tiền tố: **giấy, cam kết** (kể cả
  đích danh "cam kết hôn nhân thực tế", "cam kết cư trú"), **xác nhận**
  ("đơn xin xác nhận độc thân", "xác nhận quốc tịch", "giấy xác nhận tình trạng
  hôn nhân"), **chứng nhận** ("đơn xin cấp giấy chứng nhận độc thân"),
  **chứng minh** ("chứng minh nhân dân", "căn cước công dân", "CCCD"),
  **tờ khai / bản khai** ("tờ khai tình trạng gia đình"), **trích lục**
  (hộ tịch/khai sinh/khai tử), **quyết định** ("quyết định nuôi con nuôi",
  "quyết định công nhận thuận tình ly hôn"), giấy chuyển hộ, giấy thông hành…
  Test `CatalogDictionaryTests` assert đích danh 5 mục user chỉ định.
- `CatalogSeeder`: tách `ComputeCounts`, seed nhận `maxCount` (band) + bộ
  `LabelKeywords` lọc nhiễu tên người (chỉ áp khi seed có maxCount — seed
  mặc định ≥50 giữ nguyên hành vi); thêm `Suggest()` không ghi DB.
- Endpoint `GET api/summary/catalog/suggestions?minCount=&maxCount=`
  (mặc định 2/49) xem trước danh mục đề xuất; `POST seed` nhận `MaxCount`.
- `ICatalogDictionary.GetById(id)` (kể cả mục đã ẩn): PATCH disable giờ trả
  **200** thay vì 404-đó sau khi ẩn thành công.
- Chạy thật band 2–49 trên DB `StpArchive`: suggestions ra 23 mục thật
  ("đơn xin xác nhận tình trạng hôn nhân" ×32, "tờ khai danh dạ về khai
  sinh" ×22, "tờ khai tình trạng gia đình" ×16, "giấy thông hành" ×12,
  "đơn xin xác nhận đính hôn" ×6, "đơn xin cấp giấy chứng nhận độc thân" ×2…);
  `POST seed {minCount:2,maxCount:49}` → added 23, tổng 43 mục. 5 mục rác
  thuật toán ("CMND đơn xin đăng ký kết hôn", "CMND sơ yếu lí lịch",
  "đơn xi hộ khẩu", "giấy chứng tứ", "giấy xác nhận không có trở ngại")
  soft-disable qua `IsDisabled` (không xoá, hoàn tác được) — **38 active**.
- `AppSelect.vue` fix dropdown "Loại hồ sơ" bị cắt khi cuộn gần đỉnh trang
  (bug "kéo cột"): đo chiều cao panel THẬT sau render (thay ước lượng
  cứng 360 gây flip dropUp nhầm), flip dropUp theo khoảng trống thật,
  chặn panel trong viewport (clamp top), `scrollActiveIntoView()` lúc
  mở. Kiểm chứng browser thật tại scrollY=900: panel top≈12, 7 option trọn
  viewport; chọn xong layout đứng yên (table top 1214.8 không đổi).
- `SummaryItemPicker.vue` (picker chọn giấy tờ, chip ×N) fix cùng loạt lỗi:
  bỏ ước lượng 430 → đo cao thật + clamp trọn viewport (bug cũ: trigger thấp
  gần đáy thì panel mở xuôi tràn đáy, hàng "Thêm giấy tờ khác" + hint chìm
  ngoài màn hình); **tự mồi lại vị trí sau khi chọn** (mục chọn làm chips lớn
  ra, layout dịch, panel fixed đứng yên trông như tách rời/trống — đúng ảnh
  user gửi 11/11); `scrollActiveIntoView` cuộn CHỈ trong `.ms-list` thay
  `scrollIntoView` toàn cục (nguyên nhân kéo cả trang phía sau). Kiểm chứng
  browser thật: trigger cách đáy 80px → panel mở LÊN trọn viewport; chọn xong
  scrollY 831.2 không đổi, panel theo trigger; ArrowDown ×50 list cuộn nội bộ
  scrollTop 1538, trang đứng yên. AppSelect dùng chung cách cuộn giới hạn.

### 7.2 Còn lại (P1 — làm tiếp)

- Piggyback UsageCount: increment khi áp hàng loạt / xuất / lưu Kho (tầng 3) —
  hiện chỉ tự tăng khi user gõ lại mục đã có.
- Preset `CatalogSet` (bộ chỉ định có tên) + "Lưu bộ đang chọn thành preset" trong bulk.
- Panel "Nạp danh mục đề xuất" band 2–49 cho dev review trong Workspace
  (mục 6.2bis tầng 2) — **API đã sẵn**: GET suggestions view trước, POST
  seed giờ nhận MaxCount; chỉ còn thiếu UI panel (dữ liệu đã nạp tay qua
  API: 23 mục, 5 mục rác đã ẩn).
- Badge chip xác nhận "đã lưu danh mục" mờ dần sau 2s (chi tiết thẩm mỹ).
- AuditLog cho thao tác thêm/rename/disable (chốt 4: mọi người dùng được thêm).



---

## 8. Nền tảng nhóm (14/11/2026)

> Mục tiêu người dùng: "giống Google Sheets — ai làm ở đâu mình biết, có kênh chat
> tổng để anh em làm việc với nhau trong app". Chốt "Cả 3 luôn": tên người dùng +
> nhật ký hoạt động + chat tổng + badge ai đang sửa dòng nào.

### 8.1 Ai làm gì — khai tên 1 lần

- Không có hệ thống tài khoản ở giai đoạn này. Mở app lần đầu → modal
  **"Mình gọi bạn là gì?"** (UserNameModal) — gõ tên, máy nhớ trong
  `localStorage stp.user.name`, mở lại không hỏi nữa.
- Tên được gửi kèm **mọi request** qua header `X-Stp-User` (thêm trong
  `request()` lẫn `download()` của `api/client.ts`) — backend đọc tên ở
  `TeamAudit.ActorFromHeader`.
- Có nút "avatar + Đổi tên" trên dock để sửa tên (gửi kèm `X-Stp-User` mới).

### 8.2 Nhật ký hoạt động (tab "Hoạt động")

- Ghi vào bảng `AuditLogs` (bảng có sẵn, trước đó trống) — **không thêm bảng
  mới**, đội backend writer `TeamAudit` fire-and-forget: nhật ký gãy không bao
  giờ làm rớt thao tác thật.
- Sự kiện được ghi đúng 4 loại (theo chốt): `export`, `export-filtered`,
  `export-print` (SummaryToolController) và `sync` (SyncController sau khi
  Upload thành công) — ai → làm gì → số dòng/chi tiết → lúc nào.
- Endpoint: `GET /api/team/activity?take=60` (mới nhất lên đầu).
- UI: tab trong dock, hiển thị "Nam · Xuất bản lọc · DK KET HON (5).xlsx:
  20642 dòng sửa · 5 phút trước".

### 8.3 Chat tổng (tab "Chat")

- Bảng mới duy nhất `ChatMessages` (migration `AddChatMessages`, EF
  standard — migration tournament chưa chạy không ảnh hưởng).
- Endpoint: `GET /api/team/chat?take=100` (tối đa 100 tin, cũ → mới),
  `POST /api/team/chat` (`actor` ≤ 100, `body` ≤ 500 ký tự).
- UI: dock góc phải (TeamDock) — pill thu gọn + panel 2 tab; tin của mình
  bubble teal bên phải, người khác avatar màu + tên bên trái. Enter để gửi.
- Dò tin 3 giây khi panel mở, 12 giây khi thu gọn (vẫn đếm số chưa đọc,
  badge đỏ trên pill lẫn tab). Lưu DB nên F5/đổi máy vẫn đọc được lịch sử.

### 8.4 Ai đang sửa dòng nào (mảnh "Google Sheets")

- Backend `TeamPresence` (singleton, **in-memory**, không DB — dấu tự tàn):
  `POST /api/team/presence` heartbeat. TTL online 75 giây, TTL dấu dòng
  65 giây; dọn mục quá hạn ngay lúc ping.
- Client ping từ `useTeamPresence`: tick 3 giây, đáo hạn mới gọi — gửi
  8 giây/lần khi đang sửa dòng, 20 giây/lần khi chỉ đứng xem. Mở/đóng bảng
  sửa → gội ping ngay lập tức (không đợi nhịp).
- Dòng nào đang có người khác sửa: nền xanh + thẻ tên "Lan đang nắn" ngay
  dưới số dòng; mở đúng dòng đó → banner cảnh báo "Lan cũng đang mở dòng
  này. Ai lưu sau sẽ ghi đè" — v1 chỉ cảnh báo, chưa khoá cứng (chốt).
- Người đóng bảng (rowNumber trống + sessionId có): server chủ động gỡ dấu
  các dòng của chính người đó; dấu người khác vẫn giữ.
- `sessionId` = `SummaryInspectResponse.sessionId`, dòng =
  `SummaryRowResponse.rowNumber` — hai người mở cùng file mới thấy nhau.

### 8.5 Share link + chạy qua tunnel

- App serve SPA từ `wwwroot` 1 cổng 5215 (deep link `/trich-yeu` OK qua
  MapFallback, `/api/*` 404 thật).
- Tunnel `cloudflared tunnel --url http://localhost:5215 --no-autoupdate`
  — free URL **đổi mỗi lần restart cloudflared** (đã xác minh lần 2).
- Polling thuần HTTP nên chạy được qua tunnel như mọi endpoint khác.

### 8.6 Cần hậu mãi (không chặn)

- Khoá cứng dòng khi 2 người cùng sửa (hiện chỉ banner cảnh báo).
- Presence in-memory: restart API mất dấu online vài chục giây (các truy vấn
  session/dữ liệu không mất — FileStorage giữ toàn bộ).
- ~~Tên tự khai chưa phân quyền~~ → đã làm tài khoản nhóm thật ngày 11/11,
  xem mục 8.7.

### 8.7 Tài khoản nhóm (2026-11-11)

- Bảng mới `StpUsers` (migration `AddStpUsers`): `Username` duy nhất (chữ
  không dấu/số/`._-`, 3–50), `DisplayName` (tiếng Việt tự do), `PasswordHash`
  (PBKDF2 chuẩn `PasswordHasher<StpUser>` — không bao giờ lưu/trả mật khẩu
  gốc), `Role` (`admin`/`member`), `IsActive`, `LastLoginAt`.
- Đăng nhập `/api/auth/login` → cookie `.stp.team` (scheme `StpTeam`,
  HttpOnly, SameSite Lax, **30 ngày sliding**); `/api/auth/me` trả danh tính,
  `/api/auth/logout` xoá. JWT developer giữ nguyên — hai hệ song song.
- **Lưu ý kỹ thuật sống còn**: default authenticate scheme là JWT nên
  `HttpContext.User` không chứa phiên nhóm. Mọi endpoint đọc identity phải
  `await StpTeamIdentity.FromAsync(HttpContext)` (tự `AuthenticateAsync` đúng
  scheme). Endpoint `[Authorize(AuthenticationSchemes = StpTeamIdentity.SchemeName)]`
  thì middleware tự resolve.
- **Chống giả danh**: người đã đăng nhập thì chat/presence/audit lấy
  `DisplayName` từ tài khoản, tên client gửi bị bỏ qua. Verify thật qua
  tunnel: đăng nhập `lan`, gửi chat kèm actor giả → tin lưu "Lan";
  member gọi `/api/admin/users` → 403.
- Admin panel `/quan-ly` (UsersView): tạo tài khoản, sửa tên hiển thị, đổi
  quyền, ngừng/kích hoạt, đặt lại mật khẩu; mọi thao tác ghi `AuditLogs`
  (`admin.user-create`, `admin.user-update`, `admin.password-reset`). Guard:
  router `meta.admin` + `[Authorize(Roles = "admin")]`; chặn tự ngừng kích
  hoạt và tự hạ quyền của chính mình.
- Khoá bắt buộc đăng nhập: appsettings `StpTeam:RequireLogin` — mặc định
  **false** để không gián đoạn ca nhập đang chạy; bật sau khi tạo đủ tài
  khoản cho anh em. Còn tắt: chưa đăng nhập vẫn dùng tên tự khai như cũ.
- Seed lần đầu bảng trống: `admin / stp2026` (log khởi động nhắc đổi ngay).
- Frontend: `LoginModal`, `useTeamIdentity` (`initTeamIdentity` hỏi cookie,
  đồng bộ DisplayName vào `stp.user.name` để code cũ dùng tiếp), TeamDock có
  nút Đăng nhập + menu tài khoản (quản lý/đăng xuất); presence ưu tiên
  displayName tài khoản. Tests: 79/79 (`AccountRulesTests` thêm 11 case).

### 8.8 Link cố định + Windows Service (2026-11-11)

- Pain gốc: link trycloudflare **đổi mỗi lần restart cloudflared** (server-side
  Cloudflare huỷ quick tunnel), và hệ thống tắt nhầm phải ai đó bật tay.
- Giải pháp chốt sau khi tra docs chính thức ngrok: free plan tặng 1 **dev
  domain cố định** — `https://mangle-tastiness-french.ngrok-free.dev` — không
  đổi khi restart agent/máy. Tra docs cũng xác nhận Cloudflare Zero Trust free
  cho 1.000 tunnels (phương án domain riêng ~200k/năm vẫn mở nếu sau này cần
  link đẹp hơn).
- Hệ thống chạy **Windows Service** (WinSW trong `service/`, chỉ để trên máy):
  `StpApiService` (API cổng 5215) + `StpNgrokService` (tunnel) — Automatic khi
  máy khởi động, gãy tự restart sau 10–15 giây. Kết nối SQL: `NT AUTHORITY\SYSTEM`
  được grant `db_owner` StpArchive (service chạy LocalSystem, Windows Auth).
- Bẫy đã gặp: Defender gắn nhãn ngrok "potentially unwanted software" → phải
  exclusion path + process trước khi tải (Expand-Archive văng lỗi). `dl.ngrok.com`
  không kết nối được từ mạng này — tải từ equinox CDN. `dotnet run` dưới SYSTEM
  lần đầu chậm (first-run banner) — đợi ~40 giây mới nghe cổng.
- Client fetch thêm header `ngrok-skip-browser-warning` (API không dính trang
  cảnh báo); lần vào đầu bằng trình duyệt hiện 1 trang warning ngrok — nhấn
  "Visit Site" là qua (hạn chế free plan, chấp nhận theo chốt).
- trycloudflare đã ngừng dùng — **chỉ còn 1 link duy nhất**. Quy trình update
  code: `Stop-Service StpApiService` → build → `Start-Service StpApiService`;
  không chạy `dotnet run` tay song song với service (đụng cổng 5215).

### 8.9 Màn ra vào "cuốn hồ sơ" + tự đăng ký + tổng kết việc (2026-11-11, vòng 2)

- **Gate khi chưa có phiên**: app mở thẳng màn full-screen AuthGate — mặt trước
  là Đăng nhập, bấm "Đăng ký ngay" lật 3D sang mặt sau (perspective 1700px,
  `rotateY(-180deg)`, 1.05s cubic-bezier mượt; màn nhỏ <860px lật 2D cho nhẹ
  máy). Lật lại bằng "Đăng nhập". Bố cục hai mặt đối xứng nhau (form/brand
  hoán đổi) cho cảm giác lật trang hồ sơ thật.
- **Tự đăng ký**: `POST /api/auth/register` — ai mở link cũng tự tạo được tài
  khoản member và vào dùng luôn (backend cấp cookie ngay). Validate bằng
  `AccountRules`; username trùng → 409 với thông báo tiếng Việt. Cờ cấu hình
  `StpTeam:AllowRegistration` (appsettings, mặc định **true**) — đặt false để
  chỉ quản trị cấp tài khoản. Audit: `auth.register`.
- **Ghi nhớ 24 giờ sliding**: cookie `ExpireTimeSpan = 24h` + `SignInAsync
  ExpiresUtc 24h`. Còn vào app mỗi ngày là tự gia hạn — không bao giờ thấy màn
  đăng nhập; vắng quá 24 giờ mới phải đăng nhập lại. (Đổi từ 30 ngày theo yêu
  cầu "tự ghi nhớ 24h, khỏi phải vào".)
- **Màn chờ = cuốn hồ sơ lật từng trang** (BootSplash, thay vòng xoay + progress
  bar): tờ hồ sơ nhấc lên `translateZ(20px)` rồi xoay `-180°` quanh gáy trái,
  keyframes quản lý z-index 45→60→5 để trang đang bay luôn đè trang đã nằm;
  đợi lâu lật 3 tờ, nhanh lật 2 tờ, nhịp tính từ durationMs prop (lần đầu
  2.6s, quay lại có phiên 1.1s); tắt mượt bằng Transition scale+fade ở App.vue.
  Bẫy: chuỗi `v-else-if` gắn sau `<Transition>` làm compiler Vue văng
  "Cannot read properties of undefined (reading 'type')" — mỗi nhánh phải tự
  mang `v-if`.
- **Thẻ tổng kết kiểu Google Sheets** (WelcomeRecap, hiện sau màn chờ, tự tấu
  25s hoặc bấm X): "Chào buổi sáng, {tên}! Bạn đang ở trang {title}" + việc
  gần đây lấy từ `GET /api/auth/recap` — lọc `AuditLogs` theo
  `Actor == DisplayName`, loại sự kiện `auth.*`, ghép area+label tiếng Việt
  (export* → Bổ sung trích yếu, sync → Đồng bộ từ Excel, admin.* → Quản lý
  tài khoản) + `relativeTime`. Tải lỗi thì im lặng, app vẫn vào bình thường.
- Token dev (`stp.dev.token`) bỏ qua gate — không gian nâng cao vào thẳng như
  cũ. API vẫn mở (RequireLogin=false): gate là lớp UI, muốn khoá cứng server
  thì bật `StpTeam:RequireLogin` như mục 8.7.
- Lưu ý vận hành: `dotnet test` phải chạy khi **service đã dừng** — service
  đang chạy khoá `Stp.Api.dll` khiến build test văng MSB3021/MSB3027 (không
  phải lỗi code).
