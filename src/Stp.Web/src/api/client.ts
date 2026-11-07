const API_BASE = (import.meta.env.VITE_API_BASE as string | undefined)?.replace(/\/$/, '') ?? ''
const DEV_TOKEN_KEY = 'stp.dev.token'

export class ApiError extends Error {
  readonly status: number
  readonly title: string
  readonly detail: string | null
  readonly payload: unknown

  constructor(status: number, title: string, detail: string | null, payload: unknown) {
    super(detail || title)
    this.name = 'ApiError'
    this.status = status
    this.title = title
    this.detail = detail
    this.payload = payload
  }

  get isValidation(): boolean {
    return (
      this.status === 422 &&
      typeof this.payload === 'object' &&
      this.payload !== null &&
      Array.isArray((this.payload as { errors?: unknown }).errors)
    )
  }

  get validationErrors(): string[] {
    if (!this.isValidation) return []
    return ((this.payload as { errors: string[] }).errors ?? []).filter(Boolean)
  }

  get validationWarnings(): string[] {
    if (!this.isValidation) return []
    return ((this.payload as { warnings?: string[] }).warnings ?? []).filter(Boolean)
  }
}

export function getDevToken(): string | null {
  try {
    return localStorage.getItem(DEV_TOKEN_KEY)
  } catch {
    return null
  }
}

export function setDevToken(token: string | null): void {
  try {
    if (token) {
      localStorage.setItem(DEV_TOKEN_KEY, token)
    } else {
      localStorage.removeItem(DEV_TOKEN_KEY)
    }
  } catch {
    // localStorage có thể bị chặn; phiên vẫn dùng được trong bộ nhớ.
  }
}

interface RequestOptions extends Omit<RequestInit, 'body'> {
  body?: unknown
  form?: FormData
}

export async function request<T>(path: string, options: RequestOptions = {}): Promise<T> {
  const { body, form, headers: extraHeaders, ...init } = options
  const headers = new Headers(extraHeaders)

  if (body !== undefined) {
    headers.set('Content-Type', 'application/json')
  }

  if (path.startsWith('/api/dev')) {
    const token = getDevToken()
    if (token) {
      headers.set('Authorization', `Bearer ${token}`)
    }
  }

  const response = await fetch(`${API_BASE}${path}`, {
    ...init,
    headers,
    body: form ?? (body !== undefined ? JSON.stringify(body) : undefined),
  })

  if (!response.ok) {
    let payload: unknown = null
    let title = response.statusText || 'Lỗi máy chủ'
    let detail: string | null = null
    const text = await response.text()

    if (text) {
      try {
        payload = JSON.parse(text)
        const problem = payload as { title?: string; detail?: string }
        title = problem.title || title
        detail = problem.detail || null
      } catch {
        detail = text.slice(0, 500)
      }
    }

    if (
      response.status === 401 &&
      path.startsWith('/api/dev') &&
      !path.includes('/auth/login') &&
      !path.includes('/auth/setup')
    ) {
      setDevToken(null)
      window.dispatchEvent(new CustomEvent('stp:auth-expired'))
    }

    throw new ApiError(response.status, title, detail, payload)
  }

  if (response.status === 204) {
    return undefined as T
  }

  const text = await response.text()
  return (text ? JSON.parse(text) : undefined) as T
}

export function fileUrl(path: string): string {
  return `${API_BASE}${path}`
}

export function queryString(params: Record<string, string | number | boolean | null | undefined>): string {
  const search = new URLSearchParams()
  for (const [key, value] of Object.entries(params)) {
    if (value !== undefined && value !== null && value !== '') {
      search.set(key, String(value))
    }
  }
  const result = search.toString()
  return result ? `?${result}` : ''
}
