<script setup lang="ts">
import { ref } from 'vue'
import { CloudUpload, FileSpreadsheet, Trash } from '@lucide/vue'
import { formatBytes } from '../lib/format'

const props = withDefaults(
  defineProps<{
    modelValue: File[]
    accept?: string
    hint?: string
    multiple?: boolean
  }>(),
  {
    accept: '.xlsx,.xlsm,.xltx,.xltm',
    hint: 'Kéo thả hoặc bấm để chọn file Excel',
    multiple: true,
  },
)

const emit = defineEmits<{
  (event: 'update:modelValue', value: File[]): void
}>()

const dragging = ref(false)

function addFiles(list: FileList | null): void {
  if (!list || list.length === 0) return

  const merged = [...props.modelValue]
  for (const file of Array.from(list)) {
    const duplicate = merged.some(
      (existing) =>
        existing.name === file.name &&
        existing.size === file.size &&
        existing.lastModified === file.lastModified,
    )
    if (!duplicate) {
      merged.push(file)
      if (!props.multiple) break
    }
  }

  emit('update:modelValue', props.multiple ? merged : merged.slice(0, 1))
}

function onInput(event: Event): void {
  const target = event.target as HTMLInputElement
  addFiles(target.files)
  target.value = ''
}

function onDrop(event: DragEvent): void {
  dragging.value = false
  addFiles(event.dataTransfer?.files ?? null)
}

function removeAt(index: number): void {
  const next = [...props.modelValue]
  next.splice(index, 1)
  emit('update:modelValue', next)
}
</script>

<template>
  <div>
    <div
      class="dropzone"
      :class="{ dragging }"
      @dragover.prevent="dragging = true"
      @dragleave.prevent="dragging = false"
      @drop.prevent="onDrop"
    >
      <div class="dropzone-icon">
        <CloudUpload :size="24" />
      </div>
      <div class="dropzone-title">Chọn file Excel cần xử lý</div>
      <div class="dropzone-hint">{{ hint }}</div>
      <div class="dropzone-hint">{{ accept.replaceAll(',', ' · ') }}</div>
      <input
        type="file"
        :accept="accept"
        :multiple="multiple"
        aria-label="Chọn file Excel"
        @change="onInput"
      />
    </div>

    <div v-if="modelValue.length > 0" class="file-list">
      <div v-for="(file, index) in modelValue" :key="`${file.name}-${file.size}-${index}`" class="file-item">
        <div class="file-icon">
          <FileSpreadsheet :size="17" />
        </div>
        <div class="grow">
          <div class="file-name" :title="file.name">{{ file.name }}</div>
          <div class="file-meta">{{ formatBytes(file.size) }}</div>
        </div>
        <button class="btn btn-ghost btn-sm" type="button" @click="removeAt(index)">
          <Trash :size="14" />
          Bỏ
        </button>
      </div>
    </div>
  </div>
</template>
