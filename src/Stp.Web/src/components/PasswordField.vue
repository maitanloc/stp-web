<script setup lang="ts">
import { ref } from 'vue'

/**
 * Ô mật khẩu kèm nút "con mắt" (yêu cầu 11/11/2026): mặc định mắt NHẮM (mí
 * mắt + hàng mi), bấm vào mắt MỞ RA như mắt người — hình mắt dựng dần từ phẳng
 * (scaleY nhỏ) lên tròn (scaleY 1) với độ nảy nhẹ, kèm mi mắt nhạt dần; bấm
 * lần nữa mắt nhắm lại và mật khẩu được che. Input tắt autocapitalize/spellcheck
 * để bộ gõ điện thoại không phá mật khẩu.
 */
const props = withDefaults(
  defineProps<{
    modelValue: string
    placeholder?: string
    autocomplete?: string
    maxlength?: number
  }>(),
  {
    placeholder: 'Mật khẩu',
    autocomplete: 'current-password',
    maxlength: 128,
  },
)

const emit = defineEmits<{ 'update:modelValue': [value: string] }>()

const show = ref(false)

function onInput(event: Event): void {
  emit('update:modelValue', (event.target as HTMLInputElement).value)
}
</script>

<template>
  <div class="pw-wrap">
    <input
      :type="show ? 'text' : 'password'"
      :value="modelValue"
      :placeholder="props.placeholder"
      :autocomplete="props.autocomplete"
      :maxlength="props.maxlength"
      autocapitalize="off"
      autocorrect="off"
      spellcheck="false"
      @input="onInput"
    />
    <button
      type="button"
      class="eye"
      :class="{ on: show }"
      :title="show ? 'Ẩn mật khẩu' : 'Xem mật khẩu'"
      :aria-label="show ? 'Ẩn mật khẩu' : 'Xem mật khẩu'"
      :aria-pressed="show"
      @click="show = !show"
    >
      <svg
        viewBox="0 0 24 24"
        width="20"
        height="20"
        fill="none"
        stroke="currentColor"
        stroke-width="1.8"
        stroke-linecap="round"
        stroke-linejoin="round"
        aria-hidden="true"
      >
        <!-- Mắt mở: dựng dần từ phẳng lên tròn khi bấm -->
        <g class="open-eye">
          <path d="M2.2 12 C 5.2 6.6, 18.8 6.6, 21.8 12 C 18.8 17.4, 5.2 17.4, 2.2 12 Z" />
          <circle cx="12" cy="12" r="3" fill="currentColor" stroke="none" />
        </g>
        <!-- Mí nhắm + hàng mi: hiện khi đang che, mờ dần khi mở -->
        <g class="lid">
          <path d="M3.4 11.2 C 8 14.4, 16 14.4, 20.6 11.2" />
          <path d="M7.9 13.2 l-1.4 2" />
          <path d="M12 13.8 l0 2.3" />
          <path d="M16.1 13.2 l1.4 2" />
        </g>
      </svg>
    </button>
  </div>
</template>

<style scoped>
.pw-wrap {
  position: relative;
  display: block;
}

.pw-wrap input {
  width: 100%;
  box-sizing: border-box;
  border: 1.5px solid #dbe4ec;
  background: #f8fafc;
  border-radius: 11px;
  padding: 11px 46px 11px 14px;
  font: inherit;
  font-size: 14px;
  color: #0f172a;
  outline: none;
  transition: border-color 0.2s ease, box-shadow 0.2s ease, background 0.2s ease;
}

.pw-wrap input::placeholder {
  color: #9aa8b8;
}

.pw-wrap input:focus {
  border-color: #0d9488;
  background: #ffffff;
  box-shadow: 0 0 0 4px rgba(13, 148, 136, 0.14);
}

.eye {
  position: absolute;
  right: 7px;
  top: 50%;
  transform: translateY(-50%);
  width: 32px;
  height: 32px;
  display: grid;
  place-items: center;
  border: none;
  border-radius: 9px;
  background: transparent;
  color: #94a3b8;
  cursor: pointer;
  transition: color 0.2s ease, background 0.2s ease;
}

.eye:hover {
  color: #0d9488;
  background: rgba(13, 148, 136, 0.08);
}

.eye:focus-visible {
  outline: 2px solid #0d9488;
  outline-offset: 1px;
}

/* ===== Animation mắt nhắm ↔ mở ===== */

/* Mắt mở: mặc định ép phẳng (như mí đóng) + trong suốt; mở = dựng lên với
   độ nảy nhẹ cubic-bezier > 1 ở cuối cho cảm giác "blink" tự nhiên */
.open-eye {
  transform: scaleY(0.1);
  opacity: 0;
  transform-origin: 12px 12px;
  transition:
    transform 0.4s cubic-bezier(0.3, 0.7, 0.35, 1.25),
    opacity 0.24s ease;
}

.eye.on .open-eye {
  transform: scaleY(1);
  opacity: 1;
}

/* Mi nhắm: hiện khi che, biến mất khi mở (dịch nhẹ xuống như x mí mắt) */
.lid {
  opacity: 1;
  transition:
    opacity 0.22s ease 0.04s,
    transform 0.3s ease;
}

.eye.on .lid {
  opacity: 0;
  transform: translateY(1.5px);
}
</style>
