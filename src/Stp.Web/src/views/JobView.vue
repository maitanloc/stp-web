<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref, watch } from 'vue'
import { RouterLink, useRoute } from 'vue-router'
import { ArrowLeft, FileClock } from '@lucide/vue'
import { request } from '../api/client'
import type { JobDetailResponse } from '../api/types'
import { errorMessage } from '../lib/errors'
import JobDetailPanel from '../components/JobDetailPanel.vue'
import { useToast } from '../composables/useToast'
import { useConfirm } from '../composables/useConfirm'

const route = useRoute()
const toast = useToast()
const { confirmDialog } = useConfirm()

const detail = ref<JobDetailResponse | null>(null)
const loading = ref(true)
const error = ref<string | null>(null)

let pollTimer: number | undefined
let wasActive = false
let notified = false

const jobId = computed(() => String(route.params.id))

function schedulePoll(): void {
  if (pollTimer) window.clearTimeout(pollTimer)
  pollTimer = window.setTimeout(() => void load(true), 2000)
}

function notifyFinished(): void {
  const summary = detail.value?.summary
  if (!summary || notified) return
  notified = true

  if (summary.status === 'Completed') {
    toast.success(
      'Job hoàn tất',
      `${summary.succeededItems}/${summary.totalItems} file thành công${summary.canDownload ? ' · có thể tải kết quả' : ''}.`,
    )
  } else if (summary.status === 'Failed') {
    toast.error('Job thất bại', summary.errorMessage ?? 'Xem nhật ký để biết chi tiết.')
  } else if (summary.status === 'Cancelled') {
    toast.warning('Job đã huỷ', 'Các file đã xử lý xong vẫn có thể tải riêng.')
  }
}

async function load(silent = false): Promise<void> {
  if (!silent) loading.value = true
  error.value = null
  try {
    detail.value = await request<JobDetailResponse>(`/api/jobs/${jobId.value}`)
    const status = detail.value.summary.status
    const active = status === 'Queued' || status === 'Running'

    if (wasActive && !active) {
      notifyFinished()
    }
    wasActive = active

    if (active) {
      schedulePoll()
    }
  } catch (caught) {
    error.value = errorMessage(caught)
  } finally {
    loading.value = false
  }
}

async function cancel(): Promise<void> {
  const ok = await confirmDialog({
    title: 'Huỷ tiến trình?',
    message: 'Huỷ job này? Các file đang xử lý dở sẽ không được tiếp tục.',
    variant: 'danger',
    confirmText: 'Huỷ job',
  })
  if (!ok) return
  try {
    await request(`/api/jobs/${jobId.value}/cancel`, { method: 'POST' })
    toast.warning('Đã gửi yêu cầu huỷ job')
    await load(true)
  } catch (caught) {
    toast.error('Không huỷ được job', errorMessage(caught))
  }
}

function refresh(): void {
  void load(true)
}

watch(jobId, () => {
  detail.value = null
  wasActive = false
  notified = false
  void load()
})

onMounted(() => void load())

onUnmounted(() => {
  if (pollTimer) window.clearTimeout(pollTimer)
})
</script>

<template>
  <div class="stack">
    <div class="row-between wrap">
      <div class="row wrap">
        <RouterLink class="btn btn-ghost btn-sm" to="/">
          <ArrowLeft :size="14" />
          Tổng quan
        </RouterLink>
        <span class="chip">
          <FileClock :size="14" />
          <span class="mono text-xs">{{ jobId }}</span>
        </span>
      </div>
      <span class="text-xs muted">Trang tự làm mới mỗi 2 giây khi job đang chạy.</span>
    </div>

    <div v-if="error" class="alert alert-danger">
      <div class="grow">
        <strong>Không tải được job</strong>
        <div>{{ error }}</div>
      </div>
      <button class="btn btn-secondary btn-sm" type="button" @click="refresh">Thử lại</button>
    </div>

    <JobDetailPanel
      :detail="detail"
      :loading="loading"
      @cancel="cancel"
      @refresh="refresh"
    />
  </div>
</template>
