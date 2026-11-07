<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { RouterLink, useRoute } from 'vue-router'
import {
  ArrowLeft,
  BookOpen,
  FileText,
  Printer,
  StickyNote,
  TriangleExclamationPoint,
} from '@lucide/vue'
import { request } from '../api/client'
import type { DossierResponse } from '../api/types'
import { errorMessage } from '../lib/errors'
import {
  formatDateTime,
  formatNumber,
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

onMounted(load)
</script>

<template>
  <div class="stack" style="gap: 18px">
    <div class="row-between wrap">
      <RouterLink
        v-if="dossier"
        class="btn btn-ghost btn-sm"
        :to="{ name: 'archive-detail', params: { id: dossier.workbookId } }"
      >
        <ArrowLeft :size="14" />
        Danh sách hồ sơ
      </RouterLink>
      <span v-else class="chip">Chi tiết hồ sơ</span>

      <RouterLink
        v-if="dossier"
        class="btn btn-primary"
        :to="{ name: 'print', params: { id: dossier.id } }"
      >
        <Printer :size="16" />
        In hồ sơ A4
      </RouterLink>
    </div>

    <div v-if="error" class="alert alert-danger">
      <TriangleExclamationPoint :size="17" />
      <div class="grow">
        <strong>Không tải được hồ sơ</strong>
        <div>{{ error }}</div>
      </div>
      <button class="btn btn-secondary btn-sm" type="button" @click="load">Thử lại</button>
    </div>

    <div v-if="loading" class="loading-block"><span class="spinner lg" /> Đang tải hồ sơ...</div>

    <div v-else-if="!dossier" class="card">
      <EmptyState title="Không tìm thấy hồ sơ" description="Hồ sơ có thể đã bị xoá trong lần đồng bộ gần nhất." />
    </div>

    <template v-else>
      <div class="card" :class="fitClass">
        <div class="card-body">
          <div class="row-between wrap">
            <div>
              <div class="text-xs muted strong" style="letter-spacing: 0.08em; text-transform: uppercase">
                Hồ sơ lưu trữ · năm {{ dossier.year }}
              </div>
              <h1 class="page-title" style="font-size: 28px; margin-top: 4px">
                Số {{ numberDisplay(dossier) }}
              </h1>
              <p class="page-subtitle" style="margin-top: 4px">
                {{ dossier.originalSummary || 'Chưa có trích yếu gốc' }}
              </p>
            </div>
            <div class="row wrap">
              <span class="badge badge-brand">{{ dossier.type || 'Chưa phân loại' }}</span>
              <span v-if="dossier.hasPrintLayout" class="badge badge-success">Sẵn sàng in</span>
              <span v-else class="badge badge-warning">Thiếu dữ liệu in</span>
            </div>
          </div>

          <div class="metric-grid" style="margin-top: 18px">
            <div class="metric">
              <div class="metric-label">Số hồ sơ</div>
              <div class="metric-value">{{ numberDisplay(dossier) }}</div>
            </div>
            <div class="metric">
              <div class="metric-label">Dòng ML</div>
              <div class="metric-value">{{ formatNumber(dossier.mlRow) }}</div>
            </div>
            <div class="metric">
              <div class="metric-label">Văn bản</div>
              <div class="metric-value">{{ formatNumber(dossier.documents.length) }}</div>
            </div>
            <div class="metric">
              <div class="metric-label">Tổng số tờ</div>
              <div class="metric-value">{{ dossier.totalPages > 0 ? formatNumber(dossier.totalPages) : '—' }}</div>
            </div>
          </div>

          <div class="row wrap" style="margin-top: 14px">
            <span class="chip">Nhóm: {{ dossier.group || '—' }}</span>
            <span class="chip">Hộp: {{ dossier.boxNumber || '—' }}</span>
            <span class="chip">Hồ sơ phụ: {{ dossier.subNumber || '—' }}</span>
            <span class="chip">Ghi chú số: {{ dossier.numberNote || '—' }}</span>
            <span class="chip">Tập: {{ dossier.sourceVolume || '—' }}</span>
            <span class="chip">Cập nhật: {{ formatDateTime(dossier.updatedAt) }}</span>
          </div>
        </div>
      </div>

      <div class="card">
        <div class="card-header">
          <div>
            <div class="card-title">
              <FileText :size="16" style="vertical-align: -2px" />
              Danh mục văn bản
            </div>
            <div class="card-subtitle">{{ dossier.documents.length }} văn bản · {{ totalPagesText(dossier.totalPages) }}</div>
          </div>
          <RouterLink class="btn btn-secondary btn-sm" :to="{ name: 'print', params: { id: dossier.id } }">
            <Printer :size="14" />
            Xem bản in
          </RouterLink>
        </div>
        <div class="card-body" style="padding-top: 12px">
          <EmptyState
            v-if="dossier.documents.length === 0"
            title="Chưa có văn bản"
            description="Hồ sơ này chưa có dòng văn bản nào trong sheet ML."
          />
          <div v-else class="table-wrap">
            <table class="table">
              <thead>
                <tr>
                  <th class="num">STT</th>
                  <th>Số ký hiệu</th>
                  <th>Ngày</th>
                  <th>Trích yếu</th>
                  <th>Tác giả</th>
                  <th class="num">Tờ số</th>
                  <th>Ghi chú</th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="line in dossier.documents" :key="line.stt">
                  <td class="num">{{ line.stt }}</td>
                  <td class="text-sm">{{ line.soKyHieu || '—' }}</td>
                  <td class="text-sm" style="white-space: nowrap">{{ line.ngay || '—' }}</td>
                  <td class="text-sm">{{ line.trichYeu || '—' }}</td>
                  <td class="text-sm">{{ line.tacGia || '—' }}</td>
                  <td class="num">{{ line.toSo || '—' }}</td>
                  <td class="text-sm">{{ line.ghiChu || '—' }}</td>
                </tr>
              </tbody>
            </table>
          </div>
        </div>
      </div>

      <div class="grid grid-2">
        <div class="card">
          <div class="card-header">
            <div>
              <div class="card-title"><BookOpen :size="16" style="vertical-align: -2px" /> Toàn văn</div>
              <div class="card-subtitle">Nội dung đầy đủ nhập trực tiếp trong Excel</div>
            </div>
          </div>
          <div class="card-body">
            <div v-if="dossier.fullText" class="prose-block">{{ dossier.fullText }}</div>
            <div v-else class="text-sm muted">Chưa có toàn văn cho hồ sơ này.</div>
          </div>
        </div>

        <div class="card">
          <div class="card-header">
            <div>
              <div class="card-title"><StickyNote :size="16" style="vertical-align: -2px" /> Ghi chú</div>
              <div class="card-subtitle">Ghi chú nghiệp vụ, không in vào mục lục</div>
            </div>
          </div>
          <div class="card-body">
            <div v-if="dossier.note" class="prose-block">{{ dossier.note }}</div>
            <div v-else class="text-sm muted">Chưa có ghi chú.</div>
          </div>
        </div>
      </div>
    </template>
  </div>
</template>
