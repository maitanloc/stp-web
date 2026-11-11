<script setup lang="ts">
import { nextTick, ref } from 'vue'
import { FileSpreadsheet, FolderOpen, RefreshCw, ShieldCheck, Users } from '@lucide/vue'
import { errorMessage } from '../lib/errors'
import { loginTeamIdentity, registerTeamIdentity } from '../composables/useTeamIdentity'

/**
 * "Cuốn hồ sơ" ra vào hệ thống (yêu cầu 11/11/2026):
 *   • Chưa có phiên → app mở thẳng màn này, mặt trước là Đăng nhập.
 *   • Bấm "Đăng ký" → lật 3D sang mặt sau là form tự đăng ký, xong vào dùng ngay.
 *   • Đã có phiên (cookie ghi nhớ 24 giờ sliding) → màn này không bao giờ hiện,
 *     App.vue vào thẳng hệ thống qua màn chờ.
 */
const emit = defineEmits<{ entered: [] }>()

const flipped = ref(false)
const busy = ref(false)

const loginUsername = ref('')
const loginPassword = ref('')
const loginError = ref<string | null>(null)

const regUsername = ref('')
const regDisplayName = ref('')
const regPassword = ref('')
const regError = ref<string | null>(null)

const loginUsernameElement = ref<HTMLInputElement | null>(null)
const regUsernameElement = ref<HTMLInputElement | null>(null)

async function submitLogin(): Promise<void> {
  if (busy.value) return
  if (loginUsername.value.trim().length === 0 || loginPassword.value.length === 0) {
    loginError.value = 'Nhập cả tên đăng nhập và mật khẩu.'
    return
  }
  busy.value = true
  loginError.value = null
  try {
    await loginTeamIdentity(loginUsername.value.trim(), loginPassword.value)
    emit('entered')
  } catch (caught) {
    loginError.value = errorMessage(caught)
  } finally {
    busy.value = false
  }
}

async function submitRegister(): Promise<void> {
  if (busy.value) return
  if (regUsername.value.trim().length === 0 || regDisplayName.value.trim().length === 0) {
    regError.value = 'Nhập tên đăng nhập và tên hiển thị.'
    return
  }
  if (regPassword.value.length < 6) {
    regError.value = 'Mật khẩu cần tối thiểu 6 ký tự.'
    return
  }
  busy.value = true
  regError.value = null
  try {
    await registerTeamIdentity(regUsername.value.trim(), regDisplayName.value.trim(), regPassword.value)
    emit('entered')
  } catch (caught) {
    regError.value = errorMessage(caught)
  } finally {
    busy.value = false
  }
}

function openRegister(): void {
  flipped.value = true
  void nextTick(() => window.setTimeout(() => regUsernameElement.value?.focus(), 450))
}

function openLogin(): void {
  flipped.value = false
  void nextTick(() => window.setTimeout(() => loginUsernameElement.value?.focus(), 450))
}
</script>

<template>
  <div class="gate" role="dialog" aria-label="Đăng nhập hoặc đăng ký tài khoản nhóm">
    <!-- Nền: các tờ hồ sơ trôi lơ lửng -->
    <div class="gate-deco" aria-hidden="true">
      <span class="sheet sheet-1"></span>
      <span class="sheet sheet-2"></span>
      <span class="sheet sheet-3"></span>
      <span class="glow glow-1"></span>
      <span class="glow glow-2"></span>
    </div>

    <header class="gate-top">
      <span class="gate-mark">
        <FolderOpen :size="18" />
        STP ARCHIVE
      </span>
      <span class="gate-badge">Phiên tự ghi nhớ 24 giờ</span>
    </header>

    <div class="book" :class="{ 'book-flipped': flipped }">
      <div class="book-inner">
        <!-- MẶT TRƯỚC — Đăng nhập -->
        <section class="face face-front">
          <aside class="face-side">
            <h1>STP Archive</h1>
            <p class="side-sub">Hồ sơ nhóm nội bộ — một link duy nhất cho cả phòng.</p>
            <ul class="side-list">
              <li>
                <ShieldCheck :size="17" />
                <span>Mỗi người một tài khoản — chat, nhật ký, dấu dòng gắn đúng tên bạn.</span>
              </li>
              <li>
                <RefreshCw :size="17" />
                <span>Đồng bộ, xuất trích yếu… ai làm gì hệ thống ghi rõ.</span>
              </li>
              <li>
                <FolderOpen :size="17" />
                <span>Kho hồ sơ giữ nguyên dữ liệu khi bạn quay lại.</span>
              </li>
            </ul>
            <p class="side-foot">Vào lần đầu? Lật trang để tự đăng ký — 30 giây là xong.</p>
          </aside>
          <div class="face-form">
            <h2>Mở hồ sơ làm việc</h2>
            <p class="form-sub">Đăng nhập bằng tài khoản nhóm của bạn.</p>
            <form @submit.prevent="submitLogin()">
              <label>
                <span>Tên đăng nhập</span>
                <input
                  ref="loginUsernameElement"
                  v-model="loginUsername"
                  type="text"
                  maxlength="50"
                  autocomplete="username"
                  placeholder="vd: lan, minh.quang"
                />
              </label>
              <label>
                <span>Mật khẩu</span>
                <input
                  v-model="loginPassword"
                  type="password"
                  maxlength="128"
                  autocomplete="current-password"
                  placeholder="Mật khẩu của bạn"
                />
              </label>
              <p v-if="loginError" class="form-error">{{ loginError }}</p>
              <button class="face-submit" type="submit" :disabled="busy">
                {{ busy ? 'Đang mở hồ sơ…' : 'Đăng nhập' }}
              </button>
            </form>
            <p class="face-swap">
              Chưa có tài khoản?
              <button type="button" @click="openRegister()">Đăng ký ngay</button>
            </p>
          </div>
        </section>

        <!-- MẶT SAU — Đăng ký (lật từ mặt trước sang) -->
        <section class="face face-back" aria-hidden="false">
          <div class="face-form">
            <h2>Đăng ký tài khoản</h2>
            <p class="form-sub">Tự tạo tài khoản thành viên và vào làm việc luôn.</p>
            <form @submit.prevent="submitRegister()">
              <label>
                <span>Tên đăng nhập</span>
                <input
                  ref="regUsernameElement"
                  v-model="regUsername"
                  type="text"
                  maxlength="50"
                  autocomplete="username"
                  placeholder="vd: lan, minh.quang (không dấu, không cách)"
                />
              </label>
              <label>
                <span>Tên hiển thị</span>
                <input
                  v-model="regDisplayName"
                  type="text"
                  maxlength="100"
                  autocomplete="nickname"
                  placeholder="vd: Lan, Minh Quang — hiện trong chat, nhật ký"
                />
              </label>
              <label>
                <span>Mật khẩu</span>
                <input
                  v-model="regPassword"
                  type="password"
                  maxlength="128"
                  autocomplete="new-password"
                  placeholder="Tối thiểu 6 ký tự"
                />
              </label>
              <p v-if="regError" class="form-error">{{ regError }}</p>
              <button class="face-submit" type="submit" :disabled="busy">
                {{ busy ? 'Đang tạo hồ sơ…' : 'Tạo tài khoản & vào ngay' }}
              </button>
            </form>
            <p class="face-swap">
              Đã có tài khoản?
              <button type="button" @click="openLogin()">Đăng nhập</button>
            </p>
          </div>
          <aside class="face-side side-back">
            <h1>Hồ sơ của bạn</h1>
            <p class="side-sub">Tài khoản vừa tạo là tài khoản thành viên — quản trị nâng quyền sau nếu cần.</p>
            <ul class="side-list">
              <li>
                <Users :size="17" />
                <span>Tên hiển thị hiện ở chat, presence và nhật ký nhóm.</span>
              </li>
              <li>
                <FileSpreadsheet :size="17" />
                <span>Việc bạn làm (xuất, đồng bộ…) vào thẻ tổng kết sau màn chờ.</span>
              </li>
              <li>
                <ShieldCheck :size="17" />
                <span>Mật khẩu mã hóa PBKDF2 — không ai đọc được, kể cả quản trị.</span>
              </li>
            </ul>
          </aside>
        </section>
      </div>
    </div>

    <footer class="gate-foot">Một link duy nhất cho cả nhóm · dữ liệu nằm trên máy nội bộ</footer>
  </div>
</template>

<style scoped>
/* ===== Sân khấu ===== */
.gate {
  position: fixed;
  inset: 0;
  z-index: 100;
  display: grid;
  grid-template-rows: auto 1fr auto;
  justify-items: center;
  overflow: auto;
  background:
    radial-gradient(1100px 600px at 15% -10%, rgba(20, 184, 166, 0.28), transparent 60%),
    radial-gradient(900px 700px at 110% 110%, rgba(13, 148, 136, 0.22), transparent 55%),
    linear-gradient(155deg, #052e2c 0%, #073a37 45%, #0a4640 100%);
  padding: 22px 18px 16px;
  color: #eafff9;
}

.gate-deco {
  position: absolute;
  inset: 0;
  overflow: hidden;
  pointer-events: none;
}

.sheet {
  position: absolute;
  border-radius: 12px;
  background: rgba(255, 255, 255, 0.05);
  border: 1px solid rgba(255, 255, 255, 0.09);
  box-shadow: 0 18px 50px rgba(0, 0, 0, 0.25);
  animation: floaty 9s ease-in-out infinite;
}

.sheet-1 {
  width: 150px;
  height: 200px;
  top: 12%;
  left: 6%;
  transform: rotate(-14deg);
}

.sheet-2 {
  width: 120px;
  height: 160px;
  bottom: 10%;
  right: 7%;
  transform: rotate(12deg);
  animation-delay: -3s;
}

.sheet-3 {
  width: 90px;
  height: 120px;
  top: 58%;
  left: 88%;
  transform: rotate(24deg);
  animation-delay: -6s;
}

.glow {
  position: absolute;
  border-radius: 999px;
  filter: blur(70px);
}

.glow-1 {
  width: 380px;
  height: 380px;
  top: -120px;
  right: -80px;
  background: rgba(45, 212, 191, 0.16);
}

.glow-2 {
  width: 320px;
  height: 320px;
  bottom: -140px;
  left: -60px;
  background: rgba(13, 148, 136, 0.18);
}

@keyframes floaty {
  0%, 100% { margin-top: 0; }
  50% { margin-top: -18px; }
}

/* ===== Đầu trang / chân trang ===== */
.gate-top {
  width: min(980px, 100%);
  display: flex;
  align-items: center;
  justify-content: space-between;
  z-index: 1;
}

.gate-mark {
  display: inline-flex;
  align-items: center;
  gap: 9px;
  font-weight: 800;
  letter-spacing: 0.14em;
  font-size: 13px;
}

.gate-badge {
  font-size: 11.5px;
  padding: 5px 12px;
  border-radius: 999px;
  border: 1px solid rgba(255, 255, 255, 0.18);
  background: rgba(255, 255, 255, 0.06);
  backdrop-filter: blur(6px);
}

.gate-foot {
  z-index: 1;
  font-size: 11.5px;
  color: rgba(234, 255, 249, 0.55);
  padding-top: 14px;
}

/* ===== Cuốn hồ sơ ===== */
.book {
  width: min(960px, 100%);
  height: 585px;
  perspective: 1700px;
  z-index: 1;
  margin-top: 14px;
}

.book-inner {
  position: relative;
  width: 100%;
  height: 100%;
  transform-style: preserve-3d;
  transition: transform 1.05s cubic-bezier(0.22, 0.9, 0.26, 1);
}

.book-flipped .book-inner {
  transform: rotateY(-180deg);
}

.face {
  position: absolute;
  inset: 0;
  display: grid;
  grid-template-columns: 1fr 1.06fr;
  backface-visibility: hidden;
  border-radius: 22px;
  overflow: hidden;
  background: #fdfefc;
  box-shadow:
    0 40px 90px rgba(2, 24, 22, 0.45),
    0 4px 18px rgba(2, 24, 22, 0.3);
}

.face-back {
  transform: rotateY(180deg);
}

/* Gáy sách: đường gấp mờ ở mép trong */
.face::after {
  content: '';
  position: absolute;
  top: 0;
  bottom: 0;
  left: 50%;
  width: 46px;
  transform: translateX(-23px);
  background: linear-gradient(
    90deg,
    transparent 0%,
    rgba(15, 23, 42, 0.05) 45%,
    rgba(15, 23, 42, 0.11) 50%,
    rgba(15, 23, 42, 0.05) 55%,
    transparent 100%
  );
  pointer-events: none;
}

/* ===== Panel thương hiệu ===== */
.face-side {
  padding: 34px 30px;
  display: flex;
  flex-direction: column;
  gap: 14px;
  color: #f0fdfa;
  background:
    radial-gradient(500px 300px at -10% -20%, rgba(255, 255, 255, 0.09), transparent 60%),
    linear-gradient(160deg, #0b5f57 0%, #0c7263 55%, #0a5a50 100%);
  position: relative;
  overflow: hidden;
}

.face-side::before {
  content: '';
  position: absolute;
  right: -60px;
  bottom: -70px;
  width: 240px;
  height: 240px;
  border-radius: 30px;
  border: 1.5px dashed rgba(255, 255, 255, 0.16);
  transform: rotate(18deg);
}

.face-side h1 {
  margin: 4px 0 0;
  font-size: 27px;
  font-weight: 800;
  letter-spacing: 0.01em;
}

.side-sub {
  margin: 0;
  font-size: 13.5px;
  line-height: 1.6;
  color: rgba(240, 253, 250, 0.82);
}

.side-list {
  list-style: none;
  margin: 8px 0 0;
  padding: 0;
  display: flex;
  flex-direction: column;
  gap: 13px;
}

.side-list li {
  display: flex;
  gap: 11px;
  align-items: flex-start;
  font-size: 13px;
  line-height: 1.55;
  color: rgba(240, 253, 250, 0.92);
}

.side-list svg {
  flex-shrink: 0;
  margin-top: 2px;
  color: #7ff0dd;
}

.side-foot {
  margin-top: auto;
  font-size: 12px;
  color: rgba(240, 253, 250, 0.62);
  border-top: 1px solid rgba(255, 255, 255, 0.14);
  padding-top: 14px;
}

/* ===== Form ===== */
.face-form {
  padding: 40px 42px 30px;
  display: flex;
  flex-direction: column;
  overflow: auto;
}

.face-form h2 {
  margin: 0 0 6px;
  font-size: 22px;
  color: #0f172a;
  font-weight: 800;
}

.form-sub {
  margin: 0 0 22px;
  font-size: 13px;
  color: #64748b;
}

.face-form form {
  display: flex;
  flex-direction: column;
  gap: 15px;
}

.face-form label {
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.face-form label span {
  font-size: 12px;
  font-weight: 700;
  letter-spacing: 0.04em;
  text-transform: uppercase;
  color: #475569;
}

.face-form input {
  border: 1.5px solid #dbe4ec;
  background: #f8fafc;
  border-radius: 11px;
  padding: 11px 14px;
  font-size: 14px;
  color: #0f172a;
  outline: none;
  transition: border-color 0.2s ease, box-shadow 0.2s ease, background 0.2s ease;
}

.face-form input::placeholder {
  color: #9aa8b8;
}

.face-form input:focus {
  border-color: #0d9488;
  background: #ffffff;
  box-shadow: 0 0 0 4px rgba(13, 148, 136, 0.14);
}

.form-error {
  margin: 0;
  font-size: 12.5px;
  color: #dc2626;
  background: #fef2f2;
  border: 1px solid #fecaca;
  border-radius: 9px;
  padding: 8px 12px;
}

.face-submit {
  margin-top: 4px;
  border: none;
  border-radius: 12px;
  padding: 13px 18px;
  font-size: 14.5px;
  font-weight: 700;
  color: #ffffff;
  cursor: pointer;
  background: linear-gradient(135deg, #0d9488 0%, #0f766e 100%);
  box-shadow: 0 10px 24px rgba(13, 148, 136, 0.35);
  transition: transform 0.18s ease, box-shadow 0.18s ease, filter 0.18s ease;
}

.face-submit:hover:not(:disabled) {
  transform: translateY(-2px);
  box-shadow: 0 14px 30px rgba(13, 148, 136, 0.42);
  filter: brightness(1.05);
}

.face-submit:disabled {
  opacity: 0.6;
  cursor: wait;
}

.face-swap {
  margin: auto 0 0;
  padding-top: 20px;
  text-align: center;
  font-size: 13px;
  color: #64748b;
}

.face-swap button {
  border: none;
  background: none;
  color: #0d9488;
  font-weight: 700;
  cursor: pointer;
  font-size: 13px;
  padding: 2px 4px;
  border-radius: 6px;
}

.face-swap button:hover {
  text-decoration: underline;
}

/* ===== Hình dạng theo kích thước ===== */
@media (max-width: 860px) {
  .face {
    grid-template-columns: 1fr;
  }

  .face-side {
    display: none;
  }

  .face::after {
    display: none;
  }

  .book {
    height: auto;
    min-height: 545px;
  }

  /* Màn nhỏ: bỏ 3D, lật kiểu chuyển trang 2D cho nhẹ máy và không lỗi gương */
  .book-inner {
    transform: none !important;
    transform-style: flat;
    height: auto;
    min-height: 545px;
  }

  .face-front,
  .face-back {
    position: relative;
  }

  .face-back {
    transform: none;
    display: none;
  }

  .book-flipped .face-front {
    display: none;
  }

  .book-flipped .face-back {
    display: flex;
    animation: face-in 0.5s ease;
  }

  @keyframes face-in {
    from {
      opacity: 0;
      transform: translateX(26px);
    }
    to {
      opacity: 1;
      transform: none;
    }
  }

  .gate {
    padding: 16px 12px 12px;
  }

  .gate-badge {
    display: none;
  }
}
</style>
