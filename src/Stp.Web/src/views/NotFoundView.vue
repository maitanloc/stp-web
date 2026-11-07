<script setup lang="ts">
import { RouterLink } from 'vue-router'
import { Compass } from '@lucide/vue'
</script>

<template>
  <div class="notfound-wrap">
    <div class="notfound-card">
      <div class="dev-gate-icon">
        <Compass :size="26" />
      </div>
      <h1 class="dev-gate-title">Không tìm thấy trang</h1>
      <p class="dev-gate-text">
        Đường dẫn bạn mở không tồn tại hoặc đã bị di chuyển. Hãy quay về trang tổng quan để tiếp tục.
      </p>
      <div class="row" style="margin-top: 20px">
        <RouterLink class="btn btn-primary" to="/">Về tổng quan</RouterLink>
        <RouterLink class="btn btn-secondary" to="/kho">Mở kho hồ sơ</RouterLink>
      </div>
    </div>
  </div>
</template>
