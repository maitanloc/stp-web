<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { RouterLink, useRoute } from 'vue-router'
import { ArrowLeft, Clock, Printer, TriangleExclamationPoint } from '@lucide/vue'
import { queryString, request } from '../api/client'
import type { DossierResponse, DossierSummaryResponse, PagedResponse } from '../api/types'
import { errorMessage } from '../lib/errors'
import {
  numberDisplay,
  printFitClass,
  printTextLength,
  totalPagesText,
} from '../lib/format'
import EmptyState from '../components/EmptyState.vue'

const MAX_BATCH = 300

const route = useRoute()

const workbookId = computed(() => String(route.query.workbook ?? ''))
const year = computed(() => (route.query.year ? Number(route.query.year) : undefined))
const type = computed(() => (route.query.type ? String(route.query.type) : undefined))

const dossiers = ref<Array<DossierResponse | null>>([])
const batteryTotal = ref(0)
const loading = ref(true)
const error = ref<string | null>(null)
const truncated = ref(false)

const title = computed(() => 'MỤC LỤC VĂN BẢN')

function fitClassOf(dossier: DossierResponse | null): string {
  if (!dossier) return ''
  return printFitClass(dossier.documents.length, printTextLength(dossier.documents))
}

async function load(): Promise<void> {
  loading.value = true
  error.value = null
  dossiers.value = []
  truncated.value = false

  try {
    const params: Record<string, string | number | undefined> = {
      year: year.value,
      type: type.value,
      pageSize: 200,
    }

    let currentPage = 1
    let totalPages = 1
    const ids: DossierSummaryResponse[] = []

    do {
      const page = await request<PagedResponse<DossierSummaryResponse>>(
        `/api/workbooks/${workbookId.value}/dossiers${queryString({ ...params, page: currentPage })}`,
      )
      ids.push(...page.items)
      totalPages = Math.max(1, Math.ceil(page.total / page.pageSize))
      batteryTotal.value = page.total
      currentPage++
    } while (currentPage <= totalPages && ids.length < MAX_BATCH + 1)

    if (ids.length > MAX_BATCH) {
      truncated.value = true
      ids.splice(MAX_BATCH)
    }

    const details: Array<DossierResponse | null> = []
    for (const item of ids) {
      try {
        const detail = await request<DossierResponse>(`/api/dossiers/${item.id}`)
        details.push(detail)
      } catch {
        details.push(null)
      }
    }

    dossiers.value = details
  } catch (caught) {
    error.value = errorMessage(caught)
  } finally {
    loading.value = false
  }
}

function printNow(): void {
  window.print()
}

onMounted(load)
</script>

<template>
  <div class="print-toolbar no-print">
    <div class="print-toolbar-inner">
      <div class="row wrap" style="min-width: 0">
        <RouterLink class="btn btn-sm" style="background: rgba(255, 255, 255, 0.1); color: #fff" to="/kho">
          <ArrowLeft :size="14" />
          Quay lại
        </RouterLink>
        <div style="min-width: 0">
          <div class="text-sm strong truncate" style="color: #fff">
            In hàng loạt · {{ batteryTotal }} hồ sơ trong phạm vi
            <template v-if="year"> (năm {{ year }})</template>
            <template v-if="type"> ({{ type }})</template>
          </div>
          <div class="text-xs" style="color: #cbd5e1">
            Mỗi hồ sơ một trang A4 dọc · in tối đa {{ MAX_BATCH }} hồ sơ mỗi lần
          </div>
        </div>
      </div>

      <div class="row wrap">
        <span class="chip no-print-hint">
          <Clock :size="13" />
          Chọn A4 Portrait, tắt Headers and footers
        </span>
        <button class="btn btn-primary" type="button" :disabled="loading || dossiers.length === 0" @click="printNow">
          <Printer :size="16" />
          In / Lưu PDF
        </button>
      </div>
    </div>
  </div>

  <div class="print-stage">
    <div v-if="error" class="alert alert-danger" style="max-width: 720px; margin: 0 auto 20px">
      <TriangleExclamationPoint :size="17" />
      <div class="grow">
        <strong>Không tải được danh sách in</strong>
        <div>{{ error }}</div>
      </div>
      <button class="btn btn-secondary btn-sm" type="button" @click="load">Thử lại</button>
    </div>

    <div v-if="truncated" class="alert alert-warning no-print" style="max-width: 720px; margin: 0 auto 16px">
      <TriangleExclamationPoint :size="17" />
      <div class="grow">
        Phạm vi có {{ batteryTotal }} hồ sơ nhưng chỉ in {{ MAX_BATCH }} hồ sơ đầu tiên.
        Hãy nới/lọc năm hoặc loại để in phần còn lại.
      </div>
    </div>

    <div v-if="loading" class="loading-block" style="color: #334155">
      <span class="spinner lg" /> Đang dựng bản in...
    </div>

    <EmptyState
      v-else-if="dossiers.length === 0"
      title="Không có hồ sơ nào để in"
      description="Thử nới điều kiện năm/loại hoặc quay lại kho hồ sơ."
    />

    <template v-else>
      <section
        v-for="(dossier, index) in dossiers"
        :key="dossier?.id ?? `missing-${index}`"
        class="sheet-page"
        :class="fitClassOf(dossier)"
      >
        <h1 class="sheet-title">{{ title }}</h1>
        <div class="sheet-subtitle">Hồ sơ số: {{ dossier ? numberDisplay(dossier) : '—' }}</div>

        <table class="doc-table">
          <colgroup>
            <col style="width: 6%" />
            <col style="width: 13%" />
            <col style="width: 12%" />
            <col style="width: 40%" />
            <col style="width: 16%" />
            <col style="width: 8%" />
            <col style="width: 5%" />
          </colgroup>
          <thead>
            <tr>
              <th>STT</th>
              <th>Số ký hiệu văn bản</th>
              <th>Ngày, tháng, văn bản</th>
              <th>Trích yếu nội dung văn bản</th>
              <th>Tác giả văn bản</th>
              <th>Tờ số</th>
              <th>Ghi chú</th>
            </tr>
          </thead>
          <tbody>
            <tr v-if="!dossier || dossier.documents.length === 0" class="doc-row">
              <td class="cell-center">1</td>
              <td></td>
              <td></td>
              <td></td>
              <td></td>
              <td></td>
              <td></td>
            </tr>
            <tr v-for="line in dossier?.documents ?? []" :key="line.stt" class="doc-row">
              <td class="cell-center">{{ line.stt }}</td>
              <td class="cell-wrap">{{ line.soKyHieu }}</td>
              <td class="cell-center cell-wrap">{{ line.ngay }}</td>
              <td class="cell-wrap">{{ line.trichYeu }}</td>
              <td class="cell-wrap">{{ line.tacGia }}</td>
              <td class="cell-number">{{ line.toSo }}</td>
              <td class="cell-wrap">{{ line.ghiChu }}</td>
            </tr>
          </tbody>
          <tfoot>
            <tr>
              <td colspan="7" class="sheet-total">
                {{ dossier ? totalPagesText(dossier.totalPages) : '' }}
              </td>
            </tr>
          </tfoot>
        </table>
      </section>
    </template>
  </div>
</template>
