<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { RouterLink } from 'vue-router'
import {
  ArrowRight,
  Calendar,
  Clock,
  Cpu,
  Database,
  FileSpreadsheet,
  Files,
  FolderOpen,
  Layers,
  RefreshCw,
  TriangleExclamationPoint,
} from '@lucide/vue'
import { request } from '../api/client'
import type { OverviewResponse } from '../api/types'
import { errorMessage } from '../lib/errors'
import { formatDateTime, formatNumber } from '../lib/format'
import EmptyState from '../components/EmptyState.vue'
import StatusBadge from '../components/StatusBadge.vue'

const overview = ref<OverviewResponse | null>(null)
const loading = ref(true)
const error = ref<string | null>(null)

const stats = computed(() => {
  const data = overview.value
  if (!data) return []
  return [
    {
      label: 'Hồ sơ lưu trữ',
      value: formatNumber(data.dossierCount),
      note: `${formatNumber(data.yearCount)} năm · ${formatNumber(data.typeCount)} loại`,
      icon: Files,
      accent: false,
    },
    {
      label: 'Workbook đã đồng bộ',
      value: formatNumber(data.workbookCount),
      note: 'Nguồn dữ liệu Excel',
      icon: Layers,
      accent: true,
    },
    {
      label: 'Job đã chạy',
      value: formatNumber(data.jobCount),
      note: data.runningJobCount > 0 ? `${formatNumber(data.runningJobCount)} job đang chạy` : 'Không có job đang chạy',
      icon: Cpu,
      accent: false,
    },
    {
      label: 'Lần đồng bộ gần nhất',
      value: data.lastSyncAt ? formatDateTime(data.lastSyncAt) : 'Chưa có',
      note: `${formatNumber(data.syncSessionCount)} phiên đồng bộ`,
      icon: Clock,
      accent: true,
    },
  ]
})

async function load(): Promise<void> {
  loading.value = true
  error.value = null
  try {
    overview.value = await request<OverviewResponse>('/api/overview')
  } catch (caught) {
    error.value = errorMessage(caught)
  } finally {
    loading.value = false
  }
}

onMounted(load)
</script>

<template>
  <div class="stack" style="gap: 20px">
    <section class="hero">
      <div class="hero-content">
        <span class="hero-eyebrow">STP Archive · Quy trình mới</span>
        <h1 class="hero-title">Chuyển đổi, đồng bộ và in mục lục hồ sơ trong một luồng.</h1>
        <p class="hero-text">
          Nhận nhiều workbook cùng cấu trúc, tự sinh sheet in A4, chèn bộ công cụ và macro,
          xuất file <strong>.xlsm</strong> sẵn sàng gửi sang máy có Excel bản quyền.
        </p>
        <div class="hero-actions">
          <RouterLink class="btn btn-primary" to="/chuyen-doi">
            <FileSpreadsheet :size="17" />
            Chuyển đổi Excel
          </RouterLink>
          <RouterLink class="btn btn-ghost" to="/dong-bo">
            <RefreshCw :size="17" />
            Đồng bộ từ Excel
          </RouterLink>
          <RouterLink class="btn btn-ghost" to="/kho">
            <FolderOpen :size="17" />
            Xem kho hồ sơ
          </RouterLink>
        </div>
      </div>

      <div class="hero-aside">
        <div class="hero-aside-label">Môi trường xử lý</div>
        <div class="hero-aside-value">{{ overview?.excelHost ?? 'Đang kiểm tra...' }}</div>
        <div class="hero-aside-meta">
          {{ overview?.excelAvailable ? 'Sẵn sàng chuyển đổi' : 'Không tìm thấy Excel/WPS' }}
        </div>
        <div class="hero-aside-label" style="margin-top: 8px">Cơ sở dữ liệu</div>
        <div class="hero-aside-value">
          {{ overview?.databaseConnected ? 'SQL Server đã kết nối' : 'Chưa kết nối' }}
        </div>
        <div class="hero-aside-meta">Cập nhật {{ formatDateTime(overview?.serverTime) }}</div>
      </div>
    </section>

    <div v-if="error" class="alert alert-danger">
      <TriangleExclamationPoint :size="17" />
      <div class="grow">
        <strong>Không tải được tổng quan</strong>
        <div>{{ error }}</div>
      </div>
      <button class="btn btn-secondary btn-sm" type="button" @click="load">Thử lại</button>
    </div>

    <div v-if="loading" class="stat-grid">
      <div v-for="index in 4" :key="index" class="stat-card">
        <div class="skeleton" style="height: 12px; width: 46%" />
        <div class="skeleton" style="height: 30px; width: 62%; margin-top: 16px" />
        <div class="skeleton" style="height: 10px; width: 50%; margin-top: 12px" />
      </div>
    </div>

    <div v-else-if="overview" class="stat-grid">
      <div v-for="stat in stats" :key="stat.label" class="stat-card" :class="{ accent: stat.accent }">
        <div class="stat-head">
          <span>{{ stat.label }}</span>
          <span class="stat-icon">
            <component :is="stat.icon" :size="17" />
          </span>
        </div>
        <div class="stat-value">{{ stat.value }}</div>
        <div class="stat-note">{{ stat.note }}</div>
      </div>
    </div>

    <div class="grid grid-2">
      <div class="card">
        <div class="card-header">
          <div>
            <div class="card-title">Job gần đây</div>
            <div class="card-subtitle">Chuyển đổi, gộp file và các lần chạy gần nhất</div>
          </div>
          <RouterLink class="btn btn-ghost btn-sm" to="/chuyen-doi">
            Tạo job
            <ArrowRight :size="14" />
          </RouterLink>
        </div>
        <div class="card-body" style="padding-top: 12px">
          <div v-if="!overview || overview.recentJobs.length === 0" class="text-sm muted" style="padding: 10px 0">
            Chưa có job nào. Hãy bắt đầu bằng việc chuyển đổi một workbook.
          </div>
          <div v-else class="stack" style="gap: 0">
            <RouterLink
              v-for="job in overview.recentJobs"
              :key="job.id"
              :to="{ name: 'job', params: { id: job.id } }"
              class="job-item"
            >
              <StatusBadge :value="job.status" />
              <div class="grow" style="min-width: 0">
                <div class="job-title">
                  {{ job.type === 'Merge' ? 'Gộp file' : 'Chuyển đổi' }} · {{ job.totalItems }} file
                </div>
                <div class="job-meta">{{ formatDateTime(job.createdAt) }}</div>
              </div>
              <div class="text-right">
                <div class="text-xs strong">{{ job.succeededItems }}/{{ job.totalItems }}</div>
                <div class="text-xs muted">thành công</div>
              </div>
              <ArrowRight :size="15" class="muted" />
            </RouterLink>
          </div>
        </div>
      </div>

      <div class="card">
        <div class="card-header">
          <div>
            <div class="card-title">Workbook đã đồng bộ</div>
            <div class="card-subtitle">Dữ liệu ML đang làm nguồn chính cho kho hồ sơ</div>
          </div>
          <RouterLink class="btn btn-ghost btn-sm" to="/kho">
            Mở kho
            <ArrowRight :size="14" />
          </RouterLink>
        </div>
        <div class="card-body" style="padding-top: 12px">
          <EmptyState
            v-if="!overview || overview.recentWorkbooks.length === 0"
            title="Chưa có workbook"
            description="Đồng bộ một file Excel ở trang Đồng bộ để đưa dữ liệu vào kho."
          />
          <div v-else class="stack" style="gap: 10px">
            <RouterLink
              v-for="book in overview.recentWorkbooks"
              :key="book.id"
              :to="{ name: 'archive-detail', params: { id: book.id } }"
              class="book-row"
            >
              <span class="file-icon">
                <FileSpreadsheet :size="17" />
              </span>
              <span class="grow" style="min-width: 0">
                <span class="file-name">{{ book.name }}</span>
                <span class="file-meta">
                  {{ formatNumber(book.dossierCount) }} hồ sơ · {{ formatNumber(book.yearCount) }} năm
                </span>
              </span>
              <StatusBadge kind="workbook" :value="book.kind" />
              <ArrowRight :size="15" class="muted" />
            </RouterLink>
          </div>
        </div>
      </div>
    </div>

    <div class="grid grid-3">
      <div class="card">
        <div class="card-body">
          <div class="row">
            <span class="stat-icon"><Database :size="17" /></span>
            <div>
              <div class="text-sm strong">SQL Server</div>
              <div class="text-xs muted">
                {{ overview?.databaseConnected ? 'Kết nối thành công' : 'Chưa kết nối được' }}
              </div>
            </div>
          </div>
        </div>
      </div>
      <div class="card">
        <div class="card-body">
          <div class="row">
            <span class="stat-icon"><Cpu :size="17" /></span>
            <div>
              <div class="text-sm strong">{{ overview?.excelHost ?? 'Excel/WPS' }}</div>
              <div class="text-xs muted truncate" style="max-width: 260px">
                {{ overview?.excelPath ?? 'Chưa phát hiện engine' }}
              </div>
            </div>
          </div>
        </div>
      </div>
      <div class="card">
        <div class="card-body">
          <div class="row">
            <span class="stat-icon"><Calendar :size="17" /></span>
            <div>
              <div class="text-sm strong">Dữ liệu theo năm</div>
              <div class="text-xs muted">
                {{ formatNumber(overview?.yearCount) }} năm · {{ formatNumber(overview?.typeCount) }} loại hồ sơ
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>
