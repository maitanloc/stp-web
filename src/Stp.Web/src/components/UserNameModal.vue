<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { setUserName } from '../api/client'

const props = defineProps<{ open: boolean }>()
const emit = defineEmits<{ saved: [name: string]; closed: [] }>()

const draft = ref('')
const error = ref<string | null>(null)
const inputElement = ref<HTMLInputElement | null>(null)

const trimmed = computed(() => draft.value.trim())

function submit(): void {
  if (trimmed.value.length < 2) {
    error.value = 'Tên cần tối thiểu 2 ký tự.'
    return
  }
  if (trimmed.value.length > 100) {
    error.value = 'Tên tối đa 100 ký tự.'
    return
  }
  error.value = null
  setUserName(trimmed.value)
  draft.value = ''
  emit('saved', trimmed.value)
}

onMounted(() => {
  inputElement.value?.focus()
})
</script>

<template>
  <div v-if="open" class="modal-backdrop" @click.self="emit('closed')">
    <div class="modal-card" role="dialog" aria-modal="true" aria-label="Khai tên">
      <form @submit.prevent="submit()">
        <h2>Mình gọi bạn là gì?</h2>
        <p class="hint">
          Tên này hiện trên chat, nhật ký và dấu dòng đang sửa của bạn cho cả nhóm.<br />
          Gõ một lần là máy nhớ, không cần mật khẩu.
        </p>
        <input
          ref="inputElement"
          v-model="draft"
          type="text"
          maxlength="100"
          autocomplete="off"
          placeholder="VD: Lan, Minh Q., Tú Kho..."
          @keydown.enter="submit()"
        />
        <p v-if="error" class="error">{{ error }}</p>
        <div class="actions">
          <button type="button" class="ghost" @click="emit('closed')">Để sau</button>
          <button type="submit" class="primary" :disabled="trimmed.length < 2">
            Dùng tên này
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

input[type='text'] {
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

input[type='text']:focus {
  border-color: var(--brand);
  box-shadow: 0 0 0 3px var(--brand-soft);
}

.error {
  margin: 8px 0 0;
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
