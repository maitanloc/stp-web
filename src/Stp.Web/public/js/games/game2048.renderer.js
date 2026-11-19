// Game 2048 Farm (Gộp Nông Sản) HTML5 Canvas 2D Renderer (ES Module)
// Logic Size: 160x160 logic pixels.
// 4x4 plowed soil grid on a cozy wooden tray.
// 11 crop tiers (Seed 2 -> Golden Pumpkin 2048).
// Slide easeOutQuad 120ms, sprout scale-in, merge pop (1.0 -> 1.25 -> 1.0) + sparkles.

import { spriteAtlas } from './shared/spriteAtlas.js';
import { ParticleSystem } from './shared/particles.js';
import { farmEnvironment } from './shared/environment.js';
import { sfx } from './shared/sfx.js';

export class Game2048Renderer {
  constructor(canvas, dotNetRef) {
    this.canvas = canvas;
    this.ctx = canvas.getContext('2d');
    this.dotNetRef = dotNetRef;

    this.logicWidth = 160;
    this.logicHeight = 160;
    this.boardSize = 4;
    this.tileSize = 32;
    this.gap = 4;
    this.boardPadding = 8;

    this.state = null;
    this.animFrame = null;
    this.particles = new ParticleSystem(300);

    // Track merge animations: key "r,c" -> { startTime, duration: 150 }
    this.mergeAnims = new Map();
    // Track spawn animation: key "r,c" -> { startTime, duration: 120 }
    this.spawnAnim = null;

    // Highest unlocked tier banner
    this.unlockedBanner = null; // { tier, name, startTime }
    this.maxDiscoveredTier = 1;

    this.bgCanvas = null;
    this.init();
  }

  async init() {
    await spriteAtlas.init();
    this.setupCanvas();
    this.createStaticBackground();
    this.bindEvents();
    this.startLoop();
  }

  bindEvents() {
    let touchStartX = 0;
    let touchStartY = 0;
    this.canvas.addEventListener('touchstart', (e) => {
      const touch = e.touches[0];
      if (touch) {
        touchStartX = touch.clientX;
        touchStartY = touch.clientY;
      }
    }, { passive: true });

    this.canvas.addEventListener('touchend', (e) => {
      const touch = e.changedTouches[0];
      if (!touch) return;
      const dx = touch.clientX - touchStartX;
      const dy = touch.clientY - touchStartY;
      const absDx = Math.abs(dx);
      const absDy = Math.abs(dy);

      if (Math.max(absDx, absDy) > 20) {
        let dir = 'right';
        if (absDx > absDy) {
          dir = dx > 0 ? 'right' : 'left';
        } else {
          dir = dy > 0 ? 'down' : 'up';
        }
        if (typeof this.dotNetRef?.onMove === 'function') {
          this.dotNetRef.onMove(dir);
        } else if (this.dotNetRef?.invokeMethodAsync) {
          this.dotNetRef.invokeMethodAsync('OnMove', dir);
        }
      }
    }, { passive: true });
  }

  setupCanvas() {
    this.ctx.imageSmoothingEnabled = false;
    this.updateScale();
    this.resizeObserver = new ResizeObserver(() => this.updateScale());
    this.resizeObserver.observe(this.canvas.parentElement || this.canvas);
  }

  updateScale() {
    const parent = this.canvas.parentElement;
    const pw = parent ? parent.clientWidth : window.innerWidth;
    const ph = parent ? parent.clientHeight : window.innerHeight;
    const sx = Math.floor(pw / this.logicWidth) || 1;
    const sy = Math.floor(ph / this.logicHeight) || 1;
    this.scale = Math.max(1, Math.min(sx, sy));

    this.canvas.width = this.logicWidth * this.scale;
    this.canvas.height = this.logicHeight * this.scale;
    this.ctx.imageSmoothingEnabled = false;
  }

  createStaticBackground() {
    this.bgCanvas = document.createElement('canvas');
    this.bgCanvas.width = this.logicWidth;
    this.bgCanvas.height = this.logicHeight;
    const bCtx = this.bgCanvas.getContext('2d');
    bCtx.imageSmoothingEnabled = false;

    // 1. Khay gỗ nền
    bCtx.fillStyle = '#613b19';
    bCtx.fillRect(0, 0, this.logicWidth, this.logicHeight);
    bCtx.fillStyle = '#96602e';
    bCtx.fillRect(3, 3, this.logicWidth - 6, this.logicHeight - 6);
    bCtx.fillStyle = '#573114';
    bCtx.fillRect(this.boardPadding - 2, this.boardPadding - 2, 144 + 4, 144 + 4);

    // 2. 16 ô đất đã cày rỗng
    for (let r = 0; r < this.boardSize; r++) {
      for (let c = 0; c < this.boardSize; c++) {
        const x = this.boardPadding + c * (this.tileSize + this.gap);
        const y = this.boardPadding + r * (this.tileSize + this.gap);

        bCtx.fillStyle = '#3a200f';
        bCtx.fillRect(x, y, this.tileSize, this.tileSize);
        bCtx.fillStyle = '#4e2b14';
        bCtx.fillRect(x + 1, y + 1, this.tileSize - 2, this.tileSize - 2);
      }
    }
  }

  updateState(dto) {
    const prevScore = this.state ? this.state.score : 0;
    const now = performance.now();

    // 1. Kiểm tra các ô vừa gộp
    if (dto.lastMerged && dto.lastMerged.length > 0) {
      sfx.playMerge();
      dto.lastMerged.forEach((t) => {
        const key = `${t.r},${t.c}`;
        this.mergeAnims.set(key, { startTime: now, duration: 160 });

        const x = this.boardPadding + t.c * (this.tileSize + this.gap) + this.tileSize / 2;
        const y = this.boardPadding + t.r * (this.tileSize + this.gap) + this.tileSize / 2;
        this.particles.emitBurst(x, y, 10, ['#fbc531', '#78c850', '#ffffff']);
      });
    } else if (dto.score > prevScore) {
      sfx.playMove();
    }

    // 2. Kiểm tra ô mới sinh
    if (dto.lastSpawned) {
      this.spawnAnim = { r: dto.lastSpawned.r, c: dto.lastSpawned.c, startTime: now, duration: 120 };
      const x = this.boardPadding + dto.lastSpawned.c * (this.tileSize + this.gap) + this.tileSize / 2;
      const y = this.boardPadding + dto.lastSpawned.r * (this.tileSize + this.gap) + this.tileSize / 2;
      this.particles.emitBurst(x, y, 6, ['#875026', '#b0703c']);
    }

    // 3. Kiểm tra mở khóa cấp nông sản mới
    if (dto.tierBoard) {
      let currentMax = 1;
      for (let r = 0; r < 4; r++) {
        for (let c = 0; c < 4; c++) {
          const t = dto.tierBoard[r][c];
          if (t > currentMax) currentMax = t;
        }
      }

      if (currentMax > this.maxDiscoveredTier) {
        this.maxDiscoveredTier = currentMax;
        const cropNames = [
          '', 'Hạt giống', 'Mầm non', 'Cải thảo', 'Cà rốt', 'Khoai tây',
          'Dâu tây', 'Cà chua', 'Bắp ngô', 'Dưa hấu', 'Bí ngô', 'Bí ngô vàng'
        ];
        this.unlockedBanner = {
          tier: currentMax,
          name: cropNames[currentMax] || 'Nông sản mới',
          startTime: now
        };
        sfx.playWin();
      }
    }

    if (dto.isGameOver && (!this.state || !this.state.isGameOver)) {
      sfx.playGameOver();
      this.particles.triggerShake(5);
    }

    this.state = dto;
  }

  startLoop() {
    const loop = (time) => {
      this.update(time);
      this.render(time);
      this.animFrame = requestAnimationFrame(loop);
    };
    this.animFrame = requestAnimationFrame(loop);
  }

  update(time) {
    this.particles.update();
    this.particles.emitAmbient(this.logicWidth, this.logicHeight);

    // Auto dismiss unlocked banner after 2.5s
    if (this.unlockedBanner && time - this.unlockedBanner.startTime > 2500) {
      this.unlockedBanner = null;
    }
  }

  render(time) {
    const ctx = this.ctx;
    ctx.save();
    ctx.imageSmoothingEnabled = false;

    // Screen shake
    const shake = this.particles.getShakeOffset();
    ctx.translate(shake.x * this.scale, shake.y * this.scale);
    ctx.scale(this.scale, this.scale);

    // 1. Static wooden background
    if (this.bgCanvas) {
      ctx.drawImage(this.bgCanvas, 0, 0);
    }

    // 2. Render 4x4 Crops
    const board = this.state ? (this.state.grid || this.state.board) : null;
    if (board) {
      for (let r = 0; r < this.boardSize; r++) {
        for (let c = 0; c < this.boardSize; c++) {
          const val = board[r] ? board[r][c] : 0;
          if (!val || val === 0) continue;

          const tier = (this.state.tierBoard && this.state.tierBoard[r])
            ? this.state.tierBoard[r][c]
            : Math.max(1, Math.min(11, Math.round(Math.log2(val))));
          if (tier === 0) continue;

          const baseX = this.boardPadding + c * (this.tileSize + this.gap);
          const baseY = this.boardPadding + r * (this.tileSize + this.gap);

          // Calculate scale (merge pop or spawn scale-in)
          let scaleFactor = 1.0;
          const key = `${r},${c}`;
          if (this.mergeAnims.has(key)) {
            const m = this.mergeAnims.get(key);
            const elapsed = time - m.startTime;
            if (elapsed < m.duration) {
              const progress = elapsed / m.duration;
              scaleFactor = 1.0 + 0.28 * Math.sin(progress * Math.PI);
            } else {
              this.mergeAnims.delete(key);
            }
          } else if (this.spawnAnim && this.spawnAnim.r === r && this.spawnAnim.c === c) {
            const elapsed = time - this.spawnAnim.startTime;
            if (elapsed < this.spawnAnim.duration) {
              scaleFactor = Math.min(1.0, elapsed / this.spawnAnim.duration);
            }
          }

          // Draw tile background (soil patch)
          ctx.fillStyle = '#7a421b';
          ctx.fillRect(baseX + 1, baseY + 1, this.tileSize - 2, this.tileSize - 2);

          // Draw crop sprite centered & scaled
          const spr = spriteAtlas.get(`crop_2048_tier_${tier}`) || spriteAtlas.get(`crop2048_${tier}`);
          const cx = baseX + this.tileSize / 2;
          const cy = baseY + this.tileSize / 2;

          ctx.save();
          ctx.translate(cx, cy);
          ctx.scale(scaleFactor, scaleFactor);

          // Drop shadow
          ctx.fillStyle = 'rgba(0, 0, 0, 0.25)';
          ctx.beginPath();
          ctx.ellipse(0, 6, 7, 3, 0, 0, Math.PI * 2);
          ctx.fill();

          if (spr) {
            ctx.drawImage(spr, -8, -10);
          } else {
            ctx.fillStyle = '#fbc531';
            ctx.fillRect(-6, -6, 12, 12);
          }

          // Value badge
          ctx.font = 'bold 8px "Pixelify Sans", monospace';
          ctx.textAlign = 'center';
          ctx.fillStyle = '#ffffff';
          ctx.shadowColor = '#000000';
          ctx.shadowBlur = 2;
          ctx.fillText(val.toString(), 0, 11);
          ctx.shadowBlur = 0;

          ctx.restore();
        }
      }
    }

    // 3. Render particles
    this.particles.render(ctx);

    // 4. Mở khóa nông sản mới Banner (Wooden Plaque)
    if (this.unlockedBanner) {
      ctx.fillStyle = 'rgba(44, 24, 16, 0.92)';
      ctx.fillRect(15, 60, 130, 42);
      ctx.strokeStyle = '#fbc531';
      ctx.lineWidth = 1;
      ctx.strokeRect(16.5, 61.5, 127, 39);

      const spr = spriteAtlas.get(`crop2048_${this.unlockedBanner.tier}`);
      if (spr) {
        ctx.drawImage(spr, 24, 73);
      }

      ctx.fillStyle = '#fbc531';
      ctx.font = 'bold 9px "Pixelify Sans", monospace';
      ctx.textAlign = 'left';
      ctx.fillText('MỞ KHÓA MỚI!', 46, 74);
      ctx.fillStyle = '#f5ecd5';
      ctx.font = '8px "Pixelify Sans", monospace';
      ctx.fillText(this.unlockedBanner.name, 46, 88);
    }

    // 5. Game Over / Win overlay
    if (this.state && this.state.isGameOver) {
      ctx.fillStyle = 'rgba(15, 22, 34, 0.8)';
      ctx.fillRect(0, 0, this.logicWidth, this.logicHeight);

      ctx.fillStyle = '#fbc531';
      ctx.font = 'bold 12px "Pixelify Sans", monospace';
      ctx.textAlign = 'center';
      ctx.fillText('HẾT Ô TRỐNG ĐỂ GỘP!', this.logicWidth / 2, 70);

      ctx.fillStyle = '#f5ecd5';
      ctx.font = '8px "Pixelify Sans", monospace';
      ctx.fillText(`Điểm cuối: ${this.state.score}`, this.logicWidth / 2, 88);
      ctx.fillText('Bấm Ván mới để chơi lại', this.logicWidth / 2, 102);
    }

    // 6. Day/night ambient overlay
    farmEnvironment.renderOverlay(ctx, this.logicWidth, this.logicHeight);

    ctx.restore();
  }

  destroy() {
    if (this.animFrame) cancelAnimationFrame(this.animFrame);
    if (this.resizeObserver) this.resizeObserver.disconnect();
  }
}

export function initGame2048(canvas, dotNetRef) {
  return new Game2048Renderer(canvas, dotNetRef);
}
