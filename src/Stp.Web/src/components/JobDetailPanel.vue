<script setup lang="ts">
import { computed } from 'vue'
import { Download, RefreshCw, X } from '@lucide/vue'
import type { JobDetailResponse } from '../api/types'
import { fileUrl } from '../api/client'
import {
  formatDateTime,
  formatNumber,
  jobTypeLabel,
  originalModeLabel,
  percent,
} from '../lib/format'
import StatusBadge from './StatusBadge.vue'

const props = defineProps<{
  detail: JobDetailResponse | null
  loading?: boolean
}>()

const emit = defineEmits<{
  (event: 'cancel'): void
  (event: 'refresh'): void
}>()

const summary = computed(() => props.detail?.summary ?? null)

const isActive = computed(
  () => summary.value?.status === 'Queued' || summary.value?.status === 'Running',
)

const progress = computed(() => {
  const current = summary.value
  if (!current) return 0
  return percent(current.succeededItems + current.failedItems, current.totalItems)
})

const canCancel = computed(
  () => summary.value !== null && (summary.value.status === 'Queued' || summary.value.status === 'Running'),
)

const jobId = computed(() => summary.value?.id ?? '')
</script>

<template>
  <div v-if="loading && !detail" class="loading-block">
    <span class="spinner lg" />
    Đang tải thông tin job...
  </div>

  <div v-else-if="detail && summary" class="stack">
    <div class="card">
      <div class="card-body">
        <div class="row-between wrap">
          <div class="row wrap">
            <StatusBadge :value="summary.status" />
            <StatusBadge kind="jobType" :value="summary.type" />
            <span class="text-xs muted">Tạo lúc {{ formatDateTime(summary.createdAt) }}</span>
          </div>
          <div class="row">
            <button class="btn btn-secondary btn-sm" type="button" :disabled="loading" @click="emit('refresh')">
              <RefreshCw :size="14" :class="{ spin: loading }" />
              Làm mới
            </button>
            <button
              v-if="canCancel"
              class="btn btn-danger btn-sm"
              type="button"
              @click="emit('cancel')"
            >
              <X :size="14" />
              Huỷ job
            </button>
            <a
              v-if="summary.canDownload"
              class="btn btn-primary btn-sm"
              :href="fileUrl(`/api/jobs/${jobId}/download`)"
            >
              <Download :size="14" />
              Tải kết quả
            </a>
          </div>
        </div>

        <div class="row-between" style="margin-top: 18px">
          <div class="text-sm strong">
            Tiến độ: {{ summary.succeededItems + summary.failedItems }} / {{ summary.totalItems }} file
          </div>
          <div class="text-sm muted">
            Thành công {{ formatNumber(summary.succeededItems) }} · Lỗi {{ formatNumber(summary.failedItems) }}
          </div>
        </div>
        <div class="progress" style="margin-top: 8px">
          <div
            class="progress-bar"
            :class="{ indeterminate: isActive && progress === 0 }"
            :style="{ width: `${progress}%` }"
          />
        </div>

        <div v-if="summary.errorMessage" class="alert alert-danger" style="margin-top: 14px">
          <X :size="17" />
          <div>
            <strong>Job gặp lỗi</strong>
            <div>{{ summary.errorMessage }}</div>
          </div>
        </div>

        <div class="row wrap" style="margin-top: 14px">
          <span class="chip">Bắt đầu: {{ formatDateTime(summary.startedAt) }}</span>
          <span class="chip">Kết thúc: {{ formatDateTime(summary.completedAt) }}</span>
          <span v-if="summary.outputFileName" class="chip">Kết quả: {{ summary.outputFileName }}</span>
        </div>
      </div>
    </div>

    <div class="card">
      <div class="card-header">
        <div>
          <div class="card-title">Tuỳ chọn xử lý</div>
          <div class="card-subtitle">Thiết lập đã dùng cho job này</div>
        </div>
      </div>
      <div class="card-body">
        <div class="row wrap">
          <span class="chip">{{ originalModeLabel[detail.options.originalFileMode] ?? detail.options.originalFileMode }}</span>
          <span v-if="detail.options.createZip" class="chip">Đóng gói ZIP</span>
          <span v-if="detail.options.mergeOutput" class="chip">Tạo workbook gộp</span>
          <span v-if="detail.options.generatePrintSheets" class="chip">Tự sinh sheet in</span>
          <span v-if="detail.options.injectMacro" class="chip">Chèn macro</span>
          <span v-if="detail.options.headerText" class="chip">Header: {{ detail.options.headerText }}</span>
          <span v-if="detail.options.footerText" class="chip">Footer: {{ detail.options.footerText }}</span>
        </div>
      </div>
    </div>

    <div class="card">
      <div class="card-header">
        <div>
          <div class="card-title">Từng file</div>
          <div class="card-subtitle">{{ detail.items.length }} file trong job</div>
        </div>
      </div>
      <div class="card-body" style="padding-top: 12px">
        <div class="table-wrap">
          <table class="table">
            <thead>
              <tr>
                <th>File</th>
                <th>Loại</th>
                <th class="num">Hồ sơ</th>
                <th>Trạng thái</th>
                <th>Hoàn tất</th>
                <th></th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="item in detail.items" :key="item.id">
                <td>
                  <div class="strong text-sm">{{ item.inputFileName }}</div>
                  <div v-if="item.errorMessage" class="error-text">{{ item.errorMessage }}</div>
                </td>
                <td><StatusBadge kind="workbook" :value="item.workbookKind" /></td>
                <td class="num">{{ formatNumber(item.dossierCount) }}</td>
                <td><StatusBadge :value="item.status" /></td>
                <td class="text-xs muted">{{ formatDateTime(item.completedAt) }}</td>
                <td class="text-right">
                  <a
                    v-if="item.canDownload"
                    class="btn btn-secondary btn-sm"
                    :href="fileUrl(`/api/jobs/${jobId}/files/${item.id}`)"
                  >
                    <Download :size="14" />
                    Tải
                  </a>
                </td>
              </tr>
            </tbody>
          </table>
        </div>
      </div>
    </div>

    <div class="card">
      <div class="card-header">
        <div>
          <div class="card-title">Nhật ký xử lý</div>
          <div class="card-subtitle">{{ detail.logs.length }} dòng log</div>
        </div>
      </div>
      <div class="card-body" style="padding-top: 12px">
        <div v-if="detail.logs.length === 0" class="text-sm muted">Chưa có log.</div>
        <div v-else class="log-view">
          <div v-for="(log, index) in detail.logs" :key="index" class="log-line">
            <span class="log-time">{{ formatDateTime(log.timestamp) }}</span>
            <span class="log-level" :class="log.level">{{ log.level }}</span>
            <span class="grow">{{ log.message }}</span>
          </div>
        </div>
      </div>
    </div>

    <div class="text-xs muted">
      Job {{ jobTypeLabel[summary.type as keyof typeof jobTypeLabel] ?? summary.type }} · mã {{ summary.id }}
    </div>
  </div>

  <div v-else class="empty-state">
    <div class="empty-title">Không tìm thấy job</div>
  </div>
</template>
