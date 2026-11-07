<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import {
  ArrowRight,
  FileSpreadsheet,
  Info,
  ScanLine,
  Sparkles,
  TriangleExclamationPoint,
} from '@lucide/vue'
import { request } from '../api/client'
import type { InspectionResponse, JobCreatedResponse } from '../api/types'
import { errorMessage } from '../lib/errors'
import { formatBytes, formatNumber, originalModeLabel } from '../lib/format'
import FilePicker from '../components/FilePicker.vue'
import InspectionResult from '../components/InspectionResult.vue'
import { useToast } from '../composables/useToast'

const router = useRouter()
const toast = useToast()

const files = ref<File[]>([])
const originalMode = ref<number>(0)
const createZip = ref(true)
const mergeOutput = ref(true)
const generatePrintSheets = ref(true)
const injectMacro = ref(true)
const headerText = ref('')
const footerText = ref('')

const inspecting = ref(false)
const inspection = ref<InspectionResponse | null>(null)
const inspectError = ref<string | null>(null)

const submitting = ref(false)
const submitError = ref<string | null>(null)

const modes = [
  {
    value: 0,
    title: 'Giữ file gốc',
    description: 'File upload được giữ nguyên; kết quả .xlsm ghi vào thư mục outputs của job.',
  },
  {
    value: 1,
    title: 'Ghi cạnh file gốc + sao lưu',
    description: 'Tạo .xlsm cùng thư mục với file tải lên và sao lưu bản gốc vào data/backups.',
  },
  {
    value: 2,
    title: 'Chỉ xuất vào thư mục outputs',
    description: 'Mọi kết quả tập trung trong thư mục outputs của job, không đụng file gốc.',
  },
]

const totalSize = computed(() => files.value.reduce((total, file) => total + file.size, 0))

watch(files, () => {
  inspection.value = null
  inspectError.value = null
})

async function inspectFirst(): Promise<void> {
  const first = files.value[0]
  if (!first) return

  inspecting.value = true
  inspectError.value = null
  try {
    const form = new FormData()
    form.append('file', first, first.name)
    inspection.value = await request<InspectionResponse>('/api/inspect', {
      method: 'POST',
      form,
    })
  } catch (caught) {
    inspection.value = null
    inspectError.value = errorMessage(caught)
  } finally {
    inspecting.value = false
  }
}

async function submit(): Promise<void> {
  if (files.value.length === 0) {
    submitError.value = 'Hãy chọn ít nhất một file Excel.'
    return
  }

  submitting.value = true
  submitError.value = null
  try {
    const form = new FormData()
    for (const file of files.value) {
      form.append('files', file, file.name)
    }
    form.append('originalFileMode', String(originalMode.value))
    form.append('createZip', String(createZip.value))
    form.append('mergeOutput', String(mergeOutput.value))
    form.append('generatePrintSheets', String(generatePrintSheets.value))
    form.append('injectMacro', String(injectMacro.value))
    if (headerText.value.trim()) form.append('headerText', headerText.value.trim())
    if (footerText.value.trim()) form.append('footerText', footerText.value.trim())

    const created = await request<JobCreatedResponse>('/api/jobs/convert', {
      method: 'POST',
      form,
    })

    toast.success('Đã đưa job vào hàng đợi', created.message)
    await router.push({ name: 'job', params: { id: created.id } })
  } catch (caught) {
    submitError.value = errorMessage(caught)
    toast.error('Không tạo được job', submitError.value)
  } finally {
    submitting.value = false
  }
}
</script>

<template>
  <div class="convert-layout">
    <div class="stack">
      <div class="alert alert-info">
        <Sparkles :size="17" />
        <div>
          <strong>Quy trình tự động:</strong>
          nhận diện loại 1 (có ML + sheet in) hoặc loại 2 (chỉ có ML), sinh sheet in A4 dọc nếu cần,
          chèn bộ công cụ và macro, luôn xuất <strong>.xlsm</strong>. Khi mở file trên máy khác chỉ cần bấm
          <strong>Enable Content</strong>.
        </div>
      </div>

      <div class="card">
        <div class="card-header">
          <div>
            <div class="card-title">1 · Chọn workbook</div>
            <div class="card-subtitle">Có thể chọn tối đa 50 file cùng cấu trúc trong một job</div>
          </div>
          <span class="badge badge-brand">{{ files.length }} file</span>
        </div>
        <div class="card-body">
          <FilePicker v-model="files" />
        </div>
      </div>

      <div class="card">
        <div class="card-header">
          <div>
            <div class="card-title">Kiểm tra nhanh file đầu tiên</div>
            <div class="card-subtitle">Xem loại workbook, số năm, hồ sơ và sheet in trước khi chạy</div>
          </div>
          <button
            class="btn btn-secondary btn-sm"
            type="button"
            :disabled="files.length === 0 || inspecting"
            @click="inspectFirst"
          >
            <span v-if="inspecting" class="spinner" />
            <ScanLine v-else :size="14" />
            {{ inspecting ? 'Đang đọc...' : 'Kiểm tra' }}
          </button>
        </div>
        <div class="card-body" style="padding-top: 14px">
          <div v-if="inspectError" class="alert alert-danger">
            <TriangleExclamationPoint :size="17" />
            <div>{{ inspectError }}</div>
          </div>
          <InspectionResult v-else-if="inspection" :inspection="inspection" />
          <div v-else class="text-sm muted">
            Chọn file rồi bấm <strong>Kiểm tra</strong> để xem cấu trúc workbook. Bước này không bắt buộc.
          </div>
        </div>
      </div>
    </div>

    <div class="stack">
      <div class="card">
        <div class="card-header">
          <div>
            <div class="card-title">2 · Tuỳ chọn xử lý</div>
            <div class="card-subtitle">Cách lưu file gốc và nội dung sinh thêm</div>
          </div>
        </div>
        <div class="card-body">
          <div class="field">
            <span class="label">Xử lý file gốc</span>
            <div class="radio-cards">
              <label
                v-for="mode in modes"
                :key="mode.value"
                class="radio-card"
                :class="{ selected: originalMode === mode.value }"
              >
                <input v-model="originalMode" type="radio" :value="mode.value" />
                <span class="checkbox-text">
                  <span class="checkbox-title">{{ mode.title }}</span>
                  <span class="checkbox-desc">{{ mode.description }}</span>
                </span>
              </label>
            </div>
          </div>

          <div class="divider" />

          <label class="toggle-row">
            <span class="checkbox-text">
              <span class="checkbox-title">Đóng gói ZIP</span>
              <span class="checkbox-desc">Gom toàn bộ .xlsm và workbook gộp vào một file ZIP.</span>
            </span>
            <span class="switch">
              <input v-model="createZip" type="checkbox" />
              <span class="switch-track" />
            </span>
          </label>

          <label class="toggle-row">
            <span class="checkbox-text">
              <span class="checkbox-title">Tạo workbook gộp</span>
              <span class="checkbox-desc">Thêm file GOP gồm ML, bảng tổng hợp và sheet in các năm.</span>
            </span>
            <span class="switch">
              <input v-model="mergeOutput" type="checkbox" />
              <span class="switch-track" />
            </span>
          </label>

          <label class="toggle-row">
            <span class="checkbox-text">
              <span class="checkbox-title">Tự sinh sheet in cho file loại 2</span>
              <span class="checkbox-desc">File chỉ có ML sẽ được sinh sheet in A4 dọc theo từng năm.</span>
            </span>
            <span class="switch">
              <input v-model="generatePrintSheets" type="checkbox" />
              <span class="switch-track" />
            </span>
          </label>

          <label class="toggle-row">
            <span class="checkbox-text">
              <span class="checkbox-title">Chèn macro STP</span>
              <span class="checkbox-desc">Dùng phiên bản macro đang active trong hệ thống.</span>
            </span>
            <span class="switch">
              <input v-model="injectMacro" type="checkbox" />
              <span class="switch-track" />
            </span>
          </label>

          <details class="details-block" style="margin-top: 12px">
            <summary>Header / footer tuỳ chỉnh</summary>
            <div class="stack" style="gap: 10px; margin-top: 10px">
              <label class="field">
                <span class="label">Header in</span>
                <input v-model="headerText" class="input" placeholder="Ví dụ: PHÒNG LƯU TRỮ" />
              </label>
              <label class="field">
                <span class="label">Footer in</span>
                <input v-model="footerText" class="input" placeholder="Ví dụ: STP Archive" />
              </label>
            </div>
          </details>
        </div>
      </div>

      <div class="card">
        <div class="card-body">
          <div class="row-between">
            <div>
              <div class="text-sm strong">{{ files.length }} file · {{ formatBytes(totalSize) }}</div>
              <div class="text-xs muted">{{ originalModeLabel[String(originalMode)] ?? '' }}</div>
            </div>
            <FileSpreadsheet :size="22" class="muted" />
          </div>

          <div v-if="submitError" class="alert alert-danger" style="margin-top: 12px">
            <TriangleExclamationPoint :size="17" />
            <div>{{ submitError }}</div>
          </div>

          <button
            class="btn btn-primary btn-lg btn-block"
            type="button"
            style="margin-top: 14px"
            :disabled="submitting || files.length === 0"
            @click="submit"
          >
            <span v-if="submitting" class="spinner" />
            <ArrowRight v-else :size="17" />
            {{ submitting ? 'Đang tải file...' : 'Bắt đầu chuyển đổi' }}
          </button>
          <div class="hint" style="margin-top: 8px">
            Job chạy nền tuần tự. Sau khi tạo, bạn sẽ được chuyển sang trang theo dõi tiến độ.
            File gộp (nếu bật) dùng định dạng <strong>.xlsm</strong>.
          </div>
        </div>
      </div>

      <div class="alert">
        <Info :size="17" />
        <div>
          Số file tối đa mỗi job: {{ formatNumber(50) }}. Dung lượng mỗi file tối đa 200 MB.
        </div>
      </div>
    </div>
  </div>
</template>
