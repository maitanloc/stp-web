<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { ArrowRight, Combine, FileSpreadsheet, Info, ScanLine, TriangleExclamationPoint } from '@lucide/vue'
import { request } from '../api/client'
import type { InspectionResponse, JobCreatedResponse } from '../api/types'
import { errorMessage } from '../lib/errors'
import { formatBytes } from '../lib/format'
import FilePicker from '../components/FilePicker.vue'
import InspectionResult from '../components/InspectionResult.vue'
import { useToast } from '../composables/useToast'

const router = useRouter()
const toast = useToast()

const files = ref<File[]>([])
const generatePrintSheets = ref(true)
const injectMacro = ref(true)
const headerText = ref('')
const footerText = ref('')

const inspecting = ref(false)
const inspection = ref<InspectionResponse | null>(null)
const inspectError = ref<string | null>(null)

const submitting = ref(false)
const submitError = ref<string | null>(null)

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
    inspection.value = await request<InspectionResponse>('/api/inspect', { method: 'POST', form })
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
    form.append('generatePrintSheets', String(generatePrintSheets.value))
    form.append('injectMacro', String(injectMacro.value))
    if (headerText.value.trim()) form.append('headerText', headerText.value.trim())
    if (footerText.value.trim()) form.append('footerText', footerText.value.trim())

    const created = await request<JobCreatedResponse>('/api/jobs/merge', { method: 'POST', form })
    toast.success('Đã đưa job gộp vào hàng đợi', created.message)
    await router.push({ name: 'job', params: { id: created.id } })
  } catch (caught) {
    submitError.value = errorMessage(caught)
    toast.error('Không tạo được job gộp', submitError.value)
  } finally {
    submitting.value = false
  }
}
</script>

<template>
  <div class="convert-layout">
    <div class="stack">
      <div class="alert alert-info">
        <Combine :size="17" />
        <div>
          Workbook gộp giữ sheet <strong>ML</strong>, thêm sheet <strong>GOP</strong> tổng hợp và
          sinh sheet in theo từng năm. Hồ sơ trùng số vẫn được giữ, kèm cột file nguồn để đối chiếu.
        </div>
      </div>

      <div class="card">
        <div class="card-header">
          <div>
            <div class="card-title">1 · Chọn các workbook cần gộp</div>
            <div class="card-subtitle">Các file phải cùng cấu trúc ML; thứ tự gộp theo tên file</div>
          </div>
          <span class="badge badge-accent">{{ files.length }} file</span>
        </div>
        <div class="card-body">
          <FilePicker v-model="files" hint="Chọn từ 2 file trở lên để gộp thành một workbook" />
        </div>
      </div>

      <div class="card">
        <div class="card-header">
          <div>
            <div class="card-title">Kiểm tra nhanh file đầu tiên</div>
            <div class="card-subtitle">Đảm bảo file có sheet ML đúng cấu trúc</div>
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
            Bước kiểm tra không bắt buộc. Job gộp chỉ cần các workbook có sheet <strong>ML</strong>.
          </div>
        </div>
      </div>
    </div>

    <div class="stack">
      <div class="card">
        <div class="card-header">
          <div>
            <div class="card-title">2 · Tuỳ chọn gộp</div>
            <div class="card-subtitle">Kết quả là một workbook .xlsm duy nhất</div>
          </div>
        </div>
        <div class="card-body">
          <label class="toggle-row">
            <span class="checkbox-text">
              <span class="checkbox-title">Sinh sheet in theo năm</span>
              <span class="checkbox-desc">Sheet in A4 dọc cho từng năm, mỗi hồ sơ một trang.</span>
            </span>
            <span class="switch">
              <input v-model="generatePrintSheets" type="checkbox" />
              <span class="switch-track" />
            </span>
          </label>

          <label class="toggle-row">
            <span class="checkbox-text">
              <span class="checkbox-title">Chèn macro STP</span>
              <span class="checkbox-desc">Kèm bộ công cụ lọc, xem trước và in trong Excel.</span>
            </span>
            <span class="switch">
              <input v-model="injectMacro" type="checkbox" />
              <span class="switch-track" />
            </span>
          </label>

          <div class="divider" />

          <label class="field">
            <span class="label">Header in</span>
            <input v-model="headerText" class="input" placeholder="Ví dụ: PHÒNG LƯU TRỮ" />
          </label>
          <label class="field" style="margin-top: 10px">
            <span class="label">Footer in</span>
            <input v-model="footerText" class="input" placeholder="Ví dụ: STP Archive" />
          </label>

          <div class="alert" style="margin-top: 14px">
            <Info :size="17" />
            <div>
              File gộp được tạo từ sheet ML của tất cả file đầu vào. Nếu muốn giữ riêng từng file,
              hãy dùng trang <strong>Chuyển đổi Excel</strong>.
            </div>
          </div>
        </div>
      </div>

      <div class="card">
        <div class="card-body">
          <div class="row-between">
            <div>
              <div class="text-sm strong">{{ files.length }} file · {{ formatBytes(totalSize) }}</div>
              <div class="text-xs muted">Đầu ra: GOP.xlsm</div>
            </div>
            <FileSpreadsheet :size="22" class="muted" />
          </div>

          <div v-if="submitError" class="alert alert-danger" style="margin-top: 12px">
            <TriangleExclamationPoint :size="17" />
            <div>{{ submitError }}</div>
          </div>

          <button
            class="btn btn-accent btn-lg btn-block"
            type="button"
            style="margin-top: 14px"
            :disabled="submitting || files.length === 0"
            @click="submit"
          >
            <span v-if="submitting" class="spinner" />
            <ArrowRight v-else :size="17" />
            {{ submitting ? 'Đang tải file...' : 'Bắt đầu gộp file' }}
          </button>
          <div class="hint" style="margin-top: 8px">
            Job chạy nền và có thể mất vài phút với file lớn. Theo dõi tiến độ ở trang job.
          </div>
        </div>
      </div>
    </div>
  </div>
</template>
