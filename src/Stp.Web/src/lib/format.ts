import type {
  DocumentLineResponse,
  DossierResponse,
  DossierSummaryResponse,
  JobStatus,
  JobType,
  WorkbookKind,
} from '../api/types'

const numberFormatter = new Intl.NumberFormat('vi-VN')

const dateTimeFormatter = new Intl.DateTimeFormat('vi-VN', {
  day: '2-digit',
  month: '2-digit',
  year: 'numeric',
  hour: '2-digit',
  minute: '2-digit',
})

const dateFormatter = new Intl.DateTimeFormat('vi-VN', {
  day: '2-digit',
  month: '2-digit',
  year: 'numeric',
})

export function formatNumber(value: number | null | undefined): string {
  if (value === null || value === undefined || Number.isNaN(value)) return '0'
  return numberFormatter.format(value)
}

export function formatBytes(bytes: number | null | undefined): string {
  if (!bytes || bytes <= 0) return '0 B'
  if (bytes < 1024) return `${bytes} B`
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`
  if (bytes < 1024 * 1024 * 1024) return `${(bytes / (1024 * 1024)).toFixed(1)} MB`
  return `${(bytes / (1024 * 1024 * 1024)).toFixed(2)} GB`
}

export function formatDateTime(value: string | null | undefined): string {
  if (!value) return '—'
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return '—'
  return dateTimeFormatter.format(date)
}

export function formatDate(value: string | null | undefined): string {
  if (!value) return '—'
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return '—'
  return dateFormatter.format(date)
}

export const jobStatusLabel: Record<JobStatus, string> = {
  Queued: 'Đang chờ',
  Running: 'Đang xử lý',
  Completed: 'Hoàn tất',
  Failed: 'Thất bại',
  Cancelled: 'Đã huỷ',
}

export const jobTypeLabel: Record<JobType, string> = {
  Convert: 'Chuyển đổi',
  Merge: 'Gộp file',
  Sync: 'Đồng bộ',
  ExportPdf: 'Xuất PDF',
}

export const workbookKindLabel: Record<WorkbookKind, string> = {
  Unknown: 'Chưa xác định',
  StandardWithPrintSheets: 'Loại 1 · có sheet in',
  MlOnly: 'Loại 2 · chỉ có ML',
  Other: 'Cấu trúc khác',
}

export const originalModeLabel: Record<string, string> = {
  Copy: 'Giữ file gốc, kết quả trong thư mục outputs',
  InPlaceWithBackup: 'Ghi đè cạnh file gốc + sao lưu',
  OutputFolder: 'Chỉ ghi kết quả vào thư mục outputs',
}

export type Tone = 'neutral' | 'info' | 'success' | 'warning' | 'danger' | 'brand' | 'accent'

export function statusTone(status: string): Tone {
  switch (status) {
    case 'Completed':
    case 'Success':
    case 'success':
    case 'ok':
    case 'connected':
      return 'success'
    case 'Running':
    case 'running':
      return 'info'
    case 'Queued':
    case 'queued':
      return 'warning'
    case 'Failed':
    case 'failed':
    case 'error':
    case 'degraded':
      return 'danger'
    case 'Cancelled':
      return 'neutral'
    default:
      return 'neutral'
  }
}

export function kindTone(kind: WorkbookKind): Tone {
  switch (kind) {
    case 'StandardWithPrintSheets':
      return 'success'
    case 'MlOnly':
      return 'warning'
    case 'Other':
      return 'danger'
    default:
      return 'neutral'
  }
}

export function jobTypeTone(type: JobType): Tone {
  switch (type) {
    case 'Convert':
      return 'brand'
    case 'Merge':
      return 'accent'
    case 'Sync':
      return 'info'
    default:
      return 'neutral'
  }
}

export function numberDisplay(
  dossier: Pick<DossierSummaryResponse, 'number' | 'numberLabel'> | Pick<DossierResponse, 'number' | 'numberLabel'>,
): string {
  if (dossier.numberLabel && dossier.numberLabel.trim().length > 0) return dossier.numberLabel
  return dossier.number > 0 ? formatNumber(dossier.number) : '(không số)'
}

export function printTextLength(documents: DocumentLineResponse[] | undefined): number {
  if (!documents || documents.length === 0) return 0
  return documents.reduce(
    (total, line) =>
      total +
      (line.trichYeu?.length ?? 0) +
      (line.soKyHieu?.length ?? 0) +
      (line.ngay?.length ?? 0) +
      (line.tacGia?.length ?? 0) +
      (line.toSo?.length ?? 0) +
      (line.ghiChu?.length ?? 0),
    0,
  )
}

/** Khớp ngưỡng với PrintLayoutService phía máy chủ và bản in web cũ. */
export function printFitClass(documentCount: number, textLength: number): string {
  if (documentCount >= 12 || textLength >= 1200) return 'fit-tiny'
  if (documentCount >= 8 || textLength >= 850) return 'fit-tight'
  if (documentCount >= 4 || textLength >= 550) return 'fit-compact'
  return ''
}

export function totalPagesText(totalPages: number): string {
  return totalPages > 0
    ? `Tổng số tờ: ${formatNumber(totalPages)} tờ`
    : 'Tổng số tờ: để trống'
}

export function shortHash(hash: string | null | undefined, length = 12): string {
  if (!hash) return '—'
  return hash.length > length ? hash.slice(0, length) : hash
}

export function percent(value: number, total: number): number {
  if (!total || total <= 0) return 0
  return Math.min(100, Math.max(0, Math.round((value / total) * 100)))
}
