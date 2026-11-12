// DTOs mirror Stp.Contracts (camelCase JSON).

export interface HealthResponse {
  status: string
  database: string
  databaseName: string
  utc: string
}

export type WorkbookKind =
  | 'Unknown'
  | 'StandardWithPrintSheets'
  | 'MlOnly'
  | 'Other'

export type JobStatus = 'Queued' | 'Running' | 'Completed' | 'Failed' | 'Cancelled'

export type JobType = 'Convert' | 'Merge' | 'Sync' | 'ExportPdf'

export type OriginalFileMode = 'Copy' | 'InPlaceWithBackup' | 'OutputFolder'

export interface SheetInspectionResponse {
  name: string
  isMl: boolean
  isPrint: boolean
  year: number | null
  lastRow: number
  lastColumn: number
  blockCount: number
}

export interface YearInspectionResponse {
  year: number
  sheetName: string
  dossierCount: number
  minNumber: number
  maxNumber: number
  hasPrintSheet: boolean
  sourceSheets: string[]
}

export interface InspectionResponse {
  fileName: string
  fileSizeBytes: number
  kind: WorkbookKind
  hasMlSheet: boolean
  mlSheetName: string | null
  hasPrintSheets: boolean
  hasMacro: boolean
  sheetCount: number
  yearCount: number
  dossierCount: number
  printReadyCount: number
  totalPages: number
  types: string[]
  years: YearInspectionResponse[]
  sheets: SheetInspectionResponse[]
  warnings: string[]
}

export interface WorkbookResponse {
  id: string
  name: string
  sourceFileName: string
  kind: WorkbookKind
  importedAt: string
  lastSyncedAt: string | null
  version: number
  isMaster: boolean
  notes: string | null
  yearCount: number
  dossierCount: number
}

export interface YearResponse {
  id: string
  year: number
  sheetName: string
  dossierCount: number
}

export interface DossierSummaryResponse {
  id: string
  year: number
  mlRow: number
  number: number
  numberLabel: string
  type: string
  group: string
  boxNumber: string
  originalSummary: string
  hasPrintLayout: boolean
  isAggregate: boolean
  totalPages: number
  documentCount: number
  /** J2: số ghi chú nhóm trên dòng này (0 nếu chưa ai comment). */
  commentCount: number
}

/** H2: kết quả tìm nhanh hồ sơ cho thanh lệnh Ctrl+K. */
export interface DossierSearchHitResponse {
  id: string
  year: number
  numberLabel: string
  type: string
  originalSummary: string
}

/** J2: một ghi chú nhóm trên dòng hồ sơ (bảng sống). */
export interface DossierCommentResponse {
  id: string
  dossierId: string
  actor: string
  body: string
  createdAt: string
}

/** J2: lịch sử giá rẻ của một hồ sơ — sync gần nhất + comment gần nhất. */
export interface DossierHistoryResponse {
  lastSyncedAt: string | null
  lastSyncChange: string | null
  lastSyncFile: string | null
  lastSyncActor: string | null
  lastCommentAt: string | null
  lastCommentActor: string | null
}

export interface DocumentLineResponse {
  stt: number
  soKyHieu: string
  ngay: string
  trichYeu: string
  tacGia: string
  toSo: string
  ghiChu: string
}

export interface DossierResponse {
  id: string
  workbookId: string
  year: number
  mlRow: number
  number: number
  numberLabel: string
  title: string
  group: string
  type: string
  boxNumber: string
  subNumber: string
  originalSummary: string
  numberNote: string
  sourceVolume: string
  hasPrintLayout: boolean
  isAggregate: boolean
  totalPages: number
  fullText: string | null
  note: string | null
  rowVersion: string
  updatedAt: string
  documents: DocumentLineResponse[]
}

export interface PagedResponse<T> {
  items: T[]
  total: number
  page: number
  pageSize: number
}

export interface ConversionOptionsResponse {
  originalFileMode: OriginalFileMode
  createZip: boolean
  mergeOutput: boolean
  mergeOnly: boolean
  generatePrintSheets: boolean
  injectMacro: boolean
  headerText: string | null
  footerText: string | null
}

export interface JobSummaryResponse {
  id: string
  type: JobType
  status: JobStatus
  totalItems: number
  succeededItems: number
  failedItems: number
  errorMessage: string | null
  createdAt: string
  startedAt: string | null
  completedAt: string | null
  outputFileName: string | null
  canDownload: boolean
}

export interface JobItemResponse {
  id: string
  inputFileName: string
  status: JobStatus
  workbookKind: WorkbookKind
  dossierCount: number
  errorMessage: string | null
  startedAt: string | null
  completedAt: string | null
  canDownload: boolean
}

export interface JobLogResponse {
  timestamp: string
  level: string
  message: string
  details: string | null
}

export interface JobDetailResponse {
  summary: JobSummaryResponse
  options: ConversionOptionsResponse
  items: JobItemResponse[]
  logs: JobLogResponse[]
}

export interface JobCreatedResponse {
  id: string
  status: string
  message: string
}

export interface SyncResponse {
  success: boolean
  syncSessionId: string
  workbookId: string | null
  addedCount: number
  updatedCount: number
  deletedCount: number
  conflictCount: number
  errorMessage: string | null
}

export interface SyncSessionSummaryResponse {
  id: string
  workbookId: string | null
  sourceFileName: string
  uploadedAt: string
  status: string
  addedCount: number
  updatedCount: number
  deletedCount: number
  conflictCount: number
  errorMessage: string | null
}

export interface SyncChangeResponse {
  id: string
  entityType: string
  entityKey: string
  changeType: string
  isConflict: boolean
  resolution: string | null
  oldValueJson: string | null
  newValueJson: string | null
}

export interface SyncSessionDetailResponse {
  summary: SyncSessionSummaryResponse
  changes: SyncChangeResponse[]
}

export interface OverviewResponse {
  workbookCount: number
  dossierCount: number
  yearCount: number
  typeCount: number
  jobCount: number
  runningJobCount: number
  syncSessionCount: number
  lastSyncAt: string | null
  excelHost: string
  excelPath: string | null
  excelAvailable: boolean
  databaseConnected: boolean
  serverTime: string
  recentJobs: JobSummaryResponse[]
  recentWorkbooks: WorkbookResponse[]
}

// ---------------------------- Developer ----------------------------

export interface DeveloperAuthStatusResponse {
  setupRequired: boolean
  configured: boolean
  source: 'AppSettings' | 'LocalFile' | 'None' | string
}

export interface DeveloperSessionResponse {
  token: string
  expiresAt: string
  username: string
}

export interface DeveloperMeResponse {
  username: string
  role: string
  expiresAt: string | null
}

export interface MacroVersionSummaryResponse {
  id: string
  name: string
  versionNumber: number
  codeHash: string
  changeNote: string | null
  createdBy: string
  createdAt: string
  isActive: boolean
  parentVersionId: string | null
  lineCount: number
}

export interface MacroVersionDetailResponse extends MacroVersionSummaryResponse {
  code: string
}

export interface MacroValidationResponse {
  isValid: boolean
  errors: string[]
  warnings: string[]
}

export interface MacroDiffLineResponse {
  kind: 'same' | 'added' | 'removed'
  oldLineNumber: number | null
  newLineNumber: number | null
  text: string
}

export interface MacroDiffResponse {
  newVersionId: string
  oldVersionId: string | null
  added: number
  removed: number
  unchanged: number
  lines: MacroDiffLineResponse[]
}

export interface SystemInfoResponse {
  appVersion: string
  environment: string
  serverTime: string
  storageRoot: string
  excelHost: string
  excelPath: string | null
  excelAvailable: boolean
  databaseConnected: boolean
  activeMacro: MacroVersionSummaryResponse | null
  workbookCount: number
  dossierCount: number
  jobCount: number
  syncSessionCount: number
}

// Công cụ bổ sung trích yếu.

export interface SummaryYearResponse {
  year: number
  count: number
}

export interface SummaryTypeResponse {
  name: string
  count: number
}

export interface SummaryInspectResponse {
  sessionId: string
  fileName: string
  fileSizeBytes: number
  sheetName: string
  summaryColumn: string
  summaryHeader: string | null
  sheetNumberColumn: string
  sheetNumberHeader: string | null
  documentNumberColumn: string
  rowCount: number
  existingListCount: number
  brokenSummaryCount: number
  formulaSummaryCount: number
  missingSheetNumberCount: number
  missingDocumentNumberCount: number
  missingNumberCount: number
  normalizableCount: number
  catalog: string[]
  catalogMeta?: SummaryCatalogMetaResponse[]
  years: SummaryYearResponse[]
  types: SummaryTypeResponse[]
  numberColumn?: string | null
  /** Các sheet dữ liệu tạo nên phiên (file nhập + sheet năm); 1 mục = bản cũ một sheet. */
  sheets?: SummarySheetResponse[]
}

export interface SummarySheetResponse {
  name: string
  /** null = sheet "file nhập"; số = sheet mang tên năm. */
  year: number | null
  rowCount: number
}

export interface SummaryCatalogMetaResponse {
  name: string
  usageCount: number
  source: 'user' | 'learned' | 'file' | 'default'
}

export interface SummaryCatalogItem {
  id: string
  displayName: string
  usageCount: number
  source: 'user' | 'learned'
  lastUsedAt: string
  dossierTypeHint: string | null
}

export interface SummaryRowResponse {
  rowNumber: number
  year: number | null
  numberLabel: string
  documentNumber: string
  sheetNumber: string
  summary: string
  baseSummary: string
  items: string[]
  hasDocumentList: boolean
  hasFormula: boolean
  isBrokenSummary: boolean
  group: string | null
  type: string | null
  needsNormalization: boolean
  normalizedSummary: string
  normalizedBaseSummary: string
  /** Sheet nguồn của dòng (riêng biệt khi phiên ghép nhiều sheet). */
  sheet?: string
  /** Số dòng Excel thật trong sheet nguồn; bản một sheet thì bằng rowNumber. */
  sourceRowNumber?: number
}

export interface SummaryRowsResponse {
  items: SummaryRowResponse[]
  total: number
  page: number
  pageSize: number
}

export interface SummaryChangeRequest {
  rowNumber: number
  items: string[]
  summaryText?: string | null
  sheetNumber?: string | null
  documentNumber?: string | null
  numberLabel?: string | null
}

export interface SummaryYearReportResponse {
  year: number
  total: number
  missingSheetNumber: number
  missingDocumentNumber: number
  missingNumber: number
  needsNormalization: number
}

export interface SummarySessionInfoResponse {
  sessionId: string
  fileName: string
  fileSizeBytes: number
  createdAt: string
  rowCount: number
}

export interface SyncResponse {
  success: boolean
  syncSessionId: string
  workbookId: string | null
  addedCount: number
  updatedCount: number
  deletedCount: number
  conflictCount: number
  errorMessage: string | null
}

export interface SummaryPreviewRowResponse {
  rowNumber: number
  summary: string
  changed: boolean
  originalSummary: string
  sheetNumber: string | null
  originalSheetNumber: string | null
  summaryChanged: boolean
  sheetNumberChanged: boolean
  documentNumber?: string | null
  originalDocumentNumber?: string | null
  documentNumberChanged?: boolean
  numberLabel?: string | null
  originalNumberLabel?: string | null
  numberLabelChanged?: boolean
}

export interface SummaryExportRequest {
  changes: SummaryChangeRequest[]
  outputFileName?: string | null
  onlyRowNumbers?: number[] | null
}

export interface SummaryPreviewRequest {
  changes: SummaryChangeRequest[]
  onlyRowNumbers?: number[] | null
}

export type SummaryNumberMode = 'all' | 'range' | 'single'

export interface SummaryScopeRequest {
  year: number | null
  type: string | null
  search: string | null
  onlyExisting: boolean
  numberMode: SummaryNumberMode
  numberFrom: number | null
  numberTo: number | null
  number: number | null
  yearFrom: number | null
  yearTo: number | null
  onlyMissingSheetNumber: boolean
  onlyMissingDocumentNumber: boolean
  onlyMissingNumber: boolean
  missingSheetNumberFirst: boolean
  onlyNeedsNormalization: boolean
}

export interface SummaryFilteredExportRequest {
  changes: SummaryChangeRequest[]
  scope: SummaryScopeRequest
  outputFileName?: string | null
}

export interface SummaryFilteredPreviewResponse {
  totalRows: number
  changedRows: number
  ignoredChanges: number
  normalizedRows: number
  truncated: boolean
  items: SummaryPreviewRowResponse[]
}

export interface SummaryPrintPreviewRequest {
  changes: SummaryChangeRequest[]
  years?: number[] | null
}

export interface SummaryPrintYearStatusResponse {
  year: number
  total: number
  ready: number
  missingNumber: number
  missingDocumentCode: number
  missingDate: number
  missingSheetNumber: number
  missingSummary: number
}

export interface SummaryPrintPreviewResponse {
  years: SummaryPrintYearStatusResponse[]
  totalRows: number
  hasIssues: boolean
}

export type SummaryPrintMode = 'perYear' | 'combined'

export interface SummaryPrintExportRequest {
  changes: SummaryChangeRequest[]
  years?: number[] | null
  mode: SummaryPrintMode
  allowIncomplete: boolean
  outputFileName?: string | null
}

// ===== Nền tảng nhóm: presence / chat / nhật ký hoạt động =====

export interface PresencePingRequest {
  actor: string
  where: string
  sessionId?: string | null
  rowNumber?: number | null
}

export interface PresenceEntryResponse {
  actor: string
  where: string
  lastSeenSecondsAgo: number
}

export interface PresenceRowMarkResponse {
  sessionId: string
  rowNumber: number
  actor: string
  lastSeenSecondsAgo: number
}

export interface PresencePingResponse {
  online: PresenceEntryResponse[]
  rowMarks: PresenceRowMarkResponse[]
}

export interface ChatMessagesResponse {
  items: ChatMessageResponse[]
}

export interface ChatMessageResponse {
  id: string
  actor: string
  body: string
  createdAt: string
}

/** G4: page chat có cursor — nextCursor (iso tin cũ nhất) + hasMore để lật page cũ. */
export interface ChatMessagesResponse {
  items: ChatMessageResponse[]
  nextCursor?: string | null
  hasMore?: boolean
}

/** G3: mốc seen chat server-side + số tin chưa đọc theo mốc đó. */
export interface ChatSeenResponse {
  lastReadAt: string
  unreadCount: number
}

/** G1: một thông báo của bell topbar. */
export interface NotificationItemResponse {
  id: string
  kind: string
  title: string
  body: string
  link: string | null
  createdAt: string
  isRead: boolean
}

export interface NotificationsResponse {
  items: NotificationItemResponse[]
  unreadCount: number
}

export interface ActivityEventsResponse {
  items: ActivityEventResponse[]
}

export interface ActivityEventResponse {
  id: string
  actor: string
  action: string
  entityType: string | null
  entityId: string | null
  details: string | null
  timestamp: string
}

