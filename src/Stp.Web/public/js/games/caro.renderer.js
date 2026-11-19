// Caro Farm HTML5 Canvas 2D Renderer (ES Module)
// Logic Size: 15x15 tiles * 16px = 240x240 logic pixels.
// Uses integer scaling, offscreen cached tiles, procedural spriteAtlas, particles, and sfx.

import { spriteAtlas } from './shared/spriteAtlas.js';
import { ParticleSystem } from './shared/particles.js';
import { farmEnvironment } from './shared/environment.js';
import { sfx } from './shared/sfx.js';

export class CaroRenderer {
  constructor(canvas, dotNetRef) {
    this.canvas = canvas;
    this.ctx = canvas.getContext('2d');
    this.dotNetRef = dotNetRef;

    this.logicWidth = 240;
    this.logicHeight = 240;
    this.tileSize = 16;
    this.boardSize = 15;

    this.state = null;
    this.animFrame = null;
    this.particles = new ParticleSystem(300);

    // Piece growth animations: key "r,c" -> { frame: 0, maxFrame: 4, startTime }
    this.growthAnims = new Map();
    // Hover ghost
    this.hoverTile = null;

    // Static background cache
    this.bgCanvas = null;

    this.lastTime = performance.now();
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

    this.resizeObserver = new ResizeObserver(() => {
      this.updateScale();
    });
    this.resizeObserver.observe(this.canvas.parentElement || this.canvas);
  }

  updateScale() {
    const parent = this.canvas.parentElement;
    const parentWidth = parent ? parent.clientWidth : window.innerWidth;
    const parentHeight = parent ? parent.clientHeight : window.innerHeight;

    // Largest integer scale that fits (x1, x2, x3, x4)
    const scaleX = Math.floor(parentWidth / this.logicWidth) || 1;
    const scaleY = Math.floor(parentHeight / this.logicHeight) || 1;
    this.scale = Math.max(1, Math.min(scaleX, scaleY));

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

    const soil = spriteAtlas.get('tile_soil');
    for (let r = 0; r < this.boardSize; r++) {
      for (let c = 0; c < this.boardSize; c++) {
        if (soil) {
          bCtx.drawImage(soil, c * this.tileSize, r * this.tileSize);
        } else {
          bCtx.fillStyle = (r + c) % 2 === 0 ? '#8b5028' : '#73411e';
          bCtx.fillRect(c * this.tileSize, r * this.tileSize, this.tileSize, this.tileSize);
        }
      }
    }
  }

  bindEvents() {
    this.canvas.addEventListener('mousemove', (e) => this.onMouseMove(e));
    this.canvas.addEventListener('mouseleave', () => { this.hoverTile = null; });
    this.canvas.addEventListener('click', (e) => this.onClick(e));
    this.canvas.addEventListener('touchstart', (e) => this.onTouchStart(e), { passive: false });
  }

  getTileFromEvent(e) {
    const rect = this.canvas.getBoundingClientRect();
    const clientX = e.touches ? e.touches[0].clientX : e.clientX;
    const clientY = e.touches ? e.touches[0].clientY : e.clientY;
    const x = Math.floor((clientX - rect.left) / this.scale);
    const y = Math.floor((clientY - rect.top) / this.scale);
    const c = Math.floor(x / this.tileSize);
    const r = Math.floor(y / this.tileSize);
    if (r >= 0 && r < this.boardSize && c >= 0 && c < this.boardSize) {
      return { r, c };
    }
    return null;
  }

  onMouseMove(e) {
    this.hoverTile = this.getTileFromEvent(e);
  }

  onClick(e) {
    const tile = this.getTileFromEvent(e);
    if (!tile) return;
    if (this.state && this.state.board && this.state.board[tile.r][tile.c] === ' ') {
      sfx.playMove();
      if (this.dotNetRef?.invokeMethodAsync) {
        this.dotNetRef.invokeMethodAsync('OnCellClicked', tile.r, tile.c);
      } else if (typeof this.dotNetRef?.onCellClicked === 'function') {
        this.dotNetRef.onCellClicked(tile.r, tile.c);
      }
    }
  }

  onTouchStart(e) {
    e.preventDefault();
    const tile = this.getTileFromEvent(e);
    if (tile && this.state && this.state.board && this.state.board[tile.r][tile.c] === ' ') {
      sfx.playMove();
      if (this.dotNetRef?.invokeMethodAsync) {
        this.dotNetRef.invokeMethodAsync('OnCellClicked', tile.r, tile.c);
      } else if (typeof this.dotNetRef?.onCellClicked === 'function') {
        this.dotNetRef.onCellClicked(tile.r, tile.c);
      }
    }
  }

  updateState(dto) {
    // Check if new move placed to start growth animation & particles
    if (dto.lastMove) {
      const key = `${dto.lastMove.r},${dto.lastMove.c}`;
      if (!this.growthAnims.has(key)) {
        this.growthAnims.set(key, { frame: 0, startTime: performance.now() });
        this.particles.emitBurst(
          dto.lastMove.c * this.tileSize + 8,
          dto.lastMove.r * this.tileSize + 8,
          8,
          dto.lastMove.player === 'X' ? ['#ffcc00', '#e69900', '#78c657'] : ['#f36e21', '#88d75e', '#ffffff']
        );
      }
    }

    // Win fireworks
    if (dto.hasWon && (!this.state || !this.state.hasWon)) {
      sfx.playWin();
      this.particles.triggerShake(5);
      if (dto.winningLine) {
        dto.winningLine.forEach((p) => {
          this.particles.emitBurst(p.c * this.tileSize + 8, p.r * this.tileSize + 8, 16);
        });
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

    // Update piece growth animations (5 frames, ~70ms each)
    for (const [key, anim] of this.growthAnims.entries()) {
      const elapsed = time - anim.startTime;
      anim.frame = Math.min(4, Math.floor(elapsed / 70));
    }
  }

  render(time) {
    const ctx = this.ctx;
    ctx.save();
    ctx.imageSmoothingEnabled = false;

    // Apply screen shake
    const shake = this.particles.getShakeOffset();
    ctx.translate(shake.x * this.scale, shake.y * this.scale);
    ctx.scale(this.scale, this.scale);

    // 1. Draw static soil background
    if (this.bgCanvas) {
      ctx.drawImage(this.bgCanvas, 0, 0);
    }

    // 2. Draw hover ghost piece
    if (this.hoverTile && this.state && !this.state.isGameOver && !this.state.isThinking) {
      const { r, c } = this.hoverTile;
      if (this.state.board && this.state.board[r][c] === ' ') {
        ctx.globalAlpha = 0.45;
        const spriteName = this.state.currentTurn === 'X' ? 'caro_sunflower_3' : 'caro_carrot_3';
        const spr = spriteAtlas.get(spriteName);
        if (spr) {
          ctx.drawImage(spr, c * this.tileSize, r * this.tileSize);
        }
        ctx.globalAlpha = 1.0;
      }
    }

    // 3. Draw placed pieces (Sunflower X / Carrot O)
    if (this.state && this.state.board) {
      const winSet = new Set(
        (this.state.winningLine || []).map((p) => `${p.r},${p.c}`)
      );

      for (let r = 0; r < this.boardSize; r++) {
        const rowStr = this.state.board[r];
        for (let c = 0; c < this.boardSize; c++) {
          const cell = rowStr[c];
          if (cell === ' ') continue;

          const key = `${r},${c}`;
          const isWinPiece = winSet.has(key);
          const anim = this.growthAnims.get(key);
          let frame = anim ? anim.frame : 4;

          // If piece is in winning 5-in-a-row line, make it bob/dance
          let bobY = 0;
          if (isWinPiece) {
            bobY = Math.round(Math.sin((time * 0.01) + (r + c)) * 2);
            frame = (Math.floor(time / 140) % 2 === 0) ? 3 : 4;
          }

          const prefix = cell === 'X' ? 'caro_sunflower' : 'caro_carrot';
          const spr = spriteAtlas.get(`${prefix}_${frame}`);
          if (spr) {
            ctx.drawImage(spr, c * this.tileSize, r * this.tileSize + bobY);
          }

          // Highlight last move with golden pulsating ring
          if (this.state.lastMove && this.state.lastMove.r === r && this.state.lastMove.c === c) {
            const pulse = 0.4 + 0.3 * Math.sin(time * 0.008);
            ctx.strokeStyle = `rgba(255, 215, 0, ${pulse})`;
            ctx.lineWidth = 1;
            ctx.strokeRect(c * this.tileSize + 1, r * this.tileSize + 1, this.tileSize - 2, this.tileSize - 2);
          }
        }
      }
    }

    // 4. Render particles (bursts, leaves, fireworks)
    this.particles.render(ctx);

    // 5. "Máy đang nghĩ..." banner with Scratching Chicken
    if (this.state && this.state.isThinking) {
      const chickFrame = Math.floor(time / 180) % 4;
      const chickSpr = spriteAtlas.get(`caro_thinking_chicken_${chickFrame}`);

      // Cozy wooden banner
      ctx.fillStyle = 'rgba(44, 24, 16, 0.88)';
      ctx.fillRect(40, 95, 160, 48);
      ctx.fillStyle = '#f5ecd5';
      ctx.strokeRect(41.5, 96.5, 157, 45);

      if (chickSpr) {
        ctx.drawImage(chickSpr, 50, 110);
      }

      ctx.font = 'bold 10px "Pixelify Sans", monospace';
      ctx.fillStyle = '#ffcc00';
      ctx.fillText('Máy đang suy nghĩ...', 76, 122);
      ctx.font = '8px "Pixelify Sans", monospace';
      ctx.fillStyle = '#f5ecd5';
      ctx.fillText('Độ khó: ' + (this.state.aiLevel || 'Medium'), 76, 134);
    }

    // 6. Ambient day/night tint
    farmEnvironment.renderOverlay(ctx, this.logicWidth, this.logicHeight);

    ctx.restore();
  }

  destroy() {
    if (this.animFrame) {
      cancelAnimationFrame(this.animFrame);
    }
    if (this.resizeObserver) {
      this.resizeObserver.disconnect();
    }
  }
}

export function initCaro(canvas, dotNetRef) {
  return new CaroRenderer(canvas, dotNetRef);
}
