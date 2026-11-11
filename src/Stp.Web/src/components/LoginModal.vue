<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { errorMessage } from '../lib/errors'
import { loginTeamIdentity } from '../composables/useTeamIdentity'

const props = defineProps<{ open: boolean; required?: boolean }>()
const emit = defineEmits<{ logged: [displayName: string]; closed: [] }>()

const username = ref('')
const password = ref('')
const error = ref<string | null>(null)
const busy = ref(false)
const usernameElement = ref<HTMLInputElement | null>(null)

async function submit(): Promise<void> {
  if (busy.value) return
  const user = username.value.trim()
  if (user.length === 0 || password.value.length === 0) {
    error.value = 'Nhập cả tên đăng nhập và mật khẩu.'
    return
  }

  busy.value = true
  error.value = null
  try {
    const identity = await loginTeamIdentity(user, password.value)
    password.value = ''
    username.value = ''
    emit('logged', identity.displayName)
  } catch (caught) {
    error.value = errorMessage(caught)
  } finally {
    busy.value = false
  }
}

onMounted(() => {
  usernameElement.value?.focus()
})
</script>

<template>
  <div v-if="open" class="modal-backdrop" @click.self="emit('closed')">
    <div class="modal-card" role="dialog" aria-modal="true" aria-label="Đăng nhập">
      <form @submit.prevent="submit()">
        <h2>Đăng nhập</h2>
        <p class="hint">
          Mỗi thành viên một tài khoản riêng — chat, nhật ký và dấu dòng đang sửa
          sẽ gắn đúng tên của bạn.
        </p>
        <input
          ref="usernameElement"
          v-model="username"
          type="text"
          maxlength="50"
          autocomplete="username"
          placeholder="Tên đăng nhập (vd: lan, minh.quang)"
        />
        <input
          v-model="password"
          type="password"
          maxlength="128"
          autocomplete="current-password"
          placeholder="Mật khẩu"
          class="password"
          @keydown.enter="submit()"
        />
        <p v-if="error" class="error">{{ error }}</p>
        <div class="actions">
          <button v-if="!required" type="button" class="ghost" @click="emit('closed')">Để sau</button>
          <button type="submit" class="primary" :disabled="busy">
            {{ busy ? 'Đang đăng nhập…' : 'Đăng nhập' }}
          </button>
        </div>
      </form>
    </div>
  </div>
</template>

<style scoped>
.modal-backdrop {
  position: fixed;
  inset: 0;
  background: rgba(15, 23, 42, 0.42);
  display: grid;
  place-items: center;
  z-index: 90;
}

.modal-card {
  width: min(420px, calc(100vw - 32px));
  background: var(--surface);
  border: 1px solid var(--border);
  border-radius: var(--radius-lg);
  box-shadow: var(--shadow-lg);
  padding: 26px 26px 22px;
}

h2 {
  margin: 0 0 8px;
  font-size: 19px;
  color: var(--text);
}

.hint {
  margin: 0 0 16px;
  color: var(--muted);
  font-size: 13px;
  line-height: 1.55;
}

input {
  width: 100%;
  box-sizing: border-box;
  border: 1px solid var(--border-strong);
  border-radius: var(--radius-sm);
  background: var(--surface-2);
  color: var(--text);
  padding: 10px 12px;
  font-size: 14px;
  outline: none;
}

input + input {
  margin-top: 10px;
}

input:focus {
  border-color: var(--brand);
  box-shadow: 0 0 0 3px var(--brand-soft);
}

.error {
  margin: 10px 0 0;
  color: var(--danger);
  font-size: 12.5px;
}

.actions {
  display: flex;
  justify-content: flex-end;
  gap: 10px;
  margin-top: 18px;
}

button {
  border-radius: var(--radius-sm);
  border: 1px solid transparent;
  padding: 8px 16px;
  font-size: 13.5px;
  cursor: pointer;
}

.ghost {
  background: transparent;
  color: var(--muted);
  border-color: var(--border);
}

.ghost:hover {
  color: var(--text);
}

.primary {
  background: var(--brand);
  color: #fff;
}

.primary:hover {
  background: var(--brand-strong);
}

.primary:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}
</style>
