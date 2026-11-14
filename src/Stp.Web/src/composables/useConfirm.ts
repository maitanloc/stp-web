import { ref } from 'vue'

export interface ConfirmDialogOptions {
  title?: string
  message: string
  confirmText?: string
  cancelText?: string
  variant?: 'primary' | 'danger' | 'warning' | 'info'
  icon?: string
}

export interface AlertDialogOptions {
  title?: string
  message: string
  okText?: string
  variant?: 'primary' | 'warning' | 'info'
}

interface DialogState extends ConfirmDialogOptions {
  isOpen: boolean
  isAlert?: boolean
  resolve: (value: boolean) => void
}

const activeDialog = ref<DialogState | null>(null)

export function useConfirm() {
  function confirmDialog(options: ConfirmDialogOptions | string): Promise<boolean> {
    const opts: ConfirmDialogOptions = typeof options === 'string' ? { message: options } : options
    return new Promise<boolean>((resolve) => {
      activeDialog.value = {
        ...opts,
        title: opts.title ?? 'Xác nhận thao tác',
        confirmText: opts.confirmText ?? 'Xác nhận',
        cancelText: opts.cancelText ?? 'Huỷ',
        variant: opts.variant ?? 'primary',
        isOpen: true,
        resolve,
      }
    })
  }

  function alertDialog(options: AlertDialogOptions | string): Promise<void> {
    const opts: AlertDialogOptions = typeof options === 'string' ? { message: options } : options
    return new Promise<void>((resolve) => {
      activeDialog.value = {
        message: opts.message,
        title: opts.title ?? 'Thông báo',
        confirmText: opts.okText ?? 'Đã hiểu',
        variant: opts.variant ?? 'info',
        isAlert: true,
        isOpen: true,
        resolve: () => resolve(),
      }
    })
  }

  function accept() {
    if (!activeDialog.value) return
    const res = activeDialog.value.resolve
    activeDialog.value.isOpen = false
    activeDialog.value = null
    res(true)
  }

  function cancel() {
    if (!activeDialog.value) return
    const res = activeDialog.value.resolve
    activeDialog.value.isOpen = false
    activeDialog.value = null
    res(false)
  }

  return {
    activeDialog,
    confirmDialog,
    alertDialog,
    accept,
    cancel,
  }
}

