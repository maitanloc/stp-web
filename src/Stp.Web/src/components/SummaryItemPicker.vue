<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { Check, ChevronDown, Plus, Search, X } from '@lucide/vue'

const props = withDefaults(
  defineProps<{
    modelValue: string[]
    catalog: string[]
    placeholder?: string
    disabled?: boolean
    /** Số lần loại giấy tờ được gặp (badge ×N). Key đã chuẩn hoá như normalizeText. */
    counts?: Record<string, number>
  }>(),
  {
    placeholder: 'Chọn giấy tờ',
    disabled: false,
    counts: undefined,
  },
)

const emit = defineEmits<{
  (event: 'update:modelValue', value: string[]): void
  /** Người dùng thêm mục ngoài danh mục — view tự lưu database (danh mục học được). */
  (event: 'custom-added', value: string): void
}>()

const open = ref(false)
const query = ref('')
const debouncedQuery = ref('')
const custom = ref('')
const triggerRef = ref<HTMLButtonElement | null>(null)
const popoverRef = ref<HTMLDivElement | null>(null)
const dropUp = ref(false)
const position = ref({ top: 0, left: 0, width: 360 })
const activeIndex = ref(-1)

const selectedKeys = computed(() => new Set(props.modelValue.map(normalizeText)))

const options = computed(() => {
  const items = [...props.catalog]
  for (const item of props.modelValue) {
    if (!items.some((existing) => normalizeText(existing) === normalizeText(item))) {
      items.push(item)
    }
  }

  const tokens = searchTokens(debouncedQuery.value)
  if (tokens.length === 0) return items

  const phrase = normalizeText(debouncedQuery.value.trim())
  return items
    .map((item, index) => {
      const key = normalizeText(item)
      const matched = tokens.every((token) => key.includes(token))
      const phrasePos = key.indexOf(phrase)
      const firstTokenPos = Math.min(...tokens.map((token) => key.indexOf(token)))
      return { item, index, matched, phrasePos: phrasePos < 0 ? Number.MAX_SAFE_INTEGER : phrasePos, firstTokenPos: firstTokenPos < 0 ? Number.MAX_SAFE_INTEGER : firstTokenPos }
    })
    .filter((entry) => entry.matched)
    .sort((a, b) => a.phrasePos - b.phrasePos || a.firstTokenPos - b.firstTokenPos || a.index - b.index)
    .map((entry) => entry.item)
})

function searchTokens(queryText: string): string[] {
  return normalizeText(queryText)
    .split(' ')
    .filter((token) => token.length > 0)
}

function highlightParts(item: string): Array<{ text: string; hit: boolean }> {
  const tokens = searchTokens(debouncedQuery.value)
  if (tokens.length === 0) return [{ text: item, hit: false }]

  const key = normalizeText(item)
  const segments: Array<{ text: string; hit: boolean }> = []
  let cursor = 0

  // Tô sáng cụm khớp dài nhất trong từng token (theo ký tự gốc).
  const folded = key

  for (const token of tokens) {
    const pos = folded.indexOf(token, cursor)
    if (pos < 0) continue
    if (pos > cursor) segments.push({ text: item.slice(cursor, pos), hit: false })
    segments.push({ text: item.slice(pos, pos + token.length), hit: true })
    cursor = pos + token.length
  }

  if (cursor < item.length) {
    segments.push({ text: item.slice(cursor), hit: false })
  }

  return segments.length > 0 ? segments : [{ text: item, hit: false }]
}

let searchTimer: number | undefined

function onSearchInput(): void {
  if (searchTimer) window.clearTimeout(searchTimer)
  searchTimer = window.setTimeout(() => {
    debouncedQuery.value = query.value
    activeIndex.value = options.value.length > 0 ? 0 : -1
  }, 120)
}

function onListKeydown(event: KeyboardEvent): void {
  if (!open.value) return

  if (event.key === 'ArrowDown') {
    event.preventDefault()
    activeIndex.value = Math.min(activeIndex.value + 1, options.value.length - 1)
    scrollActiveIntoView()
    return
  }

  if (event.key === 'ArrowUp') {
    event.preventDefault()
    activeIndex.value = Math.max(activeIndex.value - 1, 0)
    scrollActiveIntoView()
    return
  }

  if (event.key === 'Enter' && activeIndex.value >= 0) {
    const item = options.value[activeIndex.value]
    if (item) {
      event.preventDefault()
      toggle(item)
    }
  }
}

function scrollActiveIntoView(): void {
  nextTick(() => {
    const list = popoverRef.value?.querySelector<HTMLElement>('.ms-list')
    const option = popoverRef.value?.querySelector<HTMLElement>(
      `[data-active-index="${activeIndex.value}"]`,
    )
    if (!list || !option) return

    // Cuộn CHỈ trong danh sách — scrollIntoView trên panel position:fixed
    // vẫn có thể kéo theo trang phía sau (hiện tượng "kéo cột" khi chọn).
    const listRect = list.getBoundingClientRect()
    const optionRect = option.getBoundingClientRect()
    if (optionRect.top < listRect.top) {
      list.scrollTop -= listRect.top - optionRect.top
    } else if (optionRect.bottom > listRect.bottom) {
      list.scrollTop += optionRect.bottom - listRect.bottom
    }
  })
}

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

  // Danh mục học được: mọi mục tự nhập đều được phát cho view để lưu bền.
  emit('custom-added', value)

  custom.value = ''
}

function usageFor(item: string): number {
  if (!props.counts) return 0
  return props.counts[normalizeText(item)] ?? 0
}

function openPopover(): void {
  if (props.disabled) return

  open.value = true
  query.value = ''
  debouncedQuery.value = ''
  nextTick(() => {
    place()
    activeIndex.value = 0
  })
}

/**
 * Đặt vị trí panel: đo chiều cao THẬT sau render thay vì ước lượng 430, rồi
 * chặn panel luôn nằm trọn viewport. Bug cũ: trigger thấp gần đáy màn hình thì
 * panel mở xuôi tràn ra ngoài — hàng "Thêm giấy tờ khác" + hint chìm dưới đáy.
 */
function place(): void {
  const trigger = triggerRef.value
  const panel = popoverRef.value
  if (!open.value || !trigger || !panel) return

  const rect = trigger.getBoundingClientRect()
  const width = Math.min(390, window.innerWidth - 24)
  const left = Math.min(Math.max(12, rect.left), Math.max(12, window.innerWidth - width - 12))

  const height = panel.offsetHeight || 430
  const gap = 8
  const spaceBelow = window.innerHeight - rect.bottom
  const spaceAbove = rect.top

  let top: number
  if (spaceBelow < height && spaceAbove > spaceBelow) {
    dropUp.value = true
    top = rect.top - gap
  } else {
    dropUp.value = false
    top = rect.bottom + gap
  }

  if (dropUp.value) {
    // translateY(-100%): mép hiển thị = top - height. Chặn để panel trọn viewport.
    top = Math.min(Math.max(top, height + 12), window.innerHeight - 12)
  } else {
    top = Math.max(12, Math.min(top, window.innerHeight - height - 12))
  }

  position.value = { top, left, width }
}

// Chọn/bỏ chọn một mục làm chips lớn ra → layout dịch → panel fixed đứng yên
// ở toạ độ cũ trông như "tách rời, để trống". Mồi lại vị trí theo trigger mới.
watch(
  () => props.modelValue.length,
  () => {
    if (open.value) nextTick(place)
  },
)

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
            placeholder="Tìm giấy tờ... (↑↓ + Enter)"
            autofocus
            @input="onSearchInput"
            @keydown="onListKeydown"
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

        <div class="ms-list" @keydown="onListKeydown">
          <p v-if="options.length === 0" class="ms-empty">Không có mục phù hợp.</p>
          <label
            v-for="(item, index) in options"
            :key="item"
            class="ms-option"
            :class="{ checked: selectedKeys.has(normalizeText(item)), active: index === activeIndex }"
            :data-active-index="index"
            @mousemove="activeIndex = index"
            @click="activeIndex = index"
          >
            <input
              type="checkbox"
              :checked="selectedKeys.has(normalizeText(item))"
              @change="toggle(item)"
            />
            <span class="ms-checkbox">
              <Check :size="12" />
            </span>
            <span class="ms-option-text">
              <template v-for="(part, partIndex) in highlightParts(item)" :key="partIndex">
                <mark v-if="part.hit">{{ part.text }}</mark>
                <template v-else>{{ part.text }}</template>
              </template>
            </span>
            <span v-if="usageFor(item) > 0" class="ms-usage" title="Số lần gặp trong dữ liệu thật">
              ×{{ usageFor(item) }}
            </span>
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
          Đã chọn {{ modelValue.length }} mục — {{ options.length }} kết quả.
          Gõ “kết hôn” sẽ ra “chứng nhận kết hôn”, “giấy kết hôn”… Bấm từng mục để thêm hoặc bỏ.
        </div>
      </div>
    </Teleport>
  </div>
</template>
