<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { RouterLink, useRoute } from 'vue-router'
import { ArrowLeft, Clock, Printer, TriangleExclamationPoint } from '@lucide/vue'
import { request } from '../api/client'
import type { DossierResponse } from '../api/types'
import { errorMessage } from '../lib/errors'
import {
  numberDisplay,
  printFitClass,
  printTextLength,
  totalPagesText,
} from '../lib/format'
import EmptyState from '../components/EmptyState.vue'

const route = useRoute()

const dossier = ref<DossierResponse | null>(null)
const loading = ref(true)
const error = ref<string | null>(null)

const dossierId = computed(() => String(route.params.id))

const fitClass = computed(() => {
  if (!dossier.value) return ''
  return printFitClass(dossier.value.documents.length, printTextLength(dossier.value.documents))
})

const title = computed(() => dossier.value?.title || 'MỤC LỤC VĂN BẢN')

async function load(): Promise<void> {
  loading.value = true
  error.value = null
  try {
    dossier.value = await request<DossierResponse>(`/api/dossiers/${dossierId.value}`)
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
        <RouterLink
          class="btn btn-sm"
          style="background: rgba(255, 255, 255, 0.1); color: #fff"
          :to="dossier ? { name: 'dossier', params: { id: dossier.id } } : '/'"
        >
          <ArrowLeft :size="14" />
          Quay lại
        </RouterLink>
        <div style="min-width: 0">
          <div class="text-sm strong truncate" style="color: #fff">
            Bản in hồ sơ {{ dossier ? numberDisplay(dossier) : '' }}
          </div>
          <div class="text-xs" style="color: #cbd5e1">
            Mỗi hồ sơ một trang A4 dọc · {{ dossier?.documents.length ?? 0 }} văn bản
          </div>
        </div>
      </div>

      <div class="row wrap">
        <span class="chip no-print-hint">
          <Clock :size="13" />
          Chọn A4 Portrait, tắt Headers and footers
        </span>
        <button class="btn btn-primary" type="button" :disabled="!dossier" @click="printNow">
          <Printer :size="16" />
          In ngay / Lưu PDF
        </button>
      </div>
    </div>
  </div>

  <div class="print-stage">
    <div v-if="error" class="alert alert-danger" style="max-width: 720px; margin: 0 auto 20px">
      <TriangleExclamationPoint :size="17" />
      <div class="grow">
        <strong>Không tải được hồ sơ</strong>
        <div>{{ error }}</div>
      </div>
      <button class="btn btn-secondary btn-sm" type="button" @click="load">Thử lại</button>
    </div>

    <div v-if="loading" class="loading-block" style="color: #334155">
      <span class="spinner lg" /> Đang dựng bản in...
    </div>

    <EmptyState
      v-else-if="!dossier"
      title="Không tìm thấy hồ sơ"
      description="Không thể dựng bản in vì hồ sơ không tồn tại."
    />

    <template v-else>
      <div v-if="!dossier.hasPrintLayout" class="alert alert-warning no-print" style="max-width: 210mm; margin: 0 auto 16px">
        <TriangleExclamationPoint :size="17" />
        <div>
          Hồ sơ này chưa có đủ dữ liệu in (thiếu số hồ sơ hoặc văn bản). Bản in vẫn được hiển thị để kiểm tra.
        </div>
      </div>

      <section class="sheet-page" :class="fitClass">
        <h1 class="sheet-title">{{ title }}</h1>
        <div class="sheet-subtitle">Hồ sơ số: {{ numberDisplay(dossier) }}</div>

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
            <tr v-if="dossier.documents.length === 0" class="doc-row">
              <td class="cell-center">1</td>
              <td></td>
              <td></td>
              <td></td>
              <td></td>
              <td></td>
              <td></td>
            </tr>
            <tr v-for="line in dossier.documents" :key="line.stt" class="doc-row">
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
              <td colspan="7" class="sheet-total">{{ totalPagesText(dossier.totalPages) }}</td>
            </tr>
          </tfoot>
        </table>
      </section>
    </template>
  </div>
</template>
