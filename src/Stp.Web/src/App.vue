<script setup lang="ts">
import { RouterView } from 'vue-router'
import TeamDock from './components/TeamDock.vue'
import ToastHost from './components/ToastHost.vue'
</script>

<template>
  <RouterView />
  <TeamDock />
  <ToastHost />
</template>
