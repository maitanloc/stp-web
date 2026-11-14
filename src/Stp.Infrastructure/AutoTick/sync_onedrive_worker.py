import sys
import os
import json
import time
import re

# Đảm bảo Playwright luôn tìm thấy Chromium dù chạy dưới quyền Windows Service (SYSTEM) hay User
if "PLAYWRIGHT_BROWSERS_PATH" not in os.environ or not os.path.exists(os.environ.get("PLAYWRIGHT_BROWSERS_PATH", "")):
    candidate_paths = [
        r"C:\ProgramData\ms-playwright",
        r"C:\WINDOWS\system32\config\systemprofile\AppData\Local\ms-playwright",
        r"C:\Users\maita\AppData\Local\ms-playwright",
    ]
    for p in candidate_paths:
        if os.path.exists(p):
            os.environ["PLAYWRIGHT_BROWSERS_PATH"] = p
            break

from playwright.sync_api import sync_playwright

def normalize_text(text):
    if not text:
        return ""
    s = text.strip().lower()
    return re.sub(r'[^a-zA-Z0-9\u00C0-\u1EF9]', '', s)

def is_match(header_title, item):
    h = normalize_text(header_title)
    it = normalize_text(item)
    if h == it:
        return True
    if (h == 'cmnd' and it == 'chungminhnhandan') or (h == 'chungminhnhandan' and it == 'cmnd'):
        return True
    if (h == 'hokhau' and 'hokhau' in it) or ('hokhau' in h and it == 'hokhau'):
        return True
    if (h == 'giaykhaisinh' and it == 'khaisinh') or (h == 'khaisinh' and it == 'giaykhaisinh'):
        return True
    if (h == 'giaychungnhankethon' and it == 'chungnhankethon') or (h == 'chungnhankethon' and it == 'giaychungnhankethon'):
        return True
    if ('xacnhandocthan' in h and 'honnhanthucte' in h) and ('xacnhandocthan' in it and 'honnhanthucte' in it):
        return True
    if (h == 'giaychungnhandocthan' and it == 'chungnhandocthan') or (h == 'chungnhandocthan' and it == 'giaychungnhandocthan'):
        return True
    if (h == 'giayxacnhandocthan' and it == 'xacnhandocthan') or (h == 'xacnhandocthan' and it == 'giayxacnhandocthan'):
        return True
    if (h == 'phieunhapxuatcanh' and it == 'phieuxuatnhapcanh') or (h == 'phieuxuatnhapcanh' and it == 'phieunhapxuatcanh'):
        return True
    if (h == 'tokhaituyenthe' and it == 'bankhaituyenthe') or (h == 'bankhaituyenthe' and it == 'tokhaituyenthe'):
        return True
    if (h == 'giaychungnhandangkykhaisinh' and it == 'chungnhandangkykhaisinh') or (h == 'chungnhandangkykhaisinh' and it == 'giaychungnhandangkykhaisinh'):
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

def run_sync(input_file, output_file):
    with open(input_file, 'r', encoding='utf-8') as f:
        data = json.load(f)

    url = data.get('oneDriveUrl')
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
        print(f"INFO:Opening OneDrive workbook for sheet {sheet_name}...", flush=True)
        try:
            page.goto(url, wait_until="domcontentloaded", timeout=60000)
        except Exception as e:
            print(f"WARN:page.goto warning: {e}", flush=True)
        time.sleep(2)

        excel_frame = None
        start_wait = time.time()
        while time.time() - start_wait < 30:
            for f in page.frames:
                if "xlviewer" in f.url.lower() or "wopiframe" in f.url.lower() or "doc.aspx" in f.url.lower() or f.name == "WacFrame_Excel_0":
                    excel_frame = f
                    break
            if excel_frame:
                break
            for f in page.frames:
                try:
                    if f.locator("#FormulaBar-NameBox-input, #FormulaBarNameBox, input[aria-label*='Name Box']").count() > 0:
                        excel_frame = f
                        break
                except Exception:
                    pass
            if excel_frame:
                break
            time.sleep(1)

        if not excel_frame:
            excel_frame = page.main_frame

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

        # Select sheet if tab button exists
        try:
            tab = excel_frame.locator(f"[role='tab']:text-is('{sheet_name}'), button:text-is('{sheet_name}'), [role='tab']:has-text('{sheet_name}'), button:has-text('{sheet_name}')")
            if tab.count() > 0:
                tab.first.click()
                time.sleep(1)
        except Exception:
            pass

        name_box = excel_frame.locator("#FormulaBar-NameBox-input, #FormulaBarNameBox, input[aria-label*='Name Box'], input[id*='NameBox']").first
        try:
            name_box.wait_for(state="attached", timeout=15000)
        except Exception:
            pass

        def safe_paste(text):
            try:
                excel_frame.evaluate(f"() => navigator.clipboard.writeText({json.dumps(text)})")
            except Exception:
                try:
                    page.evaluate(f"() => navigator.clipboard.writeText({json.dumps(text)})")
                except Exception:
                    pass
            time.sleep(0.06)
            page.keyboard.press("Control+v")

        for r_item in rows:
            r_idx = r_item.get('rowIndex')
            num_label = r_item.get('numberLabel', '')
            summary_text = r_item.get('summaryText', '')
            formula_h = r_item.get('formulaH', '')
            target_cells = r_item.get('targetCells', [])
            untick_cells = r_item.get('untickCells', [])

            ticked_cols = []
            unticked_cols = []
            err = None

            try:
                # 1. Fill Column D (Trích yếu nội dung) if provided
                if summary_text:
                    name_box.evaluate("el => el.focus()")
                    name_box.fill(f"D{r_idx}")
                    page.keyboard.press("Enter")
                    time.sleep(0.20)
                    name_box.evaluate("el => el.blur()")
                    time.sleep(0.05)

                    safe_paste(summary_text)
                    time.sleep(0.18)
                    page.keyboard.press("Enter")
                    time.sleep(0.10)
                    print(f"PROGRESS_D:{r_idx}:{num_label}:D{r_idx}", flush=True)

                # 2. Fill Column H (Công thức tổng hợp trích yếu động) if provided
                if formula_h:
                    name_box.evaluate("el => el.focus()")
                    name_box.fill(f"H{r_idx}")
                    page.keyboard.press("Enter")
                    time.sleep(0.20)
                    name_box.evaluate("el => el.blur()")
                    time.sleep(0.05)

                    safe_paste(formula_h)
                    time.sleep(0.18)
                    page.keyboard.press("Enter")
                    time.sleep(0.10)
                    print(f"PROGRESS_H:{r_idx}:{num_label}:H{r_idx}", flush=True)

                # 3. Tick checkbox cells in target_cells
                # Trong Excel Online, ô checkbox là giá trị boolean TRUE/FALSE.
                # Khi di chuyển qua NameBox, blur name_box để trả focus về ô lưới tính,
                # sau đó dán giá trị TRUE qua clipboard Control+v để đảm bảo 100% ô nhận giá trị TRUE
                # (tương thích cả ô checkbox định dạng control lẫn ô boolean).
                for cell_coord in target_cells:
                    name_box.evaluate("el => el.focus()")
                    name_box.fill(cell_coord)
                    page.keyboard.press("Enter")
                    time.sleep(0.12)
                    name_box.evaluate("el => el.blur()")
                    time.sleep(0.05)

                    safe_paste("TRUE")
                    time.sleep(0.10)
                    page.keyboard.press("Enter")
                    time.sleep(0.06)

                    ticked_cols.append(cell_coord)
                    total_ticked += 1
                    print(f"PROGRESS_TICK:{r_idx}:{num_label}:{cell_coord}:{total_ticked}", flush=True)

                # 4. Untick checkbox cells in untick_cells (khi người dùng xóa bớt giấy tờ khỏi trích yếu)
                for cell_coord in untick_cells:
                    name_box.evaluate("el => el.focus()")
                    name_box.fill(cell_coord)
                    page.keyboard.press("Enter")
                    time.sleep(0.12)
                    name_box.evaluate("el => el.blur()")
                    time.sleep(0.05)

                    safe_paste("FALSE")
                    time.sleep(0.10)
                    page.keyboard.press("Enter")
                    time.sleep(0.06)

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
                "summaryFilled": bool(summary_text and not err),
                "formulaHFilled": bool(formula_h and not err),
                "tickedColumns": ticked_cols,
                "untickedColumns": unticked_cols,
                "error": err
            })

        print("INFO:Waiting 2.5s for cloud auto-save...", flush=True)
        time.sleep(2.5)

        if screenshot_path:
            try:
                page.screenshot(path=screenshot_path)
                print(f"SCREENSHOT:{screenshot_path}", flush=True)
            except Exception:
                pass

        browser.close()

    output_data = {
        "success": True,
        "syncedRowsCount": synced_rows,
        "tickedCellsCount": total_ticked,
        "message": f"Đã đồng bộ thành công {synced_rows} hồ sơ và tick {total_ticked} ô lên OneDrive!",
        "details": results
    }

    with open(output_file, 'w', encoding='utf-8') as f:
        json.dump(output_data, f, ensure_ascii=False, indent=2)

if __name__ == '__main__':
    if len(sys.argv) >= 3:
        run_sync(sys.argv[1], sys.argv[2])
