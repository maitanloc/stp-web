<script setup lang="ts">
import { CircleCheckBig, CircleX, Info, TriangleExclamationPoint, X } from '@lucide/vue'
import { useToast } from '../composables/useToast'

const { toasts, dismiss } = useToast()

const icons = {
  success: CircleCheckBig,
  error: CircleX,
  warning: TriangleExclamationPoint,
  info: Info,
}
</script>

<template>
  <div class="toast-host" aria-live="polite">
    <TransitionGroup name="toast">
      <div v-for="toast in toasts" :key="toast.id" class="toast" :class="toast.type">
        <component :is="icons[toast.type]" :size="18" />
        <div class="grow">
          <div class="toast-title">{{ toast.title }}</div>
          <div v-if="toast.message" class="toast-message">{{ toast.message }}</div>
        </div>
        <button class="toast-close" type="button" aria-label="Đóng" @click="dismiss(toast.id)">
          <X :size="15" />
        </button>
      </div>
    </TransitionGroup>
  </div>
</template>
