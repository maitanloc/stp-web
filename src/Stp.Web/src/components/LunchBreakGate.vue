<script setup lang="ts">
// Tạm thời vô hiệu hoá giờ nghỉ trưa & bảo trì máy chủ để người dùng test
</script>

<template>
  <div class="lunch-gate-container">
    <!-- Tạm thời vô hiệu hoá giờ nghỉ trưa & bảo trì máy chủ để người dùng test -->
  </div>
</template>

<style scoped>
.lunch-screen-fade-enter-active,
.lunch-screen-fade-leave-active {
  transition: opacity 0.3s ease;
}

.lunch-screen-fade-enter-from,
.lunch-screen-fade-leave-to {
  opacity: 0;
}

.dev-bypass-pill {
  position: fixed;
  bottom: 16px;
  right: 16px;
  z-index: 9990;
  animation: pill-slide 0.3s ease-out;
}

@keyframes pill-slide {
  from {
    transform: translateY(20px);
    opacity: 0;
  }
  to {
    transform: translateY(0);
    opacity: 1;
  }
}

.dev-pill-inner {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 6px 12px;
  border-radius: 999px;
  background: rgba(15, 23, 42, 0.9);
  color: #f8fafc;
  font-size: 12px;
  font-weight: 600;
  border: 1px solid #f59e0b;
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.3);
  backdrop-filter: blur(8px);
}
</style>
