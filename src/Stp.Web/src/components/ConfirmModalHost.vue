<script setup lang="ts">
import { computed, nextTick, onMounted, onUnmounted, ref, watch } from 'vue'
import { AlertTriangle, HelpCircle, Info, Trash2, X } from '@lucide/vue'
import { useConfirm } from '../composables/useConfirm'

const { activeDialog, accept, cancel } = useConfirm()
const confirmBtnRef = ref<HTMLButtonElement | null>(null)

const iconComponent = computed(() => {
  if (!activeDialog.value) return HelpCircle
  if (activeDialog.value.variant === 'danger') return Trash2
  if (activeDialog.value.variant === 'warning') return AlertTriangle
  if (activeDialog.value.variant === 'info') return Info
  return HelpCircle
})

const iconClass = computed(() => {
  if (!activeDialog.value) return 'icon-primary'
  if (activeDialog.value.variant === 'danger') return 'icon-danger'
  if (activeDialog.value.variant === 'warning') return 'icon-warning'
  if (activeDialog.value.variant === 'info') return 'icon-info'
  return 'icon-primary'
})

const confirmBtnClass = computed(() => {
  if (!activeDialog.value) return 'btn-primary'
  if (activeDialog.value.variant === 'danger') return 'btn-danger'
  if (activeDialog.value.variant === 'warning') return 'btn-warning'
  return 'btn-primary'
})

watch(
  () => activeDialog.value?.isOpen,
  (open) => {
    if (open) {
      void nextTick(() => {
        confirmBtnRef.value?.focus()
      })
    }
  },
)

function onKeydown(e: KeyboardEvent) {
  if (!activeDialog.value?.isOpen) return
  if (e.key === 'Escape') {
    e.preventDefault()
    cancel()
  } else if (e.key === 'Enter') {
    e.preventDefault()
    accept()
  }
}

onMounted(() => {
  window.addEventListener('keydown', onKeydown)
})

onUnmounted(() => {
  window.removeEventListener('keydown', onKeydown)
})
</script>

<template>
  <Teleport to="body">
    <Transition name="confirm-modal-fade">
      <div
        v-if="activeDialog?.isOpen"
        class="confirm-modal-backdrop"
        role="dialog"
        aria-modal="true"
        :aria-labelledby="'confirm-title'"
        @click.self="cancel"
      >
        <div class="confirm-modal-card">
          <button
            class="confirm-modal-close"
            type="button"
            title="Đóng (Esc)"
            aria-label="Đóng"
            @click="cancel"
          >
            <X :size="16" />
          </button>

          <div class="confirm-modal-body">
            <div class="confirm-modal-icon-wrap" :class="iconClass">
              <component :is="iconComponent" :size="24" />
            </div>

            <div class="confirm-modal-content">
              <h3 id="confirm-title" class="confirm-modal-title">
                {{ activeDialog.title }}
              </h3>
              <p class="confirm-modal-message">
                {{ activeDialog.message }}
              </p>
            </div>
          </div>

          <div class="confirm-modal-actions">
            <button
              v-if="!activeDialog.isAlert"
              class="btn btn-secondary btn-md"
              type="button"
              @click="cancel"
            >
              {{ activeDialog.cancelText ?? 'Huỷ' }}
            </button>
            <button
              ref="confirmBtnRef"
              class="btn btn-md"
              :class="confirmBtnClass"
              type="button"
              @click="accept"
            >
              {{ activeDialog.confirmText ?? 'Xác nhận' }}
            </button>
          </div>
        </div>
      </div>
    </Transition>
  </Teleport>
</template>

<style scoped>
.confirm-modal-backdrop {
  position: fixed;
  inset: 0;
  z-index: 10000;
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 16px;
  background: rgba(15, 23, 42, 0.65);
  backdrop-filter: blur(6px);
  -webkit-backdrop-filter: blur(6px);
}

.confirm-modal-card {
  position: relative;
  width: 100%;
  max-width: 440px;
  background: var(--bg-card, #ffffff);
  border: 1px solid var(--border, #e2e8f0);
  border-radius: 18px;
  box-shadow: 0 20px 40px -10px rgba(0, 0, 0, 0.25), 0 0 0 1px rgba(255, 255, 255, 0.05);
  padding: 24px;
  display: flex;
  flex-direction: column;
  gap: 20px;
  animation: confirm-pop 0.22s cubic-bezier(0.16, 1, 0.3, 1);
}

@keyframes confirm-pop {
  0% {
    opacity: 0;
    transform: scale(0.92) translateY(8px);
  }
  100% {
    opacity: 1;
    transform: scale(1) translateY(0);
  }
}

.confirm-modal-close {
  position: absolute;
  top: 14px;
  right: 14px;
  width: 30px;
  height: 30px;
  border-radius: 8px;
  border: none;
  background: transparent;
  color: var(--text-muted, #64748b);
  display: flex;
  align-items: center;
  justify-content: center;
  cursor: pointer;
  transition: all 0.15s ease;
}

.confirm-modal-close:hover {
  background: var(--bg-hover, #f1f5f9);
  color: var(--text-primary, #0f172a);
}

.confirm-modal-body {
  display: flex;
  align-items: flex-start;
  gap: 16px;
  padding-right: 20px;
}

.confirm-modal-icon-wrap {
  width: 46px;
  height: 46px;
  border-radius: 12px;
  display: flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
}

.icon-danger {
  background: rgba(239, 68, 68, 0.12);
  color: #ef4444;
}

.icon-warning {
  background: rgba(245, 158, 11, 0.12);
  color: #f59e0b;
}

.icon-info {
  background: rgba(59, 130, 246, 0.12);
  color: #3b82f6;
}

.icon-primary {
  background: rgba(99, 102, 241, 0.12);
  color: #6366f1;
}

.confirm-modal-content {
  flex: 1;
  min-width: 0;
}

.confirm-modal-title {
  margin: 0 0 6px 0;
  font-size: 17px;
  font-weight: 700;
  color: var(--text-primary, #0f172a);
  line-height: 1.35;
}

.confirm-modal-message {
  margin: 0;
  font-size: 14px;
  line-height: 1.55;
  color: var(--text-secondary, #475569);
  word-break: break-word;
  white-space: pre-line;
}

.confirm-modal-actions {
  display: flex;
  align-items: center;
  justify-content: flex-end;
  gap: 10px;
  padding-top: 4px;
}

.btn-warning {
  background: #f59e0b;
  color: #ffffff;
  border: 1px solid #d97706;
}

.btn-warning:hover {
  background: #d97706;
}

/* Transition */
.confirm-modal-fade-enter-active,
.confirm-modal-fade-leave-active {
  transition: opacity 0.2s ease;
}

.confirm-modal-fade-enter-from,
.confirm-modal-fade-leave-to {
  opacity: 0;
}
</style>
