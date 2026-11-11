import { createRouter, createWebHistory, type RouteRecordRaw } from 'vue-router'
import AppShell from './components/AppShell.vue'
import { initTeamIdentity, teamIdentity } from './composables/useTeamIdentity'

declare module 'vue-router' {
  interface RouteMeta {
    /** Route chỉ dành cho quản trị viên (tài khoản nhóm role admin). */
    admin?: boolean
  }
}

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
        path: 'trich-yeu',
        name: 'summary',
        component: () => import('./views/SummaryToolView.vue'),
        meta: {
          title: 'Bổ sung trích yếu',
          subtitle: 'Tích giấy tờ cần thêm vào ngoặc cuối rồi xuất lại đúng workbook gốc',
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
      {
        path: 'quan-ly',
        name: 'users',
        component: () => import('./views/UsersView.vue'),
        meta: {
          title: 'Quản lý tài khoản',
          subtitle: 'Tạo và quản lý tài khoản thành viên nhóm',
          admin: true,
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
    path: '/in-hang-loat',
    name: 'print-batch',
    component: () => import('./views/PrintBatchView.vue'),
    meta: { title: 'In hàng loạt / Xuất PDF' },
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

// Route admin: kiểm tra quyền từ cookie phiên (không chặn route thường).
router.beforeEach(async (to) => {
  if (!to.meta.admin) return true
  await initTeamIdentity()
  if (teamIdentity.value?.role !== 'admin') {
    return { name: 'dashboard' }
  }
  return true
})

export default router
