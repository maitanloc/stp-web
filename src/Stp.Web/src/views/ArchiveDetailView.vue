<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { RouterLink, useRoute, useRouter } from 'vue-router'
import {
  ArrowLeft,
  ChevronLeft,
  ChevronRight,
  FileSpreadsheet,
  Printer,
  Search,
  SlidersHorizontal,
  TriangleExclamationPoint,
} from '@lucide/vue'
import { queryString, request } from '../api/client'
import type {
  DossierSummaryResponse,
  PagedResponse,
  WorkbookResponse,
  YearResponse,
} from '../api/types'
import { errorMessage } from '../lib/errors'
import { formatDateTime, formatNumber, numberDisplay } from '../lib/format'
import AppSelect from '../components/AppSelect.vue'
import EmptyState from '../components/EmptyState.vue'
import StatusBadge from '../components/StatusBadge.vue'

const route = useRoute()
const router = useRouter()

const bookId = computed(() => String(route.params.id))
const book = ref<WorkbookResponse | null>(null)
const years = ref<YearResponse[]>([])
const types = ref<string[]>([])
const metaLoading = ref(true)
const metaError = ref<string | null>(null)

const dossiers = ref<DossierSummaryResponse[]>([])
const total = ref(0)
const page = ref(1)
const pageSize = 50
const listLoading = ref(false)
const listError = ref<string | null>(null)

const yearFilter = ref('')
const typeFilter = ref('')
const search = ref('')

const yearOptions = computed(() => [
  { value: '', label: 'Tất cả các năm' },
  ...years.value.map((year) => ({
    value: String(year.year),
    label: String(year.year),
    countLabel: formatNumber(year.dossierCount),
  })),
])

const typeOptions = computed(() => [
  { value: '', label: 'Tất cả loại hồ sơ' },
  ...types.value.map((type) => ({ value: type, label: type })),
])

const totalPages = computed(() => Math.max(1, Math.ceil(total.value / pageSize)))
const pageNumbers = computed(() => {
  const last = totalPages.value
  const start = Math.max(1, Math.min(page.value - 2, last - 4))
  const end = Math.min(last, start + 4)
  const result: number[] = []
  for (let current = start; current <= end; current += 1) result.push(current)
  return result
})

async function loadMeta(): Promise<void> {
  metaLoading.value = true
  metaError.value = null
  try {
    const books = await request<WorkbookResponse[]>('/api/workbooks')
    book.value = books.find((item) => item.id === bookId.value) ?? null

    if (book.value) {
      const [yearResult, typeResult] = await Promise.all([
        request<YearResponse[]>(`/api/workbooks/${bookId.value}/years`),
        request<string[]>(`/api/workbooks/${bookId.value}/types`),
      ])
      years.value = yearResult
      types.value = typeResult
    }
  } catch (caught) {
    metaError.value = errorMessage(caught)
  } finally {
    metaLoading.value = false
  }
}

async function loadDossiers(): Promise<void> {
  listLoading.value = true
  listError.value = null
  try {
    const query = queryString({
      year: yearFilter.value ? Number(yearFilter.value) : undefined,
      type: typeFilter.value || undefined,
      search: search.value.trim() || undefined,
      page: page.value,
      pageSize,
    })
    const response = await request<PagedResponse<DossierSummaryResponse>>(
      `/api/workbooks/${bookId.value}/dossiers${query}`,
    )
    dossiers.value = response.items
    total.value = response.total
  } catch (caught) {
    listError.value = errorMessage(caught)
    dossiers.value = []
    total.value = 0
  } finally {
    listLoading.value = false
  }
}

function applyFilters(): void {
  page.value = 1
  void loadDossiers()
}

function goTo(nextPage: number): void {
  if (nextPage < 1 || nextPage > totalPages.value) return
  page.value = nextPage
  void loadDossiers()
}

function openDossier(id: string): void {
  void router.push({ name: 'dossier', params: { id } })
}

function resetFilters(): void {
  yearFilter.value = ''
  typeFilter.value = ''
  search.value = ''
  applyFilters()
}

async function reload(): Promise<void> {
  await loadMeta()
  await loadDossiers()
}

watch(bookId, () => {
  yearFilter.value = ''
  typeFilter.value = ''
  search.value = ''
  page.value = 1
  void reload()
})

onMounted(reload)
</script>

<template>
  <div class="stack" style="gap: 18px">
    <div class="row-between wrap">
      <div class="row wrap">
        <RouterLink class="btn btn-ghost btn-sm" to="/kho">
          <ArrowLeft :size="14" />
          Kho hồ sơ
        </RouterLink>
        <span v-if="book" class="row">
          <span class="file-icon" style="width: 36px; height: 36px">
            <FileSpreadsheet :size="17" />
          </span>
          <span>
            <span class="text-sm strong" style="display: block">{{ book.name }}</span>
            <span class="text-xs muted">{{ book.sourceFileName }}</span>
          </span>
          <StatusBadge kind="workbook" :value="book.kind" />
        </span>
      </div>
      <RouterLink class="btn btn-secondary btn-sm" to="/dong-bo">
        Đồng bộ lại
      </RouterLink>
    </div>

    <div v-if="metaError" class="alert alert-danger">
      <TriangleExclamationPoint :size="17" />
      <div class="grow">{{ metaError }}</div>
    </div>

    <div v-if="metaLoading" class="loading-block"><span class="spinner lg" /> Đang tải thông tin workbook...</div>

    <div v-else-if="!book" class="card">
      <EmptyState
        title="Không tìm thấy workbook"
        description="Workbook có thể đã bị xoá khỏi cơ sở dữ liệu."
      >
        <template #action>
          <RouterLink class="btn btn-primary" to="/kho">Quay lại kho hồ sơ</RouterLink>
        </template>
      </EmptyState>
    </div>

    <template v-else>
      <div class="metric-grid">
        <div class="metric">
          <div class="metric-label">Tổng hồ sơ</div>
          <div class="metric-value">{{ formatNumber(book.dossierCount) }}</div>
        </div>
        <div class="metric">
          <div class="metric-label">Số năm</div>
          <div class="metric-value">{{ formatNumber(book.yearCount) }}</div>
        </div>
        <div class="metric">
          <div class="metric-label">Phiên bản</div>
          <div class="metric-value">v{{ book.version }}</div>
        </div>
        <div class="metric">
          <div class="metric-label">Đồng bộ gần nhất</div>
          <div class="metric-value" style="font-size: 16px">{{ formatDateTime(book.lastSyncedAt) }}</div>
        </div>
      </div>

      <div class="card">
        <div class="card-header">
          <div>
            <div class="card-title">Danh sách hồ sơ</div>
            <div class="card-subtitle">
              {{ formatNumber(total) }} hồ sơ khớp bộ lọc · trang {{ page }}/{{ totalPages }}
            </div>
          </div>
          <SlidersHorizontal :size="18" class="muted" />
        </div>
        <div class="card-body">
          <form class="filter-bar" @submit.prevent="applyFilters">
            <div class="search-box grow">
              <Search :size="15" />
              <input
                v-model="search"
                class="input"
                placeholder="Tìm theo số hồ sơ, trích yếu, hộp, nội dung văn bản..."
              />
            </div>
            <div style="width: 170px">
              <AppSelect
                v-model="yearFilter"
                :options="yearOptions"
                aria-label="Lọc theo năm"
                @change="applyFilters"
              />
            </div>
            <div style="width: 210px">
              <AppSelect
                v-model="typeFilter"
                :options="typeOptions"
                aria-label="Lọc theo loại hồ sơ"
                @change="applyFilters"
              />
            </div>
            <button class="btn btn-primary" type="submit">
              <Search :size="15" />
              Tìm
            </button>
            <button class="btn btn-ghost" type="button" @click="resetFilters">Xoá lọc</button>
          </form>

          <div v-if="listError" class="alert alert-danger" style="margin-top: 12px">
            <TriangleExclamationPoint :size="17" />
            <div class="grow">{{ listError }}</div>
            <button class="btn btn-secondary btn-sm" type="button" @click="loadDossiers">Thử lại</button>
          </div>

          <div v-if="listLoading" class="loading-block"><span class="spinner lg" /> Đang tải hồ sơ...</div>

          <EmptyState
            v-else-if="dossiers.length === 0"
            title="Không có hồ sơ phù hợp"
            description="Thử đổi năm, loại hồ sơ hoặc từ khoá tìm kiếm."
          />

          <div v-else class="table-wrap" style="margin-top: 14px">
            <table class="table table-hover">
              <thead>
                <tr>
                  <th>Số hồ sơ</th>
                  <th>Năm</th>
                  <th>Loại</th>
                  <th>Nhóm</th>
                  <th>Hộp</th>
                  <th>Trích yếu</th>
                  <th class="num">Văn bản</th>
                  <th class="num">Tờ</th>
                  <th></th>
                </tr>
              </thead>
              <tbody>
                <tr
                  v-for="dossier in dossiers"
                  :key="dossier.id"
                  class="clickable"
                  @click="openDossier(dossier.id)"
                >
                  <td>
                    <span class="strong">{{ numberDisplay(dossier) }}</span>
                    <span class="text-xs muted" style="display: block">ML {{ dossier.mlRow }}</span>
                  </td>
                  <td>{{ dossier.year }}</td>
                  <td>
                    <span class="badge badge-brand">{{ dossier.type || 'Chưa phân loại' }}</span>
                  </td>
                  <td class="text-sm">{{ dossier.group || '—' }}</td>
                  <td class="text-sm">{{ dossier.boxNumber || '—' }}</td>
                  <td class="text-sm" style="max-width: 380px">{{ dossier.originalSummary || '—' }}</td>
                  <td class="num">{{ formatNumber(dossier.documentCount) }}</td>
                  <td class="num">{{ dossier.totalPages > 0 ? formatNumber(dossier.totalPages) : '—' }}</td>
                  <td class="text-right">
                    <span
                      v-if="dossier.hasPrintLayout"
                      class="badge badge-success"
                      title="Có dữ liệu in"
                    >
                      <Printer :size="12" />
                    </span>
                    <ChevronRight v-else :size="15" class="muted" />
                  </td>
                </tr>
              </tbody>
            </table>
          </div>

          <div v-if="!listLoading && totalPages > 1" class="pagination" style="margin-top: 16px">
            <div class="text-xs muted">
              Hiển thị {{ formatNumber((page - 1) * pageSize + 1) }}–{{ formatNumber(Math.min(page * pageSize, total)) }}
              trong {{ formatNumber(total) }} hồ sơ
            </div>
            <div class="pagination-pages">
              <button class="page-btn" type="button" :disabled="page <= 1" @click="goTo(page - 1)">
                <ChevronLeft :size="14" />
              </button>
              <button
                v-for="number in pageNumbers"
                :key="number"
                class="page-btn"
                :class="{ active: number === page }"
                type="button"
                @click="goTo(number)"
              >
                {{ number }}
              </button>
              <button class="page-btn" type="button" :disabled="page >= totalPages" @click="goTo(page + 1)">
                <ChevronRight :size="14" />
              </button>
            </div>
          </div>
        </div>
      </div>
    </template>
  </div>
</template>
