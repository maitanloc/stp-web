<script setup lang="ts">
import { computed, nextTick, onMounted, onUnmounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ListChecks, Pin, Search } from '@lucide/vue'
import { request } from '../api/client'
import type { DossierSearchHitResponse } from '../api/types'
import { useTheme } from '../composables/useTheme'
import { allTools, isPinned, togglePin, type ToolDef } from '../composables/useToolRegistry'

/**
 * Ctrl+K command palette (H2): kiểu Google Sheets help — gõ để tìm hồ sơ
 * (theo số / trích yếu), nhảy tool, chạy lệnh nhanh. Không bọc Transition
 * (v-if thuần) — mở tức thì, không kẹt rAF.
 */

const router = useRouter()
const route = useRoute()
const { theme, toggleTheme } = useTheme()

const open = ref(false)
const query = ref('')
const hits = ref<DossierSearchHitResponse[]>([])
const searching = ref(false)
const activeIndex = ref(0)
const inputElement = ref<HTMLInputElement | null>(null)

// ---------------------------- Lệnh nhanh ----------------------------

interface CommandDef {
  label: string
  hint: string
  run: () => void
}

const currentTool = computed<ToolDef | null>(() => {
  const path = route.path
  return (
    allTools.value.find((tool) => tool.to === path) ??
    allTools.value.find((tool) => tool.to !== '/' && path.startsWith(`${tool.to}/`)) ??
    null
  )
})

const commands = computed<CommandDef[]>(() => {
  const list: CommandDef[] = [
    {
      label: theme.value === 'dark' ? 'Chuyển giao diện sáng' : 'Chuyển giao diện tối',
      hint: 'lệnh',
      run: () => toggleTheme(),
    },
    {
      label: document.documentElement.classList.contains('large-text')
        ? 'Tắt chữ lớn'
        : 'Bật chữ lớn cho dễ đọc',
      hint: 'lệnh',
      run: () => {
        const enabled = document.documentElement.classList.toggle('large-text')
        try {
          localStorage.setItem('stp.largeText', enabled ? '1' : '0')
        } catch {
          // Bỏ qua.
        }
      },
    },
  ]
  const tool = currentTool.value
  if (tool) {
    list.push({
      label: isPinned(tool) ? `Bỏ ghim "${tool.label}"` : `Ghim "${tool.label}" lên đầu sidebar`,
      hint: 'lệnh',
      run: () => togglePin(tool),
    })
  }
  return list
})

// ---------------------------- Danh sách kết quả ----------------------------

interface PaletteEntry {
  key: string
  kind: 'command' | 'tool' | 'dossier'
  title: string
  hint: string
  detail?: string
  run: () => void
}

function normalize(text: string): string {
  return text
    .toLowerCase()
    .normalize('NFD')
    .replace(/[\u0300-\u036f]/g, '')
    .replace(/đ/g, 'd')
}

const commandEntries = computed<PaletteEntry[]>(() =>
  commands.value
    .filter((command) => !needle.value || normalize(command.label).includes(needle.value))
    .map((command, index) => ({
      key: `cmd-${index}`,
      kind: 'command' as const,
      title: command.label,
      hint: command.hint,
      run: command.run,
    })),
)

const toolEntries = computed<PaletteEntry[]>(() =>
  allTools.value
    .filter((tool) => !needle.value || normalize(tool.label).includes(needle.value))
    .map((tool) => ({
      key: tool.to,
      kind: 'tool' as const,
      title: tool.label,
      hint: 'công cụ',
      run: () => router.push(tool.to),
    })),
)

const needle = computed(() => normalize(query.value.trim()))

const dossierEntries = computed<PaletteEntry[]>(() =>
  hits.value.map((hit) => ({
    key: hit.id,
    kind: 'dossier' as const,
    title: `Hồ sơ ${hit.numberLabel} · ${hit.year}`,
    hint: hit.type || 'hồ sơ',
    detail: hit.originalSummary,
    run: () => router.push(`/ho-so/${hit.id}`),
  })),
)

const groups = computed(() => {
  const list: Array<{ label: string; entries: PaletteEntry[] }> = []
  if (commandEntries.value.length > 0) list.push({ label: 'Lệnh', entries: commandEntries.value })
  if (toolEntries.value.length > 0) list.push({ label: 'Công cụ', entries: toolEntries.value })
  if (dossierEntries.value.length > 0) list.push({ label: 'Hồ sơ', entries: dossierEntries.value })
  return list
})

const flatEntries = computed<PaletteEntry[]>(() => groups.value.flatMap((group) => group.entries))

const activeEntry = computed<PaletteEntry | null>(
  () => flatEntries.value[activeIndex.value] ?? null,
)

// ---------------------------- Tìm hồ sơ (debounce) ----------------------------

let searchToken = 0
let searchTimer: number | undefined

watch(query, (value) => {
  const token = ++searchToken
  activeIndex.value = 0
  if (searchTimer) window.clearTimeout(searchTimer)
  const term = value.trim()
  if (term.length < 2) {
    hits.value = []
    searching.value = false
    return
  }
  searching.value = true
  searchTimer = window.setTimeout(async () => {
    try {
      const data = await request<DossierSearchHitResponse[]>(
        `/api/dossiers/search?q=${encodeURIComponent(term)}`,
      )
      if (token === searchToken) {
        hits.value = data
        searching.value = false
      }
    } catch {
      if (token === searchToken) {
        hits.value = []
        searching.value = false
      }
    }
  }, 250)
})

// ---------------------------- Điều khiển ----------------------------

function openPalette(): void {
  query.value = ''
  hits.value = []
  activeIndex.value = 0
  open.value = true
  void nextTick(() => inputElement.value?.focus())
}

function closePalette(): void {
  open.value = false
}

function togglePalette(): void {
  if (open.value) closePalette()
  else openPalette()
}

function runEntry(entry: PaletteEntry): void {
  closePalette()
  entry.run()
}

function runActive(): void {
  const entry = activeEntry.value ?? flatEntries.value[0]
  if (entry) runEntry(entry)
}

function move(delta: number): void {
  const total = flatEntries.value.length
  if (total === 0) return
  activeIndex.value = (activeIndex.value + delta + total) % total
}

function onKeyDown(event: KeyboardEvent): void {
  const key = event.key.toLowerCase()
  if ((event.ctrlKey || event.metaKey) && !event.altKey && !event.shiftKey && key === 'k') {
    event.preventDefault()
    togglePalette()
    return
  }
  if (!open.value) return
  if (event.key === 'Escape') {
    event.preventDefault()
    closePalette()
  } else if (event.key === 'ArrowDown') {
    event.preventDefault()
    move(1)
  } else if (event.key === 'ArrowUp') {
    event.preventDefault()
    move(-1)
  } else if (event.key === 'Enter') {
    event.preventDefault()
    runActive()
  }
}

onMounted(() => window.addEventListener('keydown', onKeyDown))
onUnmounted(() => window.removeEventListener('keydown', onKeyDown))

defineExpose({ openPalette, togglePalette })
</script>

<template>
  <div v-if="open" class="palette-overlay" @mousedown.self="closePalette()">
    <div class="palette" role="dialog" aria-modal="true" aria-label="Thanh lệnh nhanh">
      <div class="palette-head">
        <Search :size="17" class="palette-search-icon" />
        <input
          ref="inputElement"
          v-model="query"
          type="text"
          class="palette-input"
          placeholder="Tìm hồ sơ theo số / trích yếu, nhảy tool, chạy lệnh…"
          autocomplete="off"
          spellcheck="false"
        />
        <button type="button" class="palette-esc" title="Đóng (Esc)" @click="closePalette()">
          Esc
        </button>
      </div>

      <div class="palette-body">
        <p v-if="searching" class="palette-note">Đang tìm hồ sơ…</p>
        <template v-for="group in groups" :key="group.label">
          <div class="palette-group">{{ group.label }}</div>
          <button
            v-for="entry in group.entries"
            :key="entry.key"
            type="button"
            class="palette-row"
            :class="{ 'is-active': activeEntry?.key === entry.key && activeEntry?.kind === entry.kind }"
            :data-entry-key="entry.key"
            @mouseenter="activeIndex = flatEntries.indexOf(entry)"
            @click="runEntry(entry)"
          >
            <span class="palette-row-icon">
              <ListChecks v-if="entry.kind === 'tool'" :size="15" />
              <Pin v-else-if="entry.kind === 'command'" :size="15" />
              <Search v-else :size="15" />
            </span>
            <span class="palette-row-main">
              <span class="palette-row-title">{{ entry.title }}</span>
              <span v-if="entry.detail" class="palette-row-detail">{{ entry.detail }}</span>
            </span>
            <span class="palette-row-hint">{{ entry.hint }}</span>
          </button>
        </template>
        <p
          v-if="!searching && flatEntries.length === 0"
          class="palette-note"
        >
          Không có kết quả cho "{{ query.trim() }}" — thử số hồ sơ hoặc vài chữ trong trích yếu.
        </p>
      </div>

      <div class="palette-foot">
        <span><kbd>↑</kbd><kbd>↓</kbd> di chuyển</span>
        <span><kbd>Enter</kbd> mở</span>
        <span><kbd>Esc</kbd> đóng</span>
        <span class="grow"></span>
        <span>Ctrl+K để mở lại bất cứ lúc nào</span>
      </div>
    </div>
  </div>
</template>
