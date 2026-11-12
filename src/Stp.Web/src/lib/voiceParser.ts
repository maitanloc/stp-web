/**
 * Parser câu nói cho phiên đọc trích yếu (Nhóm I — GĐ 1, plan
 * "trich-yeu-sieu-toc"): 100% frontend, không AI — match mờ theo token
 * (bỏ dấu, bỏ hoa/thường) trên nguồn từ vựng có sẵn của phiên: catalog
 * giấy tờ + danh sách loại hồ sơ thật.
 *
 * Parser làm việc trên chuỗi ĐÃ chuẩn hoá (bỏ dấu, chữ thường) và trả
 * về TÊN HIỂN THỊ gốc từ context — nên kết quả luôn đúng cách viết trong
 * catalog. Học lỗi nghe: bản ghi {nghe → người sửa} được thay thế vào
 * câu trước khi parse (applyLearned).
 */

export interface VoiceParseContext {
  /** Danh mục giấy tờ của phiên (tên hiển thị gốc). */
  catalog: string[]
  /** Các loại hồ sơ thật của phiên (tên hiển thị gốc). */
  types: string[]
}

export type VoiceResult =
  | { kind: 'command'; command: 'commit' | 'next' | 'prev' | 'seven' }
  | { kind: 'filter'; type: string; year: number | null }
  | { kind: 'prefill'; type: string | null; items: string[]; sheetNumber: string | null }
  | null

export interface LearnedPair {
  heard: string
  fixed: string
}

// ---------------------------- Chuẩn hoá ----------------------------

export function normalizeSpeech(value: string): string {
  return value
    .normalize('NFD')
    .replace(/[\u0300-\u036f]/g, '')
    .replace(/đ/g, 'd')
    .replace(/\s+/g, ' ')
    .trim()
    .toLowerCase()
}

function tokenize(normalized: string): string[] {
  // Tách dấu câu + gạch ngang (dấu ngắt ý "kết hôn — căn cước") thành token riêng.
  return normalized
    .replace(/[,;.!?]/g, ' $& ')
    .replace(/[—–-]/g, ' , ')
    .split(/\s+/)
    .filter((token) => token.length > 0)
}

const SEGMENT_BOUNDARIES = new Set(['va', 'kem', 'co', 'them', 'voi', ','])

function splitSegments(tokens: string[]): string[][] {
  const segments: string[][] = []
  let current: string[] = []
  for (const token of tokens) {
    if (SEGMENT_BOUNDARIES.has(token)) {
      if (current.length > 0) segments.push(current)
      current = []
      continue
    }
    if (token === ',' || token === ';') continue
    current.push(token)
  }
  if (current.length > 0) segments.push(current)
  return segments
}

// ---------------------------- Số đọc từng chữ số ----------------------------

/** "không-ba-lẻ-năm" → 035: đọc TỪNG chữ số cho chắc (plan GĐ 1). */
const DIGIT_WORDS: Record<string, string> = {
  khong: '0',
  mot: '1',
  hai: '2',
  ba: '3',
  bon: '4',
  tu: '4',
  nam: '5',
  sau: '6',
  bay: '7',
  tam: '8',
  chin: '9',
}

const NUMBER_SKIP_WORDS = new Set(['le', 'muoi', 'tram', 'nghin', 'trieu', 'chuc'])

/** Đọc dãy token thành chuỗi số từng chữ số; gặp từ lạ thì dừng. */
function readDigits(tokens: string[]): { digits: string; consumed: number } {
  let digits = ''
  let consumed = 0
  for (const token of tokens) {
    if (/^\d+$/.test(token)) {
      digits += token
      consumed++
      continue
    }
    const digit = DIGIT_WORDS[token]
    if (digit) {
      digits += digit
      consumed++
      continue
    }
    if (NUMBER_SKIP_WORDS.has(token)) {
      consumed++
      continue
    }
    break
  }
  return { digits, consumed }
}

/** "hai không hai lẻ năm" → 2025 (4 chữ số) — cho lọc năm bằng miệng. */
function readFourDigitYear(tokens: string[]): number | null {
  const { digits } = readDigits(tokens)
  return /^\d{4}$/.test(digits) ? Number(digits) : null
}

/** Cụm "số tờ <số>" — trả chuỗi số giữ nguyên số 0 đứng đầu. */
function findSheetNumber(
  tokens: string[],
): { value: string; startIndex: number; endIndex: number } | null {
  for (let index = 0; index < tokens.length - 1; index++) {
    const token = tokens[index]
    const next = tokens[index + 1]
    const isSoTo = token === 'so' && next === 'to'
    const startsClause = isSoTo || token === 'to' || (token === 'so' && next === 'so')
    if (!startsClause) continue
    const rest = tokens.slice(index + (isSoTo || (token === 'so' && next === 'so') ? 2 : 1))
    const { digits, consumed } = readDigits(rest)
    if (digits.length > 0 && consumed > 0) {
      const prefixLength = isSoTo || (token === 'so' && next === 'so') ? 2 : 1
      return { value: digits, startIndex: index, endIndex: index + prefixLength + consumed }
    }
  }
  return null
}

// ---------------------------- Match mờ ----------------------------

function levenshtein(left: string, right: string): number {
  if (left === right) return 0
  if (left.length === 0) return right.length
  if (right.length === 0) return left.length
  let previous = Array.from({ length: right.length + 1 }, (_, index) => index)
  for (let i = 1; i <= left.length; i++) {
    const row = [i]
    for (let j = 1; j <= right.length; j++) {
      const cost = left[i - 1] === right[j - 1] ? 0 : 1
      row[j] = Math.min(row[j - 1] + 1, previous[j] + 1, previous[j - 1] + cost)
    }
    previous = row
  }
  return previous[right.length]
}

/** Token khớp chính xác, hoặc lệch nhẹ (levenshtein ≤ 1 với từ ≥ 5 ký tự). */
function tokenMatches(spoken: string, target: string): boolean {
  if (spoken === target) return true
  if (spoken.length >= 5 && target.length >= 5) {
    return levenshtein(spoken, target) <= 1
  }
  return false
}

/**
 * Cụm nói có phải một ĐOẠN LIÊN TIẾP của cụm gốc (hoặc bằng nguyên cụm)?
 * Khớp theo vị trí — tránh "ăn" token lân cận như cũ.
 *   spoken ⊆ catalog: "hộ khẩu" ⊆ "sổ hộ khẩu", "kết hôn" ⊆ "đăng ký kết hôn"
 *   spoken ⊇ catalog: nguyên cụm đọc đủ.
 */
function contiguousMatch(spokenTokens: string[], targetTokens: string[]): boolean {
  if (spokenTokens.length === 0 || targetTokens.length === 0) return false
  if (spokenTokens.length > targetTokens.length) return false
  const offset = targetTokens.length - spokenTokens.length
  for (let start = 0; start <= offset; start++) {
    let aligned = true
    for (let index = 0; index < spokenTokens.length; index++) {
      if (!tokenMatches(spokenTokens[index], targetTokens[start + index])) {
        aligned = false
        break
      }
    }
    if (aligned) return true
  }
  return false
}

const QUANTITY_WORDS: Record<string, number> = {
  mot: 1,
  hai: 2,
  ba: 3,
  bon: 4,
  tu: 4,
  nam: 5,
  sau: 6,
  bay: 7,
  tam: 8,
  chin: 9,
}

interface CatalogEntry {
  display: string
  tokens: string[]
}

function buildCatalog(catalog: string[]): CatalogEntry[] {
  return catalog
    .map((display) => ({ display, tokens: tokenize(normalizeSpeech(display)) }))
    .filter((entry) => entry.tokens.length > 0)
}

/**
 * Match danh mục trong MỘT đoạn nói: trả tên hiển thị + số lượng ("hai ảnh"
 * → "ảnh 3×4 ×2"). Khớp nhiều mục nhất có thể trong đoạn.
 */
function matchItemsInSegment(
  tokens: string[],
  entries: CatalogEntry[],
  usedDisplays: string[],
): Array<{ display: string; count: number }> {
  const found: Array<{ display: string; count: number }> = []
  let left = [...tokens]

  for (let pass = 0; pass < 4; pass++) {
    let best: { entry: CatalogEntry; count: number; start: number; length: number } | null = null
    for (const entry of entries) {
      if (usedDisplays.includes(normalizeSpeech(entry.display))) continue
      for (let start = 0; start < left.length; start++) {
        const length = Math.min(entry.tokens.length, left.length - start)
        if (length === 0) continue
        const window = left.slice(start, start + length)
        if (!contiguousMatch(window, entry.tokens)) continue
        if (!best || entry.tokens.length > best.entry.tokens.length) {
          const previous = start > 0 ? left[start - 1] : ''
          const count = QUANTITY_WORDS[previous] ?? 1
          best = { entry, count, start, length }
        }
      }
    }
    if (!best) break

    // Gỡ cụm đã khớp (kèm số lượng đứng ngay trước nếu có).
    const from = best.count > 1 && best.start > 0 ? best.start - 1 : best.start
    const count = best.start - from + best.length
    left.splice(from, count)
    found.push({ display: best.entry.display, count: best.count })
  }

  return found
}

// ---------------------------- Học lỗi nghe ----------------------------

/**
 * Thay thế trước khi parse: câu máy nghe sai → cụm người dùng đã sửa.
 * Chỉ thay lần đầu tiên khớp — đủ dùng cho trường hợp nói lặp lại.
 */
export function applyLearned(text: string, learned: LearnedPair[]): string {
  let normalized = normalizeSpeech(text)
  if (learned.length === 0) return normalized
  for (const pair of learned) {
    const heard = normalizeSpeech(pair.heard)
    const fixed = normalizeSpeech(pair.fixed)
    if (heard.length > 0 && fixed.length > 0 && normalized.includes(heard)) {
      normalized = normalized.replace(heard, fixed)
      break
    }
  }
  return normalized
}

// ---------------------------- Parser chính ----------------------------

function buildCommands(): Array<{ tokens: string[]; command: 'commit' | 'next' | 'prev' | 'seven' }> {
  return [
    { tokens: ['xong'], command: 'commit' },
    { tokens: ['ghi'], command: 'commit' },
    { tokens: ['dong', 'ke', 'tiep'], command: 'next' },
    { tokens: ['ke', 'tiep'], command: 'next' },
    { tokens: ['tiep'], command: 'next' },
    { tokens: ['quay', 'lai'], command: 'prev' },
    { tokens: ['ve', 'lai'], command: 'prev' },
    { tokens: ['dong', 'truoc'], command: 'prev' },
    { tokens: ['bo', 'bay'], command: 'seven' },
    { tokens: ['bon', 'bay'], command: 'seven' }, // nghe nhầm "bộ bảy"
  ]
}

function matchCommand(tokens: string[]): 'commit' | 'next' | 'prev' | 'seven' | null {
  const phrases = buildCommands()
  const joined = tokens.join(' ')
  // Lệnh cần "thâu câu": 1–3 token hoặc bắt đầu bằng lệnh.
  for (const phrase of phrases) {
    if (tokens.length <= phrase.tokens.length + 1) {
      if (joined.startsWith(phrase.tokens.join(' '))) return phrase.command
    }
  }
  // "… hộ khẩu xong" — lệnh ở cuối câu, phần trước là nội dung có thể bỏ.
  if (tokens[tokens.length - 1] === 'xong') return 'commit'
  return null
}

function matchType(segmentTokens: string[], types: string[]): string | null {
  let best: string | null = null
  let bestLength = 0
  for (const type of types) {
    const typeTokens = tokenize(normalizeSpeech(type))
    if (typeTokens.length === 0) continue
    // Hai hướng: đọc đủ nguyên loại, hoặc chỉ nói phần cuối ("kết hôn").
    const spokenCoversType = contiguousMatch(typeTokens, segmentTokens)
    const spokenInsideType = contiguousMatch(segmentTokens, typeTokens)
    if (spokenCoversType || spokenInsideType) {
      if (typeTokens.length > bestLength) {
        best = type
        bestLength = typeTokens.length
      }
    }
  }
  return best
}

/**
 * Parse một câu nói cuối (final result) thành lệnh / lọc / prefill.
 * Trả null khi không đoán được gì — UI giữ nguyên khung soạn.
 */
export function parseVoiceTranscript(text: string, context: VoiceParseContext): VoiceResult {
  const normalized = normalizeSpeech(text)
  const tokens = tokenize(normalized)
  if (tokens.length === 0) return null

  // 1) Lệnh điều khiển phiên đọc.
  const command = matchCommand(tokens)
  if (command) return { kind: 'command', command }

  // 2) "lọc <loại> [năm <số>]" — đặt bộ lọc bằng miệng.
  if (tokens[0] === 'loc' || tokens[0] === 'buoc' || tokens[0] === 'bo') {
    const rest = tokens.slice(1)
    if (rest.length > 0) {
      if (rest[0] === 'tat' && rest[1] === 'ca') {
        return { kind: 'filter', type: '', year: null }
      }
      let year: number | null = null
      let typeTokens = rest
      const yearIndex = rest.indexOf('nam')
      if (yearIndex >= 0) {
        year = readFourDigitYear(rest.slice(yearIndex + 1))
        typeTokens = rest.slice(0, yearIndex)
      }
      const matchedType = matchType(typeTokens, context.types)
      if (matchedType || year !== null) {
        return { kind: 'filter', type: matchedType ?? '', year }
      }
    }
  }

  // 3) Số tờ — tách trước để khỏi lẫn vào match mục.
  const sheetHit = findSheetNumber(tokens)
  const contentTokens = sheetHit
    ? tokens.filter((_, index) => index < sheetHit.startIndex || index >= sheetHit.endIndex)
    : tokens

  // 4) Pre-fill: đoạn đầu có thể là loại hồ sơ, các đoạn sau là giấy tờ.
  const segments = splitSegments(contentTokens)
  const entries = buildCatalog(context.catalog)
  const usedDisplays: string[] = []
  let spokenType: string | null = null
  const items: Array<{ display: string; count: number }> = []

  for (let index = 0; index < segments.length; index++) {
    const segment = segments[index]
    if (index === 0 && items.length === 0) {
      const type = matchType(segment, context.types)
      if (type && segment.length <= tokenize(normalizeSpeech(type)).length + 2) {
        spokenType = type
        continue
      }
    }
    for (const hit of matchItemsInSegment(segment, entries, usedDisplays)) {
      usedDisplays.push(normalizeSpeech(hit.display))
      items.push({ display: hit.display, count: hit.count })
    }
  }
  if (items.length === 0 && !spokenType) {
    return null
  }

  const itemStrings = items.map((item) =>
    item.count > 1 ? `${item.display} ×${item.count}` : item.display,
  )

  return {
    kind: 'prefill',
    type: spokenType,
    items: itemStrings,
    sheetNumber: sheetHit?.value ?? null,
  }
}
