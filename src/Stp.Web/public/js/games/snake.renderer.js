// Snake Farm (Đàn Gà Con) HTML5 Canvas 2D Renderer (ES Module)
// Logic Size: 20x20 tiles * 16px = 320x320 logic pixels.
// Mother hen head (4 directions), chicks body bobbing with phase delay, 4 crops, particles.

import { spriteAtlas } from './shared/spriteAtlas.js';
import { ParticleSystem } from './shared/particles.js';
import { farmEnvironment } from './shared/environment.js';
import { sfx } from './shared/sfx.js';

export class SnakeRenderer {
  constructor(canvas, dotNetRef) {
    this.canvas = canvas;
    this.ctx = canvas.getContext('2d');
    this.dotNetRef = dotNetRef;

    this.logicWidth = 320;
    this.logicHeight = 320;
    this.tileSize = 16;
    this.gridSize = 20;

    this.state = null;
    this.animFrame = null;
    this.particles = new ParticleSystem(300);

    // Static grass arena cache with wooden fence
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

    const grass = spriteAtlas.get('tile_grass');
    const fence = spriteAtlas.get('tile_fence');

    // 1. Phủ cỏ
    for (let r = 0; r < this.gridSize; r++) {
      for (let c = 0; c < this.gridSize; c++) {
        if (grass) bCtx.drawImage(grass, c * 16, r * 16);
        else {
          bCtx.fillStyle = (r * 7 + c * 13) % 5 === 0 ? '#63ad3b' : '#5fa838';
          bCtx.fillRect(c * 16, r * 16, 16, 16);
        }
      }
    }

    // 2. Viền hàng rào gỗ xung quanh
    for (let i = 0; i < this.gridSize; i++) {
      if (fence) {
        bCtx.drawImage(fence, i * 16, 0);
        bCtx.drawImage(fence, i * 16, (this.gridSize - 1) * 16);
        bCtx.drawImage(fence, 0, i * 16);
        bCtx.drawImage(fence, (this.gridSize - 1) * 16, i * 16);
      } else {
        bCtx.fillStyle = '#613b19';
        bCtx.fillRect(i * 16, 0, 16, 2);
        bCtx.fillRect(i * 16, (this.gridSize - 1) * 16 + 14, 16, 2);
        bCtx.fillRect(0, i * 16, 2, 16);
        bCtx.fillRect((this.gridSize - 1) * 16 + 14, i * 16, 2, 16);
      }
    }
  }

  bindEvents() {
    // Canvas click to start or resume
    this._clickHandler = () => {
      if (this.state && (!this.state.isRunning || this.state.isGameOver)) {
        if (typeof this.dotNetRef?.onStartOrRestart === 'function') {
          this.dotNetRef.onStartOrRestart();
        } else if (this.dotNetRef?.invokeMethodAsync) {
          this.dotNetRef.invokeMethodAsync('OnStartOrRestart');
        }
      }
    };
    this.canvas.addEventListener('click', this._clickHandler);
  }

  updateState(dto) {
    if (this.state) {
      // Ăn mồi -> nổ hạt + bay số điểm
      if (dto.score > this.state.score) {
        sfx.playEat();
        this.particles.emitBurst(
          dto.head.x * 16 + 8,
          dto.head.y * 16 + 8,
          12,
          ['#ffcc00', '#ff9900', '#78c850']
        );
        this.particles.emitFloatingText(
          dto.head.x * 16 + 8,
          dto.head.y * 16 + 4,
          '+10'
        );
      }

      // Game over -> rung màn hình + sfx
      if (dto.isGameOver && !this.state.isGameOver) {
        sfx.playGameOver();
        this.particles.triggerShake(6);
      }
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
  }

  render(time) {
    const ctx = this.ctx;
    ctx.save();
    ctx.imageSmoothingEnabled = false;

    // Apply screen shake
    const shake = this.particles.getShakeOffset();
    ctx.translate(shake.x * this.scale, shake.y * this.scale);
    ctx.scale(this.scale, this.scale);

    // 1. Draw static arena background
    if (this.bgCanvas) {
      ctx.drawImage(this.bgCanvas, 0, 0);
    }

    // 2. Draw Crop Food (bouncing & pulsating)
    if (this.state && this.state.food) {
      const cropIndex = this.state.cropType || 0;
      const cropSpr = spriteAtlas.get(`snake_crop_${cropIndex}`);
      const fx = this.state.food.x * 16;
      const fy = this.state.food.y * 16;
      const bounce = Math.round(Math.sin(time * 0.008) * 2);

      // Shadow under crop
      ctx.fillStyle = 'rgba(0,0,0,0.2)';
      ctx.beginPath();
      ctx.ellipse(fx + 8, fy + 14, 5, 2, 0, 0, Math.PI * 2);
      ctx.fill();

      if (cropSpr) {
        ctx.drawImage(cropSpr, fx, fy + bounce);
      } else {
        ctx.fillStyle = '#ff6b6b';
        ctx.fillRect(fx + 3, fy + 3 + bounce, 10, 10);
      }
    }

    // 3. Draw Snake Body (Chicks bobbing with phase offset)
    if (this.state && this.state.body && this.state.body.length > 1) {
      const chickSpr = spriteAtlas.get('snake_chick');
      for (let i = this.state.body.length - 1; i >= 1; i--) {
        const pt = this.state.body[i];
        const px = pt.x * 16;
        const py = pt.y * 16;
        const bob = Math.round(Math.sin(time * 0.012 + i * 0.5) * 1.5);

        // Shadow under chick
        ctx.fillStyle = 'rgba(0,0,0,0.2)';
        ctx.beginPath();
        ctx.ellipse(px + 8, py + 14, 5, 2, 0, 0, Math.PI * 2);
        ctx.fill();

        if (chickSpr) {
          ctx.drawImage(chickSpr, px, py + bob);
        } else {
          ctx.fillStyle = '#ffd166';
          ctx.fillRect(px + 3, py + 3 + bob, 10, 10);
        }
      }
    }

    // 4. Draw Mother Hen Head (facing current direction)
    if (this.state && this.state.head) {
      const dir = this.state.direction || 'right';
      const henSpr = spriteAtlas.get(`snake_hen_${dir}`);
      const hx = this.state.head.x * 16;
      const hy = this.state.head.y * 16;
      const headBob = Math.round(Math.sin(time * 0.012) * 1.5);

      // Shadow
      ctx.fillStyle = 'rgba(0,0,0,0.28)';
      ctx.beginPath();
      ctx.ellipse(hx + 8, hy + 14, 6, 2.5, 0, 0, Math.PI * 2);
      ctx.fill();

      if (henSpr) {
        ctx.drawImage(henSpr, hx, hy + headBob);
      } else {
        ctx.fillStyle = '#f5ecd5';
        ctx.fillRect(hx + 2, hy + 2 + headBob, 12, 12);
      }
    }

    // 5. Draw particles & floating texts
    this.particles.render(ctx);

    // 6. Game Over / Pause Overlay
    if (this.state && this.state.isGameOver) {
      ctx.fillStyle = 'rgba(15, 22, 34, 0.75)';
      ctx.fillRect(0, 0, this.logicWidth, this.logicHeight);

      ctx.fillStyle = '#fbc531';
      ctx.font = 'bold 16px "Pixelify Sans", monospace';
      ctx.textAlign = 'center';
      ctx.fillText('ĐÀN GÀ ĐÃ MỆT NGHỈ!', this.logicWidth / 2, 140);

      ctx.fillStyle = '#f5ecd5';
      ctx.font = '10px "Pixelify Sans", monospace';
      ctx.fillText(`Điểm đạt được: ${this.state.score}`, this.logicWidth / 2, 165);
      ctx.fillText('Bấm vào màn hình hoặc [Space] để chơi lại', this.logicWidth / 2, 185);
    } else if (this.state && this.state.isPaused) {
      ctx.fillStyle = 'rgba(15, 22, 34, 0.65)';
      ctx.fillRect(0, 0, this.logicWidth, this.logicHeight);

      ctx.fillStyle = '#fbc531';
      ctx.font = 'bold 16px "Pixelify Sans", monospace';
      ctx.textAlign = 'center';
      ctx.fillText('TẠM DỪNG', this.logicWidth / 2, 150);
      ctx.font = '10px "Pixelify Sans", monospace';
      ctx.fillStyle = '#f5ecd5';
      ctx.fillText('Bấm [P] hoặc chạm màn hình để tiếp tục', this.logicWidth / 2, 175);
    }

    // 7. Ambient day/night tint
    farmEnvironment.renderOverlay(ctx, this.logicWidth, this.logicHeight);

    ctx.restore();
  }

  destroy() {
    if (this.animFrame) cancelAnimationFrame(this.animFrame);
    if (this.resizeObserver) this.resizeObserver.disconnect();
  }
}

export function initSnake(canvas, dotNetRef) {
  return new SnakeRenderer(canvas, dotNetRef);
}
