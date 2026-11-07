<script setup lang="ts">
import DeveloperWorkspace from '../components/DeveloperWorkspace.vue'

withDefaults(
  defineProps<{
    standalone?: boolean
  }>(),
  { standalone: false },
)
</script>

<template>
  <DeveloperWorkspace :standalone="standalone" />
</template>
