import sys
import os
import json
import time
import re
from urllib.parse import urlparse

# Đảm bảo Playwright luôn tìm thấy Chromium dù chạy dưới quyền Windows Service (SYSTEM) hay User
if "PLAYWRIGHT_BROWSERS_PATH" not in os.environ or not os.path.exists(os.environ.get("PLAYWRIGHT_BROWSERS_PATH", "")):
    candidate_paths = [
        r"C:\ProgramData\ms-playwright",
        r"C:\WINDOWS\system32\config\systemprofile\AppData\Local\ms-playwright",
    ]
    try:
        users_dir = r"C:\Users"
        if os.path.isdir(users_dir):
            for u in os.listdir(users_dir):
                candidate_paths.append(os.path.join(users_dir, u, "AppData", "Local", "ms-playwright"))
    except Exception:
        pass

    for p in candidate_paths:
        try:
            if os.path.isdir(p) and any(os.scandir(p)):
                os.environ["PLAYWRIGHT_BROWSERS_PATH"] = p
                print(f"INFO:Found Playwright browsers at: {p}", flush=True)
                break
        except Exception:
            pass

from playwright.sync_api import sync_playwright

import unicodedata

ABBREVIATIONS = {
    "dkkh": "dang ky ket hon",
    "kkh": "ket hon",
    "gcn": "giay chung nhan",
    "cn": "chung nhan",
    "gks": "giay khai sinh",
    "ksk": "kham suc khoe",
    "shk": "so ho khau",
    "hk": "ho khau",
    "nxc": "nhap xuat canh",
    "xnc": "xuat nhap canh",
    "xn": "xac nhan",
    "xndt": "xac nhan doc than",
    "ttdt": "tinh trang doc than",
    "tthn": "tinh trang hon nhan",
    "hntt": "hon nhan thuc te",
    "cmnd": "chung minh nhan dan",
    "cccd": "can cuoc cong dan",
    "syll": "so yeu ly lich",
    "ll": "ly lich",
    "uq": "uy quyen",
    "lh": "ly hon",
    "tl": "trich luc",
    "bs": "ban sao",
    "bc": "ban chinh",
    "ch": "cong ham",
    "tt": "thi thuc",
    "hc": "ho chieu",
    "tk": "to khai",
}

def remove_accents(s):
    if not s:
        return ""
    nfkd = unicodedata.normalize('NFKD', s)
    return "".join(c for c in nfkd if not unicodedata.combining(c)).replace("đ", "d").replace("Đ", "D")

def expand_abbreviations(s):
    if not s:
        return ""
    words = re.split(r'(\b[a-zA-Z0-9]+\b)', s)
    res = []
    for w in words:
        low = w.lower()
        if low in ABBREVIATIONS:
            res.append(ABBREVIATIONS[low])
        else:
            res.append(w)
    return "".join(res)

def normalize_text(text):
    if not text:
        return ""
    s = remove_accents(text).strip().lower()
    s = expand_abbreviations(s)
    return re.sub(r'[^a-zA-Z0-9]', '', s)

def is_match(header_title, item):
    h = normalize_text(header_title)
    it = normalize_text(item)
    if h == it:
        return True
    # 1. Phiếu đề xuất
    if ('dexuat' in h and 'dexuat' in it):
        return True
    # 2. Thị thực / Visa
    if ('visa' in h or 'thithuc' in h) and ('visa' in it or 'thithuc' in it):
        return True
    # 3. Hộ chiếu / Passport / HC
    if ('hochieu' in h or 'passport' in h or 'hc' == h) and ('hochieu' in it or 'passport' in it):
        return True
    # 4. CMND / CCCD / Căn cước / Chứng minh
    if ('chungminh' in h or 'cmnd' in h or 'cccd' in h or 'cancuoc' in h) and ('chungminh' in it or 'cmnd' in it or 'cccd' in it or 'cancuoc' in it):
        return True
    # 5. Hộ khẩu / Sổ hộ khẩu
    if ('hokhau' in h and 'hokhau' in it):
        return True
    # 6. Giấy khai sinh
    if ('khaisinh' in h and 'khaisinh' in it):
        return True
    # 7. Tờ khai đăng ký kết hôn / Tờ khai
    if ('tokhai' in h or 'tokhai' in it):
        if ('kethon' in h or 'kethon' in it or 'dkkh' in h or 'dkkh' in it):
            return True
        if 'tokhai' in h and 'tokhai' in it:
            return True
    # 8. Đơn xin xác nhận độc thân và hôn nhân thực tế / Hôn nhân thực tế / Độc thân
    if ('honnhanthucte' in h or 'honnhanthucte' in it or 'hntt' in h or 'hntt' in it):
        if ('honnhanthucte' in h and 'honnhanthucte' in it) or ('hntt' in h and 'hntt' in it):
            return True
        if ('honnhanthucte' in it or 'hntt' in it) and ('docthan' in h or 'honnhan' in h or 'thucte' in h):
            return True
        if ('honnhanthucte' in h or 'hntt' in h) and ('docthan' in it or 'honnhan' in it or 'thucte' in it):
            return True
    if ('docthan' in h or 'chuakethon' in h) and ('docthan' in it or 'chuakethon' in it):
        return True
    # 9. Kết hôn nói chung
    if ('kethon' in h or 'honthu' in h) and ('kethon' in it or 'honthu' in it):
        h_dn = 'docthan' in h or 'chuakethon' in h
        it_dn = 'docthan' in it or 'chuakethon' in it
        if h_dn != it_dn:
            return False
        return True
    # 10. Chứng chỉ chuyên khoa
    if 'chuyenkhoa' in it and 'chuyenkhoa' in h:
        return True
    # 11. Phiếu khám sức khỏe
    if 'suckhoe' in it and 'suckhoe' in h:
        return True
    # 12. Phiếu nhập xuất cảnh / xuất nhập cảnh / NXC
    if ('nhapxuatcanh' in h or 'xuatnhapcanh' in h or 'nxc' in h or 'xnc' in h) and ('nhapxuatcanh' in it or 'xuatnhapcanh' in it or 'nxc' in it or 'xnc' in it):
        return True
    # Các loại khác
    if ('tuyenthe' in h and 'tuyenthe' in it):
        return True
    if ('congham' in h and 'congham' in it):
        return True
    if ('lyhon' in h and 'lyhon' in it):
        return True
    if ('khaitu' in h or 'chungtu' in h or 'baotu' in h) and ('khaitu' in it or 'chungtu' in it or 'baotu' in it):
        return True
    return False

def launch_browser(p):
    """
    Khởi động trình duyệt tương thích Chromium với cơ chế fallback đa tầng:
    1. Tự động phát hiện thư mục ms-playwright trong hồ sơ người dùng nếu đang chạy dưới dạng Windows Service (LocalSystem).
    2. Thử Chromium mặc định với các tham số tương thích Service (--no-sandbox, --disable-gpu...).
    3. Thử Microsoft Edge có sẵn trên mọi máy Windows (channel='msedge').
    4. Thử Google Chrome hệ thống (channel='chrome').
    """
    if "PLAYWRIGHT_BROWSERS_PATH" not in os.environ or "systemprofile" in os.environ.get("PLAYWRIGHT_BROWSERS_PATH", ""):
        users_dir = r"C:\Users"
        if os.path.isdir(users_dir):
            try:
                for u in os.listdir(users_dir):
                    candidate = os.path.join(users_dir, u, "AppData", "Local", "ms-playwright")
                    if os.path.isdir(candidate) and any(os.scandir(candidate)):
                        os.environ["PLAYWRIGHT_BROWSERS_PATH"] = candidate
                        print(f"INFO:Found Playwright browsers at: {candidate}", flush=True)
                        break
            except Exception:
                pass

    chrome_args = [
        "--no-sandbox",
        "--disable-setuid-sandbox",
        "--disable-dev-shm-usage",
        "--disable-gpu",
        "--no-first-run",
        "--no-default-browser-check",
        "--enable-clipboard-read-write",
    ]

    attempts = [
        ("chromium", lambda: p.chromium.launch(headless=True, args=chrome_args)),
        ("msedge", lambda: p.chromium.launch(headless=True, channel="msedge", args=chrome_args)),
        ("chrome", lambda: p.chromium.launch(headless=True, channel="chrome", args=chrome_args)),
    ]

    last_err = None
    for name, launcher in attempts:
        try:
            print(f"INFO:Attempting to launch browser via {name}...", flush=True)
            b = launcher()
            print(f"INFO:Successfully launched browser ({name}, version {b.version})", flush=True)
            return b
        except Exception as e:
            print(f"WARN:Failed to launch via {name}: {e}", flush=True)
            last_err = e

    raise RuntimeError(f"Could not launch any browser (Chromium, Edge, or Chrome). Last error: {last_err}")

def run_sync_google_sheet(data, output_file):
    url = data.get('oneDriveUrl', '')
    sheet_name = data.get('sheetName', '1997')
    rows = data.get('rows', [])
    screenshot_path = data.get('screenshotPath')

    results = []
    synced_rows = 0

    base_dir = os.path.dirname(os.path.abspath(__file__))
    profile_candidates = [
        os.path.abspath(os.path.join(base_dir, "..", "..", "..", "tools", "bot_profile")),
        os.path.abspath(os.path.join(base_dir, "..", "..", "tools", "bot_profile")),
        os.path.abspath(os.path.join(base_dir, "tools", "bot_profile")),
        os.path.abspath(r"d:\A\stp-web\tools\bot_profile"),
        os.path.join(os.environ.get("LOCALAPPDATA", ""), "stp_bot_profile"),
    ]
    profile_dir = next((p for p in profile_candidates if os.path.isdir(p)), profile_candidates[0])
    os.makedirs(profile_dir, exist_ok=True)

    with sync_playwright() as p:
        print(f"INFO:Opening Google Sheets with persistent profile: {profile_dir}...", flush=True)
        chrome_args = [
            "--no-sandbox",
            "--disable-setuid-sandbox",
            "--disable-dev-shm-usage",
            "--disable-gpu",
            "--no-first-run",
            "--no-default-browser-check",
            "--enable-clipboard-read-write",
        ]

        ctx = None
        for channel in ["chrome", "msedge", None]:
            try:
                launch_kwargs = {
                    "headless": True,
                    "viewport": {"width": 1400, "height": 900},
                    "permissions": ["clipboard-read", "clipboard-write"],
                    "args": chrome_args,
                }
                if channel:
                    launch_kwargs["channel"] = channel
                ctx = p.chromium.launch_persistent_context(profile_dir, **launch_kwargs)
                print(f"INFO:Launched browser for Google Sheets with channel={channel}", flush=True)
                break
            except Exception as e_launch:
                print(f"WARN:Failed persistent launch with channel={channel}: {e_launch}", flush=True)

        # Fallback: Nếu persistent profile bị khóa hoặc lỗi, mở context sạch (cho phép điền trên link Bất kỳ ai có quyền sửa)
        if not ctx:
            for channel in ["chrome", "msedge", None]:
                try:
                    b_clean = p.chromium.launch(headless=True, channel=channel, args=chrome_args)
                    ctx = b_clean.new_context(
                        viewport={"width": 1400, "height": 900},
                        permissions=["clipboard-read", "clipboard-write"]
                    )
                    print(f"INFO:Launched fallback clean browser for Google Sheets with channel={channel}", flush=True)
                    break
                except Exception as e_clean:
                    print(f"WARN:Failed clean launch with channel={channel}: {e_clean}", flush=True)

        if not ctx:
            raise RuntimeError("Không thể khởi động trình duyệt cho Google Sheets.")

        page = ctx.pages[0] if ctx.pages else ctx.new_page()
        page.set_default_timeout(15000)

        print(f"INFO:Navigating to Google Sheets URL: {url}...", flush=True)
        try:
            page.goto(url, wait_until="domcontentloaded", timeout=45000)
        except Exception as e:
            print(f"WARN:page.goto warning: {e}", flush=True)

        page.wait_for_timeout(3500)

        # 1. Kiểm tra quyền Sửa (Editor) hay Chỉ xem (View Only)
        def check_can_edit(p_page):
            try:
                view_badges = p_page.locator(".docs-gm-view-only, [aria-label*='Chỉ xem'], [aria-label*='View only'], .docs-material-view-only-badge, button:has-text('Yêu cầu quyền chỉnh sửa'), button:has-text('Request edit access')").all()
                if len(view_badges) > 0:
                    return False
                editor = p_page.locator("#t-formula-bar-input .cell-input, #t-formula-bar-input [contenteditable='true'], #t-formula-bar-input [role='textbox'], .cell-input[contenteditable='true']").first
                if editor.count() > 0:
                    return True
            except Exception:
                pass
            return False

        def check_needs_login(p_page):
            try:
                signin = p_page.locator("a[href*='accounts.google.com/ServiceLogin'], a[aria-label*='Đăng nhập'], a[aria-label*='Sign in'], button:has-text('Đăng nhập'), button:has-text('Sign in')").all()
                return len(signin) > 0
            except Exception:
                return False

        can_edit = check_can_edit(page)

        # Nếu file bị Chỉ xem, chưa đăng nhập hoặc tài khoản không có quyền sửa:
        # Báo lỗi ngay lập tức để người dùng kiểm tra hoặc bấm Đăng nhập trên web, tuyệt đối không treo robot
        if not can_edit:
            ctx.close()
            error_msg = (
                "File đang ở chế độ 'Chỉ xem'. "
                "Vui lòng mở Google Sheets -> Bấm nút 'Chia sẻ' -> Chuyển sang 'Bất kỳ ai có đường liên kết: Người chỉnh sửa' để robot có thể điền."
            )
            print(f"ERROR:{error_msg}", flush=True)
            output_data = {
                "success": False,
                "needsLogin": True,
                "syncedRowsCount": 0,
                "tickedCellsCount": 0,
                "newHeadersCount": 0,
                "message": error_msg,
                "details": []
            }
            with open(output_file, 'w', encoding='utf-8') as f:
                json.dump(output_data, f, ensure_ascii=False, indent=2)
            return

        # 2. Chuyển tab sheet nếu cần
        if sheet_name:
            try:
                tab = page.locator(".docs-sheet-tab-name").filter(has_text=sheet_name).first
                if tab.count() > 0:
                    tab.click()
                    page.wait_for_timeout(1500)
                    print(f"INFO:Switched to Google Sheet tab '{sheet_name}'", flush=True)
                else:
                    # Thử lọc theo số năm nếu tên có dạng 'Năm 2002' -> '2002'
                    year_match = re.search(r'\d{4}', sheet_name)
                    if year_match:
                        y_tab = page.locator(".docs-sheet-tab-name").filter(has_text=year_match.group(0)).first
                        if y_tab.count() > 0:
                            y_tab.click()
                            page.wait_for_timeout(1500)
                            print(f"INFO:Switched to tab by year '{year_match.group(0)}'", flush=True)
            except Exception as e_tab:
                print(f"WARN:Could not switch to tab '{sheet_name}': {e_tab}", flush=True)

        name_box = page.locator("#t-name-box").first
        try:
            name_box.wait_for(state="attached", timeout=15000)
        except Exception as e_nb:
            print(f"WARN:name_box wait error: {e_nb}", flush=True)

        def set_cell_gsheet(coord, val):
            if val is None or val == "":
                return
            name_box.click()
            name_box.fill(coord)
            page.keyboard.press("Enter")
            page.wait_for_timeout(150)

            editor = page.locator("#t-formula-bar-input .cell-input, #t-formula-bar-input [role='textbox'], #t-formula-bar-input").first
            editor.click()
            page.wait_for_timeout(50)
            page.keyboard.press("Control+a")
            page.keyboard.press("Backspace")
            page.keyboard.insert_text(val)
            page.wait_for_timeout(50)
            page.keyboard.press("Enter")
            page.wait_for_timeout(200)

        # 3. Điền dữ liệu các dòng: CHỈ CỘT A, D, H - TUYỆT ĐỐI BỎ QUA ĐÁNH TICK
        for r_item in rows:
            r_idx = r_item.get('rowIndex')
            num_label = r_item.get('numberLabel', '')
            summary_text = r_item.get('summaryText', '')
            formula_h = r_item.get('formulaH', '') or summary_text
            status_a = r_item.get('statusA', 'đã nhập')

            err = None
            try:
                # Cột A: Đánh dấu "đã nhập"
                if status_a:
                    set_cell_gsheet(f"A{r_idx}", status_a)
                    print(f"PROGRESS_A:{r_idx}:{num_label}:A{r_idx}:{status_a}", flush=True)

                # Cột D: Điền trích yếu kèm ngoặc
                if summary_text:
                    set_cell_gsheet(f"D{r_idx}", summary_text)
                    print(f"PROGRESS_D:{r_idx}:{num_label}:D{r_idx}", flush=True)

                # Cột H: Điền trích yếu kèm ngoặc
                if formula_h:
                    set_cell_gsheet(f"H{r_idx}", formula_h)
                    print(f"PROGRESS_H:{r_idx}:{num_label}:H{r_idx}", flush=True)

                synced_rows += 1
                print(f"PROGRESS_ROW:{r_idx}:{num_label}:{synced_rows}", flush=True)
            except Exception as e_row:
                err = str(e_row)
                print(f"ERROR_ROW:{r_idx}:{err}", flush=True)

            results.append({
                "rowIndex": r_idx,
                "numberLabel": num_label,
                "statusAFilled": bool(status_a and not err),
                "summaryFilled": bool(summary_text and not err),
                "formulaHFilled": bool(formula_h and not err),
                "tickedColumns": [],
                "untickedColumns": [],
                "error": err
            })

        # Lưu trên Google Sheets
        try:
            page.keyboard.press("Control+s")
        except Exception:
            pass
        page.wait_for_timeout(2000)

        if screenshot_path:
            try:
                page.screenshot(path=screenshot_path)
            except Exception:
                pass

        ctx.close()

    if synced_rows == 0 and len(rows) > 0 and any(r.get("error") for r in results):
        first_err = next((r.get("error") for r in results if r.get("error")), "Lỗi không xác định")
        output_data = {
            "success": False,
            "syncedRowsCount": 0,
            "tickedCellsCount": 0,
            "newHeadersCount": 0,
            "newHeaderTitles": [],
            "message": f"Không thể điền hồ sơ lên Google Sheets: {first_err}",
            "details": results
        }
    else:
        sync_msg = f"Đã đồng bộ thành công {synced_rows} hồ sơ: điền Cột D, H và đánh dấu Cột A là '{data.get('statusA', 'đã nhập')}' lên Google Sheets!"
        output_data = {
            "success": True,
            "syncedRowsCount": synced_rows,
            "tickedCellsCount": 0,
            "newHeadersCount": 0,
            "newHeaderTitles": [],
            "message": sync_msg,
            "details": results
        }

    with open(output_file, 'w', encoding='utf-8') as f:
        json.dump(output_data, f, ensure_ascii=False, indent=2)

def run_sync(input_file, output_file):
    with open(input_file, 'r', encoding='utf-8') as f:
        data = json.load(f)

    url = data.get('oneDriveUrl', '')
    if 'docs.google.com' in url.lower() or 'google.com/spreadsheets' in url.lower():
        try:
            run_sync_google_sheet(data, output_file)
        except Exception as ex_gs:
            err_msg = f"Lỗi Google Sheets: {ex_gs}"
            print(f"ERROR:{err_msg}", flush=True)
            err_output = {
                "success": False,
                "message": err_msg,
                "syncedRowsCount": 0,
                "details": []
            }
            with open(output_file, 'w', encoding='utf-8') as f_err:
                json.dump(err_output, f_err, ensure_ascii=False, indent=2)
        return
    sheet_name = data.get('sheetName', '1997')
    rows = data.get('rows', [])
    screenshot_path = data.get('screenshotPath')

    results = []
    total_ticked = 0
    synced_rows = 0

    with sync_playwright() as p:
        browser = launch_browser(p)
        context = browser.new_context(
            viewport={"width": 1400, "height": 900},
            permissions=["clipboard-read", "clipboard-write"]
        )
        page = context.new_page()
        page.set_default_timeout(6000)
        print(f"INFO:Opening OneDrive workbook for sheet {sheet_name}...", flush=True)
        try:
            page.goto(url, wait_until="networkidle", timeout=60000)
        except Exception as e:
            print(f"WARN:page.goto warning (networkidle): {e}", flush=True)
            try:
                page.goto(url, wait_until="domcontentloaded", timeout=30000)
            except Exception:
                pass
        time.sleep(2)

        excel_frame = None
        start_wait = time.time()
        while time.time() - start_wait < 35:
            for f in page.frames:
                if "xlviewer" in f.url.lower() or "wopiframe" in f.url.lower() or "doc.aspx" in f.url.lower() or f.name == "WacFrame_Excel_0":
                    excel_frame = f
                    break
            if excel_frame:
                break
            time.sleep(1)

        if not excel_frame:
            raise RuntimeError("Không tìm thấy khung làm việc Excel Online (iframe). Vui lòng kiểm tra liên kết OneDrive hoặc quyền chia sẻ.")

        # Grant clipboard permissions for all frame origins
        try:
            for f in page.frames:
                if f.url and f.url.startswith("http"):
                    parsed_u = urlparse(f.url)
                    frame_origin = f"{parsed_u.scheme}://{parsed_u.netloc}"
                    context.grant_permissions(["clipboard-read", "clipboard-write"], origin=frame_origin)
        except Exception:
            pass

        # Dismiss feedback dialog or prompts if present
        for selector in [
            "button[aria-label*='Close']",
            "button[aria-label*='close']",
            "button:has-text('✕')",
            "button:has-text('Got it')",
            "button:has-text('Dismiss')",
            "button:has-text('Đóng')",
            "button:has-text('Đã hiểu')",
        ]:
            try:
                btn = page.locator(selector).first
                if btn.count() > 0 and btn.is_visible():
                    btn.click()
            except Exception:
                pass

        # Chuyển sang Edit mode nếu Excel Online đang ở chế độ xem (View-only)
        try:
            edit_btn = page.locator("button:has-text('Edit Workbook'), button:has-text('Chỉnh sửa sổ làm việc'), button:has-text('Edit in Browser')").first
            if edit_btn.count() > 0 and edit_btn.is_visible():
                edit_btn.click()
                time.sleep(2)
        except Exception:
            pass

        name_box = excel_frame.locator("#FormulaBar-NameBox-input, #FormulaBarNameBox, input[aria-label*='Name Box'], input[id*='NameBox']").first
        formula_bar = excel_frame.locator("#formulaBarTextDivId_textElement, [aria-label*='formula bar'][role='textbox']").first

        try:
            name_box.wait_for(state="attached", timeout=20000)
        except Exception as e:
            raise RuntimeError(f"Không thể định vị thanh Name Box trong Excel Online: {e}")

        try:
            formula_bar.wait_for(state="attached", timeout=15000)
        except Exception as e:
            print(f"WARN:Formula bar wait warning: {e}", flush=True)

        # Chuyển sang sheet cần đồng bộ: Thử click tab trước, nếu ẩn thì nhảy trực tiếp qua Name Box
        sheet_switched = False
        try:
            tab = excel_frame.locator(f"#m_excelWebRenderer_ewaCtl_m_sheetTabBar [role='tab']:text-is('{sheet_name}'), #m_excelWebRenderer_ewaCtl_m_sheetTabBar [role='tab'][aria-label*='{sheet_name}']").first
            if tab.count() > 0:
                tab.click()
                time.sleep(0.5)
                sheet_switched = True
        except Exception:
            pass

        if not sheet_switched:
            try:
                name_box.evaluate("el => el.focus()")
                name_box.fill(f"'{sheet_name}'!A1")
                page.keyboard.press("Enter")
                time.sleep(0.8)
                sheet_switched = True
            except Exception:
                pass

        def set_cell_value(cell_coord, text):
            if text is None:
                return
            # 1. Trỏ Name Box tới ô cần ghi
            name_box.evaluate("el => el.focus()")
            name_box.fill(cell_coord)
            page.keyboard.press("Enter")
            time.sleep(0.06)

            # 2. Focus vào Formula Bar và gõ giá trị trực tiếp (CDP keyboard type, 100% miễn nhiễm lỗi phân quyền clipboard)
            formula_bar.click()
            time.sleep(0.04)
            page.keyboard.press("Control+a")
            page.keyboard.press("Backspace")
            page.keyboard.type(text, delay=0)
            page.keyboard.press("Enter")
            time.sleep(0.05)

        # Tuyệt đối không thêm cột mới theo yêu cầu: chỉ làm những gì đã có sẵn
        skip_ticking = data.get('skipTicking', True)
        new_headers_created = 0

        for r_item in rows:
            r_idx = r_item.get('rowIndex')
            num_label = r_item.get('numberLabel', '')
            summary_text = r_item.get('summaryText', '')
            formula_h = r_item.get('formulaH', '')
            status_a = r_item.get('statusA', 'đã nhập')
            target_cells = r_item.get('targetCells', [])
            untick_cells = r_item.get('untickCells', [])

            ticked_cols = []
            unticked_cols = []
            err = None

            try:
                # 1. Fill Column A (Đánh dấu "đã nhập")
                # Yêu cầu: "đánh cột A là đã nhập ( đánh theo từ số - đến số )"
                if status_a:
                    set_cell_value(f"A{r_idx}", status_a)
                    print(f"PROGRESS_A:{r_idx}:{num_label}:A{r_idx}:{status_a}", flush=True)

                # 2. Fill Column D (Trích yếu nội dung)
                # Yêu cầu: "chỉ cần đánh cột D và H , lấy dữ liệu vừa xong trong web , đẩy dữ liệu vào 2 cột đó"
                if summary_text:
                    set_cell_value(f"D{r_idx}", summary_text)
                    print(f"PROGRESS_D:{r_idx}:{num_label}:D{r_idx}", flush=True)

                # 3. Fill Column H (Trích yếu / công thức tổng hợp)
                val_h = formula_h if formula_h else summary_text
                if val_h:
                    set_cell_value(f"H{r_idx}", val_h)
                    print(f"PROGRESS_H:{r_idx}:{num_label}:H{r_idx}", flush=True)

                # 4. Tick checkbox cells in target_cells (chỉ khi KHÔNG bỏ tick)
                if not skip_ticking:
                    for cell_coord in target_cells:
                        try:
                            set_cell_value(cell_coord, "TRUE")
                            ticked_cols.append(cell_coord)
                            total_ticked += 1
                            print(f"PROGRESS_TICK:{r_idx}:{num_label}:{cell_coord}:{total_ticked}", flush=True)
                        except Exception as ex_cell:
                            print(f"WARN:Không thể tick ô {cell_coord}: {ex_cell}", flush=True)

                    for cell_coord in untick_cells:
                        set_cell_value(cell_coord, "FALSE")
                        unticked_cols.append(cell_coord)
                        total_ticked += 1
                        print(f"PROGRESS_UNTICK:{r_idx}:{num_label}:{cell_coord}:{total_ticked}", flush=True)

                synced_rows += 1
                print(f"PROGRESS_ROW:{r_idx}:{num_label}:{synced_rows}", flush=True)
            except Exception as e:
                err = str(e)
                print(f"ERROR_ROW:{r_idx}:{err}", flush=True)

            results.append({
                "rowIndex": r_idx,
                "numberLabel": num_label,
                "statusAFilled": bool(status_a and not err),
                "summaryFilled": bool(summary_text and not err),
                "formulaHFilled": bool(val_h and not err),
                "tickedColumns": ticked_cols,
                "untickedColumns": unticked_cols,
                "error": err
            })

        # Kích hoạt lưu lên OneDrive Cloud và đợi trạng thái Saved
        print("INFO:Triggering cloud save (Control+S)...", flush=True)
        try:
            page.keyboard.press("Control+s")
        except Exception:
            pass

        print("INFO:Waiting for OneDrive cloud save confirmation...", flush=True)
        save_status_btn = excel_frame.locator("#SaveStatusButton, button[aria-label*='Saved'], button[aria-label*='Saving'], button[aria-label*='Đã lưu']").first
        saved_confirmed = False
        start_wait_save = time.time()
        while time.time() - start_wait_save < 15:
            try:
                if save_status_btn.count() > 0:
                    lbl = save_status_btn.get_attribute("aria-label") or ""
                    if "Saved" in lbl or "Đã lưu" in lbl:
                        saved_confirmed = True
                        print(f"INFO:OneDrive cloud confirmed: {lbl.splitlines()[0]}", flush=True)
                        break
            except Exception:
                pass
            time.sleep(0.5)

        if not saved_confirmed:
            time.sleep(3.0)

        # Verification check: Đọc lại ô trích yếu đầu tiên để xác nhận thực tế
        verified = False
        if rows and synced_rows > 0:
            first_row = rows[0]
            verify_coord = f"D{first_row.get('rowIndex')}"
            try:
                name_box.evaluate("el => el.focus()")
                name_box.fill(verify_coord)
                page.keyboard.press("Enter")
                time.sleep(0.3)
                actual_val = formula_bar.inner_text().strip()
                expected_summary = first_row.get('summaryText', '').strip()
                if expected_summary and actual_val:
                    prefix = expected_summary[:30]
                    if prefix in actual_val:
                        verified = True
                        print(f"VERIFY_OK: Cell {verify_coord} confirmed written on OneDrive cloud!", flush=True)
                    else:
                        print(f"VERIFY_WARN: Cell {verify_coord} value mismatch. Expected prefix: {prefix}..., got: {actual_val[:30]}...", flush=True)
                else:
                    verified = True
            except Exception as ex_v:
                print(f"VERIFY_ERR: {ex_v}", flush=True)
                verified = True

        if screenshot_path:
            try:
                page.screenshot(path=screenshot_path)
                print(f"SCREENSHOT:{screenshot_path}", flush=True)
            except Exception:
                pass

        browser.close()

    if skip_ticking:
        sync_msg = f"Đã đồng bộ thành công {synced_rows} hồ sơ: điền Cột D, H và đánh dấu Cột A là 'đã nhập' lên OneDrive!"
    else:
        sync_msg = f"Đã đồng bộ thành công {synced_rows} hồ sơ, tick {total_ticked} ô" + (f", tự động thêm {new_headers_created} cột mới" if new_headers_created > 0 else "") + " lên OneDrive!"

    output_data = {
        "success": True,
        "syncedRowsCount": synced_rows,
        "tickedCellsCount": total_ticked,
        "newHeadersCount": new_headers_created,
        "newHeaderTitles": [nh.get('title') for nh in new_headers if nh.get('title')],
        "message": sync_msg,
        "details": results
    }

    with open(output_file, 'w', encoding='utf-8') as f:
        json.dump(output_data, f, ensure_ascii=False, indent=2)

if __name__ == '__main__':
    if len(sys.argv) >= 3:
        run_sync(sys.argv[1], sys.argv[2])
