<script setup lang="ts">
import { computed } from 'vue'
import {
  jobStatusLabel,
  jobTypeLabel,
  jobTypeTone,
  kindTone,
  statusTone,
  workbookKindLabel,
} from '../lib/format'

type BadgeKind = 'job' | 'jobType' | 'workbook' | 'sync'

const props = withDefaults(
  defineProps<{
    value: string
    kind?: BadgeKind
  }>(),
  { kind: 'job' },
)

const label = computed(() => {
  switch (props.kind) {
    case 'jobType':
      return jobTypeLabel[props.value as keyof typeof jobTypeLabel] ?? props.value
    case 'workbook':
      return workbookKindLabel[props.value as keyof typeof workbookKindLabel] ?? props.value
    case 'sync':
      switch (props.value) {
        case 'Applied':
          return 'Đã áp dụng'
        case 'Pending':
          return 'Đang chờ'
        case 'Conflict':
          return 'Xung đột'
        case 'Failed':
          return 'Thất bại'
        default:
          return props.value
      }
    default:
      return jobStatusLabel[props.value as keyof typeof jobStatusLabel] ?? props.value
  }
})

const tone = computed(() => {
  switch (props.kind) {
    case 'jobType':
      return jobTypeTone(props.value as Parameters<typeof jobTypeTone>[0])
    case 'workbook':
      return kindTone(props.value as Parameters<typeof kindTone>[0])
    case 'sync':
      switch (props.value) {
        case 'Applied':
          return 'success'
        case 'Pending':
        case 'Conflict':
          return 'warning'
        case 'Failed':
          return 'danger'
        default:
          return 'neutral'
      }
    default:
      return statusTone(props.value)
  }
})
</script>

<template>
  <span class="badge" :class="`badge-${tone}`">{{ label }}</span>
</template>
