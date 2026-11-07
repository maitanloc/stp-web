<script setup lang="ts">
import { computed } from 'vue'
import { CircleCheck, CircleX, Info, TriangleExclamationPoint } from '@lucide/vue'
import type { InspectionResponse } from '../api/types'
import { formatBytes, formatNumber } from '../lib/format'
import StatusBadge from './StatusBadge.vue'

const props = defineProps<{
  inspection: InspectionResponse
}>()

const metrics = computed(() => [
  { label: 'Dung lượng', value: formatBytes(props.inspection.fileSizeBytes) },
  { label: 'Số sheet', value: formatNumber(props.inspection.sheetCount) },
  { label: 'Số năm', value: formatNumber(props.inspection.yearCount) },
  { label: 'Hồ sơ', value: formatNumber(props.inspection.dossierCount) },
  { label: 'Sẵn sàng in', value: formatNumber(props.inspection.printReadyCount) },
  { label: 'Tổng số tờ', value: formatNumber(props.inspection.totalPages) },
])

const hasWarnings = computed(() => props.inspection.warnings.length > 0)
</script>

<template>
  <div class="stack" style="gap: 14px">
    <div class="row-between wrap">
      <div class="row wrap">
        <StatusBadge kind="workbook" :value="inspection.kind" />
        <span class="chip">
          <Info :size="14" />
          {{ inspection.mlSheetName ? `Sheet ML: ${inspection.mlSheetName}` : 'Không thấy sheet ML' }}
        </span>
        <span class="chip">
          <component
            :is="inspection.hasMacro ? CircleCheck : CircleX"
            :size="14"
            :style="{ color: inspection.hasMacro ? 'var(--success)' : 'var(--muted)' }"
          />
          {{ inspection.hasMacro ? 'Đã có macro' : 'Chưa có macro' }}
        </span>
        <span class="chip">
          <component
            :is="inspection.hasPrintSheets ? CircleCheck : CircleX"
            :size="14"
            :style="{ color: inspection.hasPrintSheets ? 'var(--success)' : 'var(--warning)' }"
          />
          {{ inspection.hasPrintSheets ? 'Có sheet in' : 'Chưa có sheet in' }}
        </span>
      </div>
      <span class="text-xs muted truncate" style="max-width: 280px">{{ inspection.fileName }}</span>
    </div>

    <div class="metric-grid">
      <div v-for="metric in metrics" :key="metric.label" class="metric">
        <div class="metric-label">{{ metric.label }}</div>
        <div class="metric-value">{{ metric.value }}</div>
      </div>
    </div>

    <div v-if="inspection.types.length > 0" class="row wrap">
      <span class="text-xs muted strong">Loại hồ sơ:</span>
      <span v-for="type in inspection.types" :key="type" class="badge badge-brand">{{ type }}</span>
    </div>

    <div v-if="hasWarnings" class="alert alert-warning">
      <TriangleExclamationPoint :size="17" />
      <div>
        <strong>Cảnh báo khi đọc file</strong>
        <ul style="margin-top: 4px">
          <li v-for="warning in inspection.warnings" :key="warning">{{ warning }}</li>
        </ul>
      </div>
    </div>

    <details v-if="inspection.years.length > 0" class="details-block">
      <summary>Chi tiết theo năm ({{ inspection.years.length }} năm)</summary>
      <div class="table-wrap" style="margin-top: 10px">
        <table class="table">
          <thead>
            <tr>
              <th>Năm</th>
              <th>Sheet</th>
              <th class="num">Hồ sơ</th>
              <th class="num">Số nhỏ nhất</th>
              <th class="num">Số lớn nhất</th>
              <th>Sheet in</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="year in inspection.years" :key="year.year">
              <td class="strong">{{ year.year }}</td>
              <td class="mono text-sm">{{ year.sheetName }}</td>
              <td class="num">{{ formatNumber(year.dossierCount) }}</td>
              <td class="num">{{ formatNumber(year.minNumber) }}</td>
              <td class="num">{{ formatNumber(year.maxNumber) }}</td>
              <td>
                <span class="badge" :class="year.hasPrintSheet ? 'badge-success' : 'badge-warning'">
                  {{ year.hasPrintSheet ? 'Có' : 'Tự sinh' }}
                </span>
              </td>
            </tr>
          </tbody>
        </table>
      </div>
    </details>

    <details class="details-block">
      <summary>Danh sách sheet ({{ inspection.sheets.length }} sheet)</summary>
      <div class="table-wrap" style="margin-top: 10px">
        <table class="table">
          <thead>
            <tr>
              <th>Sheet</th>
              <th>Vai trò</th>
              <th class="num">Dòng cuối</th>
              <th class="num">Cột cuối</th>
              <th class="num">Khối in</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="sheet in inspection.sheets" :key="sheet.name">
              <td class="mono text-sm">{{ sheet.name }}</td>
              <td>
                <span v-if="sheet.isMl" class="badge badge-accent">ML</span>
                <span v-else-if="sheet.isPrint" class="badge badge-success">Sheet in</span>
                <span v-else class="badge badge-neutral">Khác</span>
              </td>
              <td class="num">{{ formatNumber(sheet.lastRow) }}</td>
              <td class="num">{{ formatNumber(sheet.lastColumn) }}</td>
              <td class="num">{{ formatNumber(sheet.blockCount) }}</td>
            </tr>
          </tbody>
        </table>
      </div>
    </details>
  </div>
</template>
