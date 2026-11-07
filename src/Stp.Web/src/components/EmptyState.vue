<script setup lang="ts">
import { Inbox } from '@lucide/vue'

withDefaults(
  defineProps<{
    title: string
    description?: string
  }>(),
  { description: '' },
)
</script>

<template>
  <div class="empty-state">
    <div class="empty-icon">
      <slot name="icon">
        <Inbox :size="26" />
      </slot>
    </div>
    <div class="empty-title">{{ title }}</div>
    <p v-if="description" class="empty-desc">{{ description }}</p>
    <slot name="action" />
  </div>
</template>
