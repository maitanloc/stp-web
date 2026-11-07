import { ref, watch } from 'vue'

export type Theme = 'light' | 'dark'

const STORAGE_KEY = 'stp.theme'

function resolveInitialTheme(): Theme {
  try {
    const stored = localStorage.getItem(STORAGE_KEY)
    if (stored === 'light' || stored === 'dark') return stored
  } catch {
    // localStorage bị chặn: dùng theo hệ điều hành.
  }

  if (typeof window !== 'undefined' && window.matchMedia('(prefers-color-scheme: dark)').matches) {
    return 'dark'
  }

  return 'light'
}

const theme = ref<Theme>(resolveInitialTheme())

function applyTheme(value: Theme): void {
  if (typeof document !== 'undefined') {
    document.documentElement.dataset.theme = value
  }
  try {
    localStorage.setItem(STORAGE_KEY, value)
  } catch {
    // Bỏ qua khi không ghi được localStorage.
  }
}

watch(theme, applyTheme, { immediate: true })

export function useTheme() {
  function setTheme(value: Theme): void {
    theme.value = value
  }

  function toggleTheme(): void {
    theme.value = theme.value === 'dark' ? 'light' : 'dark'
  }

  return { theme, setTheme, toggleTheme }
}
