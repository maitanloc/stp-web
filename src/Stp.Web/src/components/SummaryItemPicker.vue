<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref } from 'vue'
import { Check, ChevronDown, Plus, Search, X } from '@lucide/vue'

const props = withDefaults(
  defineProps<{
    modelValue: string[]
    catalog: string[]
    placeholder?: string
    disabled?: boolean
  }>(),
  {
    placeholder: 'Chọn giấy tờ',
    disabled: false,
  },
)

const emit = defineEmits<{
  (event: 'update:modelValue', value: string[]): void
}>()

const open = ref(false)
const query = ref('')
const custom = ref('')
const triggerRef = ref<HTMLButtonElement | null>(null)
const popoverRef = ref<HTMLDivElement | null>(null)
const dropUp = ref(false)
const position = ref({ top: 0, left: 0, width: 360 })

const selectedKeys = computed(() => new Set(props.modelValue.map(normalizeText)))

const options = computed(() => {
  const items = [...props.catalog]
  for (const item of props.modelValue) {
    if (!items.some((existing) => normalizeText(existing) === normalizeText(item))) {
      items.push(item)
    }
  }

  const term = normalizeText(query.value.trim())
  return term ? items.filter((item) => normalizeText(item).includes(term)) : items
})

const label = computed(() => {
  if (props.modelValue.length === 0) return props.placeholder
  if (props.modelValue.length === 1) return props.modelValue[0]
  return `${props.modelValue.length} loại giấy tờ`
})

function normalizeText(value: string): string {
  return value
    .normalize('NFD')
    .replace(/[\u0300-\u036f]/g, '')
    .replace(/\s+/g, ' ')
    .trim()
    .toLowerCase()
}

function toggle(item: string): void {
  const key = normalizeText(item)
  const exists = props.modelValue.some((value) => normalizeText(value) === key)
  const next = exists
    ? props.modelValue.filter((value) => normalizeText(value) !== key)
    : [...props.modelValue, item]

  emit('update:modelValue', next)
}

function clearAll(): void {
  emit('update:modelValue', [])
}

function addCustom(): void {
  const value = custom.value.trim().replace(/\s+/g, ' ')
  if (!value) return

  if (!props.modelValue.some((item) => normalizeText(item) === normalizeText(value))) {
    emit('update:modelValue', [...props.modelValue, value])
  }

  custom.value = ''
}

function openPopover(): void {
  if (props.disabled) return

  open.value = true
  query.value = ''
  nextTick(() => {
    const trigger = triggerRef.value
    if (!trigger) return

    const rect = trigger.getBoundingClientRect()
    const width = Math.min(390, window.innerWidth - 24)
    const left = Math.min(Math.max(12, rect.left), Math.max(12, window.innerWidth - width - 12))
    const estimatedHeight = 430
    const below = window.innerHeight - rect.bottom
    const above = rect.top
    dropUp.value = below < Math.min(estimatedHeight, 360) && above > below

    position.value = {
      top: dropUp.value ? rect.top - 8 : rect.bottom + 8,
      left,
      width,
    }
  })
}

function close(): void {
  open.value = false
}

function onPointerDown(event: PointerEvent): void {
  if (!open.value) return

  const target = event.target as Node
  if (triggerRef.value?.contains(target) || popoverRef.value?.contains(target)) {
    return
  }

  close()
}

function onKeydown(event: KeyboardEvent): void {
  if (event.key === 'Escape') close()
}

/**
 * Chỉ đóng khi trang phía sau cuộn. Cuộn bên trong danh sách giấy tờ
 * (chuột lăn, kéo thanh cuộn) phải giữ popover mở.
 */
function onScroll(event: Event): void {
  if (!open.value) return

  const target = event.target as Node | null
  if (
    target &&
    (popoverRef.value?.contains(target) || triggerRef.value?.contains(target))
  ) {
    return
  }

  close()
}

onMounted(() => {
  document.addEventListener('pointerdown', onPointerDown)
  window.addEventListener('keydown', onKeydown)
  window.addEventListener('resize', close)
  document.addEventListener('scroll', onScroll, true)
})

onBeforeUnmount(() => {
  document.removeEventListener('pointerdown', onPointerDown)
  window.removeEventListener('keydown', onKeydown)
  window.removeEventListener('resize', close)
  document.removeEventListener('scroll', onScroll, true)
})
</script>

<template>
  <div class="ms-picker">
    <button
      ref="triggerRef"
      type="button"
      class="ms-trigger"
      :class="{ 'is-open': open, 'has-value': modelValue.length > 0 }"
      :disabled="disabled"
      :title="modelValue.length > 0 ? modelValue.join(', ') : placeholder"
      @click="open ? close() : openPopover()"
    >
      <span class="ms-trigger-text">{{ label }}</span>
      <span v-if="modelValue.length > 1" class="ms-count">{{ modelValue.length }}</span>
      <ChevronDown :size="15" class="ms-chevron" />
    </button>

    <Teleport to="body">
      <div
        v-if="open"
        ref="popoverRef"
        class="ms-popover"
        :class="{ up: dropUp }"
        :style="{
          top: `${position.top}px`,
          left: `${position.left}px`,
          width: `${position.width}px`,
        }"
        role="dialog"
        aria-label="Chọn giấy tờ"
      >
        <div class="ms-search">
          <Search :size="14" />
          <input
            v-model="query"
            class="input"
            type="search"
            placeholder="Tìm giấy tờ..."
            autofocus
          />
          <button
            v-if="modelValue.length > 0"
            class="btn btn-ghost btn-sm"
            type="button"
            title="Bỏ chọn tất cả"
            @click="clearAll"
          >
            <X :size="13" />
            Bỏ chọn
          </button>
        </div>

        <div class="ms-list">
          <p v-if="options.length === 0" class="ms-empty">Không có mục phù hợp.</p>
          <label
            v-for="item in options"
            :key="item"
            class="ms-option"
            :class="{ checked: selectedKeys.has(normalizeText(item)) }"
          >
            <input
              type="checkbox"
              :checked="selectedKeys.has(normalizeText(item))"
              @change="toggle(item)"
            />
            <span class="ms-checkbox">
              <Check :size="12" />
            </span>
            <span class="ms-option-text">{{ item }}</span>
          </label>
        </div>

        <div class="ms-custom">
          <input
            v-model="custom"
            class="input"
            placeholder="Thêm giấy tờ khác..."
            @keydown.enter.prevent="addCustom"
          />
          <button
            class="btn btn-secondary btn-sm"
            type="button"
            :disabled="!custom.trim()"
            @click="addCustom"
          >
            <Plus :size="14" />
            Thêm
          </button>
        </div>

        <div class="ms-hint">
          Đã chọn {{ modelValue.length }} mục. Bấm từng mục để thêm hoặc bỏ.
        </div>
      </div>
    </Teleport>
  </div>
</template>
