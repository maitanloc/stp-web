import { computed, reactive } from 'vue'
import { request, setDevToken, getDevToken, ApiError } from '../api/client'
import type {
  DeveloperAuthStatusResponse,
  DeveloperMeResponse,
  DeveloperSessionResponse,
} from '../api/types'

interface DevAuthState {
  initialized: boolean
  checking: boolean
  status: DeveloperAuthStatusResponse | null
  token: string | null
  username: string | null
  expiresAt: string | null
  me: DeveloperMeResponse | null
  error: string | null
}

const state = reactive<DevAuthState>({
  initialized: false,
  checking: false,
  status: null,
  token: getDevToken(),
  username: null,
  expiresAt: null,
  me: null,
  error: null,
})

let listenerAttached = false

function attachExpiredListener(): void {
  if (listenerAttached || typeof window === 'undefined') return
  listenerAttached = true
  window.addEventListener('stp:auth-expired', () => {
    state.token = null
    state.username = null
    state.expiresAt = null
    state.me = null
  })
}

attachExpiredListener()

function applySession(session: DeveloperSessionResponse): void {
  setDevToken(session.token)
  state.token = session.token
  state.username = session.username
  state.expiresAt = session.expiresAt
  state.error = null
}

function clearSession(): void {
  setDevToken(null)
  state.token = null
  state.username = null
  state.expiresAt = null
  state.me = null
}

function messageOf(error: unknown): string {
  if (error instanceof ApiError) return error.detail || error.title
  if (error instanceof Error) return error.message
  return 'Đã xảy ra lỗi không xác định.'
}

export function useDevAuth() {
  const isAuthenticated = computed(() => Boolean(state.token))

  async function refreshStatus(): Promise<void> {
    state.checking = true
    state.error = null
    try {
      state.status = await request<DeveloperAuthStatusResponse>('/api/dev/auth/status')

      if (state.token) {
        try {
          state.me = await request<DeveloperMeResponse>('/api/dev/auth/me')
          state.username = state.me.username
          state.expiresAt = state.me.expiresAt
        } catch {
          clearSession()
        }
      }
    } catch (error) {
      state.error = messageOf(error)
    } finally {
      state.checking = false
      state.initialized = true
    }
  }

  async function login(password: string): Promise<void> {
    state.error = null
    try {
      const session = await request<DeveloperSessionResponse>('/api/dev/auth/login', {
        method: 'POST',
        body: { password },
      })
      applySession(session)
      state.me = await request<DeveloperMeResponse>('/api/dev/auth/me')
      state.status = {
        setupRequired: false,
        configured: true,
        source: state.status?.source ?? 'LocalFile',
      }
    } catch (error) {
      state.error = messageOf(error)
      throw error
    }
  }

  async function setup(password: string, confirmPassword: string): Promise<void> {
    state.error = null
    try {
      const session = await request<DeveloperSessionResponse>('/api/dev/auth/setup', {
        method: 'POST',
        body: { password, confirmPassword },
      })
      applySession(session)
      state.status = { setupRequired: false, configured: true, source: 'LocalFile' }
      state.me = await request<DeveloperMeResponse>('/api/dev/auth/me')
    } catch (error) {
      state.error = messageOf(error)
      throw error
    }
  }

  function logout(): void {
    clearSession()
  }

  return {
    state,
    isAuthenticated,
    refreshStatus,
    login,
    setup,
    logout,
    messageOf,
  }
}
