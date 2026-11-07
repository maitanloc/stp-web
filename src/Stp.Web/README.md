# STP Archive — SPA

Giao diện Vue 3 + TypeScript + Vite cho STP Archive. SPA đọc dữ liệu từ
ASP.NET Core API (`src/Stp.Api`) qua các endpoint `/api/*`.

## Chạy khi phát triển

```powershell
# Terminal 1: API
dotnet run --project src/Stp.Api --urls http://localhost:5215

# Terminal 2: SPA
cd src/Stp.Web
npm install
npm run dev
```

Mở `http://127.0.0.1:5173`. Vite proxy `/api` sang `http://localhost:5215`,
nên không cần cấu hình CORS khi phát triển.

## Build

```powershell
npm run build
```

Kết quả nằm trong `dist/`. Có thể phục vụ tĩnh bằng IIS/nginx hoặc tích hợp
vào API khi đóng gói; khi đó cần fallback mọi route không phải `/api/*` về
`index.html`.

## Cấu trúc chính

- `src/router.ts` — route non-tech, developer độc lập và bản in.
- `src/components/AppShell.vue` — sidebar, topbar, health chip, dark mode.
- `src/components/DeveloperWorkspace.vue` — cổng đăng nhập + macro/job/sync/inspect.
- `src/views/` — dashboard, chuyển đổi, gộp, đồng bộ, kho hồ sơ, bản in, job.
- `src/api/` — client fetch, JWT developer, kiểu DTO.
- `src/style.css` — design system teal/indigo, dark mode và CSS in A4.

## Ghi chú

- Token developer lưu ở `localStorage["stp.dev.token"]`.
- Theme lưu ở `localStorage["stp.theme"]`.
- Bản in `/in/:id` dùng khổ A4 dọc 210 × 297 mm, font Times New Roman.
- Không commit `dist/`, `node_modules/` hoặc dữ liệu hồ sơ.
