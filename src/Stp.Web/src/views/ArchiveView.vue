<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { RouterLink } from 'vue-router'
import { ArrowRight, FileSpreadsheet, FolderOpen, Radio, RefreshCw, Search } from '@lucide/vue'
import { request } from '../api/client'
import type { SummaryActiveSessionResponse, WorkbookResponse } from '../api/types'
import { errorMessage } from '../lib/errors'
import { formatDateTime, formatNumber } from '../lib/format'
import EmptyState from '../components/EmptyState.vue'
import StatusBadge from '../components/StatusBadge.vue'

const workbooks = ref<WorkbookResponse[]>([])
const loading = ref(true)
const error = ref<string | null>(null)
const search = ref('')
const activeTeamSession = ref<SummaryActiveSessionResponse | null>(null)

const filtered = computed(() => {
  const term = search.value.trim().toLowerCase()
  if (!term) return workbooks.value
  return workbooks.value.filter(
    (book) =>
      book.name.toLowerCase().includes(term) ||
      book.sourceFileName.toLowerCase().includes(term),
  )
})

const totalDossiers = computed(() =>
  workbooks.value.reduce((total, book) => total + book.dossierCount, 0),
)

async function load(): Promise<void> {
  loading.value = true
  error.value = null
  try {
    const [books, active] = await Promise.all([
      request<WorkbookResponse[]>('/api/workbooks'),
      request<SummaryActiveSessionResponse>('/api/summary/active').catch(() => null),
    ])
    workbooks.value = books
    activeTeamSession.value = active && active.hasActiveSession ? active : null
  } catch (caught) {
    error.value = errorMessage(caught)
  } finally {
    loading.value = false
  }
}

onMounted(load)
</script>

<template>
  <div class="stack" style="gap: 18px">
    <div v-if="activeTeamSession && activeTeamSession.hasActiveSession" class="team-session-alert">
      <Radio :size="16" class="live-icon" />
      <div class="grow">
        <strong>Phiên trích yếu đang chạy chung:</strong> File <em>{{ activeTeamSession.fileName }}</em> (do {{ activeTeamSession.uploadedBy }} tải lên) đang được cả phòng làm trực tiếp như Google Sheets.
      </div>
      <RouterLink class="btn btn-primary btn-sm" to="/trich-yeu">
        Vào làm cùng anh em
        <ArrowRight :size="14" />
      </RouterLink>
    </div>

    <div class="row-between wrap">
      <div>
        <h1 class="page-title" style="font-size: 22px">Kho hồ sơ</h1>
        <p class="page-subtitle">
          {{ formatNumber(workbooks.length) }} workbook · {{ formatNumber(totalDossiers) }} hồ sơ đang lưu trong SQL Server.
        </p>
      </div>
      <div class="row wrap">
        <div class="search-box">
          <Search :size="15" />
          <input v-model="search" class="input" placeholder="Tìm workbook..." />
        </div>
        <button class="btn btn-secondary btn-sm" type="button" :disabled="loading" @click="load">
          <RefreshCw :size="14" />
          Làm mới
        </button>
        <RouterLink class="btn btn-primary btn-sm" to="/dong-bo">
          <RefreshCw :size="14" />
          Đồng bộ Excel
        </RouterLink>
      </div>
    </div>

    <div v-if="error" class="alert alert-danger">
      <div class="grow">
        <strong>Không tải được kho hồ sơ</strong>
        <div>{{ error }}</div>
      </div>
      <button class="btn btn-secondary btn-sm" type="button" @click="load">Thử lại</button>
    </div>

    <div v-if="loading" class="grid grid-2">
      <div v-for="index in 4" :key="index" class="card">
        <div class="card-body">
          <div class="skeleton" style="height: 16px; width: 55%" />
          <div class="skeleton" style="height: 11px; width: 40%; margin-top: 12px" />
          <div class="skeleton" style="height: 42px; margin-top: 22px" />
        </div>
      </div>
    </div>

    <div v-else-if="filtered.length === 0" class="card">
      <EmptyState
        title="Chưa có workbook nào"
        description="Đồng bộ file Excel gốc để đưa dữ liệu ML vào kho hồ sơ."
      >
        <template #icon>
          <FolderOpen :size="26" />
        </template>
        <template #action>
          <RouterLink class="btn btn-primary" to="/dong-bo">
            <RefreshCw :size="16" />
            Đồng bộ ngay
          </RouterLink>
        </template>
      </EmptyState>
    </div>

    <div v-else class="grid grid-2">
      <RouterLink
        v-for="book in filtered"
        :key="book.id"
        :to="{ name: 'archive-detail', params: { id: book.id } }"
        class="card workbook-card"
      >
        <div class="card-body">
          <div class="row-between wrap">
            <div class="row">
              <span class="file-icon" style="width: 42px; height: 42px">
                <FileSpreadsheet :size="20" />
              </span>
              <div style="min-width: 0">
                <div class="card-title truncate">{{ book.name }}</div>
                <div class="text-xs muted truncate">{{ book.sourceFileName }}</div>
              </div>
            </div>
            <StatusBadge kind="workbook" :value="book.kind" />
          </div>

          <div class="metric-grid" style="margin-top: 16px">
            <div class="metric">
              <div class="metric-label">Hồ sơ</div>
              <div class="metric-value">{{ formatNumber(book.dossierCount) }}</div>
            </div>
            <div class="metric">
              <div class="metric-label">Năm</div>
              <div class="metric-value">{{ formatNumber(book.yearCount) }}</div>
            </div>
            <div class="metric">
              <div class="metric-label">Phiên bản</div>
              <div class="metric-value">v{{ book.version }}</div>
            </div>
          </div>

          <div class="row-between wrap" style="margin-top: 14px">
            <div class="text-xs muted">
              <div>Đồng bộ: {{ formatDateTime(book.lastSyncedAt) }}</div>
              <div>Nhập: {{ formatDateTime(book.importedAt) }}</div>
            </div>
            <span class="row text-sm strong" style="color: var(--brand-text)">
              Mở kho
              <ArrowRight :size="15" />
            </span>
          </div>
        </div>
      </RouterLink>
    </div>
  </div>
</template>
