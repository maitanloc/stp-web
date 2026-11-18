import sys
import os
import json
import time

if "PLAYWRIGHT_BROWSERS_PATH" not in os.environ:
    candidate_paths = [
        r"C:\ProgramData\ms-playwright",
        os.path.expanduser(r"~\AppData\Local\ms-playwright"),
    ]
    for p in candidate_paths:
        if os.path.isdir(p):
            os.environ["PLAYWRIGHT_BROWSERS_PATH"] = p
            break

from playwright.sync_api import sync_playwright

url = sys.argv[1] if len(sys.argv) > 1 else ""

script_dir = os.path.dirname(os.path.abspath(__file__))
profile_candidates = [
    os.path.abspath(os.path.join(script_dir, "bot_profile")),
    os.path.abspath(os.path.join(script_dir, "..", "tools", "bot_profile")),
    os.path.abspath(os.path.join(script_dir, "..", "..", "..", "tools", "bot_profile")),
    r"D:\A\stp-web\tools\bot_profile",
    os.path.join(os.environ.get("LOCALAPPDATA", ""), "stp_bot_profile"),
]
profile_dir = next((p for p in profile_candidates if os.path.isdir(p)), profile_candidates[0])
os.makedirs(profile_dir, exist_ok=True)

if not url or ("docs.google.com" not in url.lower() and "google.com/spreadsheets" not in url.lower()):
    print(json.dumps({"isGoogleSheet": False, "canEdit": True, "needsLogin": False, "message": "Liên kết không phải Google Sheets (OneDrive/Excel Online)"}))
    sys.exit(0)

with sync_playwright() as p:
    ctx = None
    chrome_args = [
        "--no-sandbox",
        "--disable-setuid-sandbox",
        "--disable-dev-shm-usage",
        "--disable-gpu",
        "--no-first-run",
        "--no-default-browser-check",
    ]

    for channel in ["chrome", "msedge", None]:
        try:
            ctx = p.chromium.launch_persistent_context(
                profile_dir,
                headless=True,
                channel=channel,
                args=chrome_args,
                viewport={"width": 1280, "height": 800}
            )
            break
        except Exception:
            pass

    # Nếu persistent context bị lỗi (ví dụ file LOCK dở), thử mở context sạch
    if not ctx:
        for channel in ["chrome", "msedge", None]:
            try:
                browser = p.chromium.launch(headless=True, channel=channel, args=chrome_args)
                ctx = browser.new_context(viewport={"width": 1280, "height": 800})
                break
            except Exception:
                pass

    if not ctx:
        print(json.dumps({"isGoogleSheet": True, "canEdit": False, "needsLogin": False, "message": "Không thể khởi động trình duyệt kiểm tra quyền trên máy chủ"}))
        sys.exit(0)

    page = ctx.pages[0] if ctx.pages else ctx.new_page()
    page.set_default_timeout(15000)

    try:
        page.goto(url, wait_until="domcontentloaded", timeout=25000)
        page.wait_for_timeout(3000)
    except Exception as e:
        try: ctx.close()
        except: pass
        print(json.dumps({"isGoogleSheet": True, "canEdit": False, "needsLogin": False, "message": f"Không thể tải trang: {e}"}))
        sys.exit(0)

    # 1. Check view only badges
    view_badges = page.locator(".docs-gm-view-only, [aria-label*='Chỉ xem'], [aria-label*='View only'], .docs-material-view-only-badge, button:has-text('Yêu cầu quyền chỉnh sửa'), button:has-text('Request edit access')").all()
    
    # 2. Check formula bar editor
    editor = page.locator("#t-formula-bar-input .cell-input, #t-formula-bar-input [contenteditable='true'], #t-formula-bar-input [role='textbox'], .cell-input[contenteditable='true']").first

    can_edit = False
    if len(view_badges) == 0 and editor.count() > 0:
        can_edit = True

    signin = page.locator("a[href*='accounts.google.com/ServiceLogin'], a[aria-label*='Đăng nhập'], a[aria-label*='Sign in'], button:has-text('Đăng nhập'), button:has-text('Sign in')").all()
    needs_login = (not can_edit) and len(signin) > 0

    title = page.title()
    try: ctx.close()
    except: pass

    msg = "Sẵn sàng chỉnh sửa (đầy đủ quyền Sửa trên sheet)." if can_edit else (
        "File đang ở chế độ 'Chỉ xem'. Vui lòng mở Google Sheets -> Bấm 'Chia sẻ' -> Đổi sang 'Bất kỳ ai có đường liên kết: Người chỉnh sửa' (Editor) để robot có thể điền."
    )

    result = {
        "isGoogleSheet": True,
        "canEdit": can_edit,
        "needsLogin": needs_login,
        "title": title,
        "message": msg
    }
    print(json.dumps(result, ensure_ascii=False))
