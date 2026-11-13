<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref } from 'vue'
import { Check, ChevronDown, Search } from '@lucide/vue'

export interface CustomSelectOption {
  value: string
  label: string
  /** Hiển thị bên phải, dùng cho số lượng hồ sơ. */
  countLabel?: string
}

const props = withDefaults(
  defineProps<{
    modelValue: string
    options: CustomSelectOption[]
    placeholder?: string
    disabled?: boolean
    searchable?: boolean
    ariaLabel?: string
  }>(),
  {
    placeholder: 'Chọn…',
    disabled: false,
    searchable: true,
    ariaLabel: undefined,
  },
)

const emit = defineEmits<{
  (event: 'update:modelValue', value: string): void
  (event: 'change', value: string): void
}>()

const open = ref(false)
const query = ref('')
const triggerRef = ref<HTMLButtonElement | null>(null)
const popoverRef = ref<HTMLDivElement | null>(null)
const searchRef = ref<HTMLInputElement | null>(null)
const dropUp = ref(false)
const position = ref({ top: 0, left: 0, width: 280 })
const activeIndex = ref(-1)

const current = computed(
  () => props.options.find((option) => option.value === props.modelValue) ?? null,
)

const label = computed(() => current.value?.label ?? props.placeholder)

const filtered = computed(() => {
  if (!props.searchable) return props.options
  const term = normalizeText(query.value.trim())
  if (!term) return props.options
  return props.options.filter((option) => normalizeText(option.label).includes(term))
})

const showSearch = computed(() => props.searchable && props.options.length > 6)

function normalizeText(value: string): string {
  return value
    .normalize('NFD')
    .replace(/[̀-ͯ]/g, '')
    .replace(/\s+/g, ' ')
    .trim()
    .toLowerCase()
}

function selectValue(value: string): void {
  emit('update:modelValue', value)
  emit('change', value)
  close()
  // preventScroll: tick option xong focus lại trigger — nếu không chặn thì
  // trình duyệt CUỘN NHẢY về trigger, cả trang giật "biến dạng" (lỗi lướt).
  nextTick(() => triggerRef.value?.focus({ preventScroll: true }))
}

function openPopover(): void {
  if (props.disabled) return

  open.value = true
  query.value = ''
  activeIndex.value = Math.max(
    0,
    filtered.value.findIndex((option) => option.value === props.modelValue),
  )

  nextTick(() => {
    const trigger = triggerRef.value
    const panel = popoverRef.value
    if (!trigger) return

    // Đo chiều cao TỰ NHIÊN: bỏ max-height cài lần trước để không đo nhầm bản đã co.
    if (panel) panel.style.maxHeight = ''

    const rect = trigger.getBoundingClientRect()
    const width = Math.min(
      Math.max(rect.width, 220),
      Math.min(420, window.innerWidth - 24),
    )
    const left = Math.min(Math.max(12, rect.left), Math.max(12, window.innerWidth - width - 12))

    // Hướng nào đủ chỗ cho chiều cao tự nhiên thì dùng hướng đó; cả hai thiếu
    // (màn hình ngắn) thì chọn hướng RỘNG HƠN và CO panel theo chỗ có — panel
    // KHÔNG BAO GIỜ đè nút mở/khu form (bug "biến dạng form" trên màn ngắn).
    const naturalH = panel?.offsetHeight ?? 320
    const gap = 8
    const spaceBelow = window.innerHeight - 12 - (rect.bottom + gap)
    const spaceAbove = rect.top - gap - 12

    dropUp.value = spaceBelow < naturalH && spaceAbove > spaceBelow

    const avail = dropUp.value ? spaceAbove : spaceBelow
    const maxH = Math.max(180, Math.min(naturalH, avail))
    if (panel) panel.style.maxHeight = `${maxH}px`

    position.value = {
      top: dropUp.value ? rect.top - gap : rect.bottom + gap,
      left,
      width,
    }

    if (showSearch.value) {
      // preventScroll: panel fixed luôn nằm trong viewport, focus ô tìm không được kéo trang.
      searchRef.value?.focus({ preventScroll: true })
    }
    scrollActiveIntoView()
  })
}

function close(): void {
  open.value = false
  activeIndex.value = -1
}

function onPointerDown(event: PointerEvent): void {
  if (!open.value) return

  const target = event.target as Node
  if (triggerRef.value?.contains(target) || popoverRef.value?.contains(target)) {
    return
  }

  close()
}

/** Cuộn trang bên ngoài mới đóng; cuộn trong danh sách vẫn giữ mở. */
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

function onKeydown(event: KeyboardEvent): void {
  if (event.key === 'Escape') close()
}

function onListKeydown(event: KeyboardEvent): void {
  if (event.key === 'ArrowDown') {
    event.preventDefault()
    activeIndex.value = Math.min(activeIndex.value + 1, filtered.value.length - 1)
    scrollActiveIntoView()
    return
  }

  if (event.key === 'ArrowUp') {
    event.preventDefault()
    activeIndex.value = Math.max(activeIndex.value - 1, 0)
    scrollActiveIntoView()
    return
  }

  if (event.key === 'Enter') {
    const option = filtered.value[activeIndex.value]
    if (option) {
      event.preventDefault()
      selectValue(option.value)
    }
  }
}

function scrollActiveIntoView(): void {
  nextTick(() => {
    const list = popoverRef.value?.querySelector<HTMLElement>('.cs-list')
    const option = popoverRef.value?.querySelector<HTMLElement>(
      `[data-index="${activeIndex.value}"]`,
    )
    if (!list || !option) return

    // Cuộn CHỈ trong danh sách — scrollIntoView trên panel position:fixed
    // có thể kéo theo cả trang phía sau (hiện tượng "kéo cột").
    const listRect = list.getBoundingClientRect()
    const optionRect = option.getBoundingClientRect()
    if (optionRect.top < listRect.top) {
      list.scrollTop -= listRect.top - optionRect.top
    } else if (optionRect.bottom > listRect.bottom) {
      list.scrollTop += optionRect.bottom - listRect.bottom
    }
  })
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
  <div class="cs-picker">
    <button
      ref="triggerRef"
      type="button"
      class="cs-trigger"
      :class="{ 'is-open': open, 'has-value': !!current }"
      :disabled="disabled"
      :aria-label="ariaLabel ?? label"
      :title="label"
      @click="open ? close() : openPopover()"
    >
      <span class="cs-trigger-text">{{ label }}</span>
      <ChevronDown :size="15" class="cs-chevron" :class="{ open }" />
    </button>

    <Teleport to="body">
      <div
        v-if="open"
        ref="popoverRef"
        class="cs-popover"
        :class="{ up: dropUp }"
        :style="{
          top: `${position.top}px`,
          left: `${position.left}px`,
          width: `${position.width}px`,
        }"
        role="listbox"
        @keydown="onListKeydown"
      >
        <div v-if="showSearch" class="cs-search">
          <Search :size="14" />
          <input
            ref="searchRef"
            v-model="query"
            class="input"
            type="search"
            placeholder="Gõ để tìm..."
            @keydown="onListKeydown"
          />
        </div>

        <div class="cs-list">
          <p v-if="filtered.length === 0" class="cs-empty">Không có mục phù hợp.</p>
          <button
            v-for="(option, index) in filtered"
            :key="option.value"
            type="button"
            class="cs-option"
            :class="{
              checked: option.value === modelValue,
              active: index === activeIndex,
            }"
            :data-index="index"
            role="option"
            :aria-selected="option.value === modelValue"
            @click="selectValue(option.value)"
            @mousemove="activeIndex = index"
          >
            <Check v-if="option.value === modelValue" :size="14" class="cs-check" />
            <span v-else class="cs-check-space" />
            <span class="cs-option-text">{{ option.label }}</span>
            <span v-if="option.countLabel" class="cs-count">{{ option.countLabel }}</span>
          </button>
        </div>
      </div>
    </Teleport>
  </div>
</template>
