import { reactive } from 'vue'

export type ToastType = 'success' | 'error' | 'warning' | 'info'

export interface ToastItem {
  id: number
  type: ToastType
  title: string
  message?: string
}

const toasts = reactive<ToastItem[]>([])
let sequence = 0

function dismiss(id: number): void {
  const index = toasts.findIndex((toast) => toast.id === id)
  if (index >= 0) {
    toasts.splice(index, 1)
  }
}

function push(
  type: ToastType,
  title: string,
  message?: string,
  timeoutMs = type === 'error' ? 9000 : 5000,
): number {
  const id = ++sequence
  toasts.push({ id, type, title, message })

  if (timeoutMs > 0) {
    window.setTimeout(() => dismiss(id), timeoutMs)
  }

  return id
}

export function useToast() {
  return {
    toasts,
    dismiss,
    success: (title: string, message?: string) => push('success', title, message),
    error: (title: string, message?: string) => push('error', title, message),
    warning: (title: string, message?: string) => push('warning', title, message),
    info: (title: string, message?: string) => push('info', title, message),
  }
}
