import { createRouter, createWebHistory, type RouteRecordRaw } from 'vue-router'
import AppShell from './components/AppShell.vue'

const routes: RouteRecordRaw[] = [
  {
    path: '/',
    component: AppShell,
    children: [
      {
        path: '',
        name: 'dashboard',
        component: () => import('./views/DashboardView.vue'),
        meta: { title: 'Tổng quan', subtitle: 'Tình hình kho hồ sơ và các job gần đây' },
      },
      {
        path: 'chuyen-doi',
        name: 'convert',
        component: () => import('./views/ConvertView.vue'),
        meta: {
          title: 'Chuyển đổi Excel',
          subtitle: 'Biến workbook gốc thành .xlsm có bộ công cụ in và macro',
        },
      },
      {
        path: 'gop-file',
        name: 'merge',
        component: () => import('./views/MergeView.vue'),
        meta: {
          title: 'Gộp nhiều file',
          subtitle: 'Hợp nhất các workbook cùng cấu trúc thành một file duy nhất',
        },
      },
      {
        path: 'dong-bo',
        name: 'sync',
        component: () => import('./views/SyncView.vue'),
        meta: {
          title: 'Đồng bộ từ Excel',
          subtitle: 'Đưa dữ liệu sheet ML đã chỉnh sửa trở lại SQL Server',
        },
      },
      {
        path: 'kho',
        name: 'archive',
        component: () => import('./views/ArchiveView.vue'),
        meta: { title: 'Kho hồ sơ', subtitle: 'Tra cứu workbook và hồ sơ đã đồng bộ' },
      },
      {
        path: 'kho/:id',
        name: 'archive-detail',
        component: () => import('./views/ArchiveDetailView.vue'),
        meta: { title: 'Chi tiết kho', subtitle: 'Lọc hồ sơ theo năm, loại và nội dung' },
      },
      {
        path: 'ho-so/:id',
        name: 'dossier',
        component: () => import('./views/DossierView.vue'),
        meta: { title: 'Chi tiết hồ sơ', subtitle: 'Danh mục văn bản, toàn văn và ghi chú' },
      },
      {
        path: 'job/:id',
        name: 'job',
        component: () => import('./views/JobView.vue'),
        meta: { title: 'Tiến độ job', subtitle: 'Theo dõi xử lý, tải kết quả và xem nhật ký' },
      },
      {
        path: 'nang-cao',
        name: 'developer-embedded',
        component: () => import('./views/DeveloperView.vue'),
        props: { standalone: false },
        meta: {
          title: 'Không gian nâng cao',
          subtitle: 'Macro, job, đồng bộ và kiểm tra file dành cho kỹ thuật viên',
        },
      },
    ],
  },
  {
    path: '/developer',
    name: 'developer',
    component: () => import('./views/DeveloperView.vue'),
    props: { standalone: true },
    meta: { title: 'STP Archive · Developer' },
  },
  {
    path: '/in/:id',
    name: 'print',
    component: () => import('./views/PrintView.vue'),
    meta: { title: 'Bản in hồ sơ' },
  },
  {
    path: '/:pathMatch(.*)*',
    name: 'not-found',
    component: () => import('./views/NotFoundView.vue'),
    meta: { title: 'Không tìm thấy trang' },
  },
]

const router = createRouter({
  history: createWebHistory(),
  routes,
  scrollBehavior: () => ({ top: 0 }),
})

export default router
