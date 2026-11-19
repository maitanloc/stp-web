// Ludo Farm (Cờ Cá Ngựa Nông Trại) HTML5 Canvas 2D Renderer (ES Module)
// Logic Size: 240x240 logic pixels (15x15 grid * 16px).
// Cross-shaped farm tracks, 4 corner barns, hopping chicks, rolling dice, feather particles.

import { spriteAtlas } from './shared/spriteAtlas.js';
import { ParticleSystem } from './shared/particles.js';
import { farmEnvironment } from './shared/environment.js';
import { sfx } from './shared/sfx.js';

export class LudoRenderer {
  constructor(canvas, dotNetRef) {
    this.canvas = canvas;
    this.ctx = canvas.getContext('2d');
    this.dotNetRef = dotNetRef;

    this.logicWidth = 240;
    this.logicHeight = 240;
    this.tileSize = 16;
    this.gridSize = 15;

    this.state = null;
    this.legalMoves = [];
    this.animFrame = null;
    this.particles = new ParticleSystem(300);

    // Map 52 track coordinates (r, c in 15x15 grid)
    this.trackCoords = this.generateTrackCoords();
    // Map 4 corner barn coordinates (4 chicks per player)
    this.barnCoords = this.generateBarnCoords();
    // Map 4 home tracks (6 steps per color leading to center)
    this.homeCoords = this.generateHomeCoords();

    // Dice rolling animation state
    this.diceAnim = { isRolling: false, startTime: 0, duration: 600, targetFace: 1, currentFace: 1 };
    // Hopping animations for pieces: key "color_pieceId" -> { startPos, targetPos, startTime, duration }
    this.hopAnims = new Map();

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

  // Tạo tọa độ 52 ô vòng quanh hình chữ thập chuẩn Ludo
  generateTrackCoords() {
    const coords = [];
    // Red Start at (6, 1) -> 0..51 clockwise
    const path = [
      // Top arm going up (left edge): (6,1) to (6,5)
      [6,1], [6,2], [6,3], [6,4], [6,5],
      // Going into top branch: (5,6) to (0,6)
      [5,6], [4,6], [3,6], [2,6], [1,6], [0,6],
      // Crossing top edge
      [0,7], [0,8],
      // Going down top branch: (1,8) to (5,8)
      [1,8], [2,8], [3,8], [4,8], [5,8],
      // Blue branch going right: (6,9) to (6,14)
      [6,9], [6,10], [6,11], [6,12], [6,13], [6,14],
      // Right edge
      [7,14], [8,14],
      // Going left bottom branch: (8,13) to (8,9)
      [8,13], [8,12], [8,11], [8,10], [8,9],
      // Yellow branch going down: (9,8) to (14,8)
      [9,8], [10,8], [11,8], [12,8], [13,8], [14,8],
      // Bottom edge
      [14,7], [14,6],
      // Going up bottom branch: (13,6) to (9,6)
      [13,6], [12,6], [11,6], [10,6], [9,6],
      // Green branch going left: (8,5) to (8,0)
      [8,5], [8,4], [8,3], [8,2], [8,1], [8,0],
      // Left edge
      [7,0], [6,0]
    ];
    return path.map(([r, c]) => ({ r, c }));
  }

  // Tọa độ 4 chuồng ở 4 góc (mỗi chuồng chứa 4 vị trí gà con)
  generateBarnCoords() {
    return [
      // Đỏ (Top-Left): góc (1,1) đến (4,4)
      [{r:2, c:2}, {r:2, c:4}, {r:4, c:2}, {r:4, c:4}],
      // Lam (Top-Right): góc (1,10) đến (4,13)
      [{r:2, c:10}, {r:2, c:12}, {r:4, c:10}, {r:4, c:12}],
      // Vàng (Bottom-Right): góc (10,10) đến (13,13)
      [{r:10, c:10}, {r:10, c:12}, {r:12, c:10}, {r:12, c:12}],
      // Lục (Bottom-Left): góc (10,1) đến (13,4)
      [{r:10, c:2}, {r:10, c:4}, {r:12, c:2}, {r:12, c:4}]
    ];
  }

  // Tọa độ 6 bậc đường về chuồng nhà của 4 màu
  generateHomeCoords() {
    return [
      // Đỏ: từ trái vào tâm (hàng 7, cột 1..6)
      [{r:7,c:1}, {r:7,c:2}, {r:7,c:3}, {r:7,c:4}, {r:7,c:5}, {r:7,c:6}],
      // Lam: từ trên xuống tâm (cột 7, hàng 1..6)
      [{r:1,c:7}, {r:2,c:7}, {r:3,c:7}, {r:4,c:7}, {r:5,c:7}, {r:6,c:7}],
      // Vàng: từ phải vào tâm (hàng 7, cột 13..8)
      [{r:7,c:13}, {r:7,c:12}, {r:7,c:11}, {r:7,c:10}, {r:7,c:9}, {r:7,c:8}],
      // Lục: từ dưới lên tâm (cột 7, hàng 13..8)
      [{r:13,c:7}, {r:12,c:7}, {r:11,c:7}, {r:10,c:7}, {r:9,c:7}, {r:8,c:7}]
    ];
  }

  createStaticBackground() {
    this.bgCanvas = document.createElement('canvas');
    this.bgCanvas.width = this.logicWidth;
    this.bgCanvas.height = this.logicHeight;
    const bCtx = this.bgCanvas.getContext('2d');
    bCtx.imageSmoothingEnabled = false;

    // 1. Phủ cỏ toàn sân
    const grass = spriteAtlas.get('tile_grass');
    for (let r = 0; r < this.gridSize; r++) {
      for (let c = 0; c < this.gridSize; c++) {
        if (grass) bCtx.drawImage(grass, c * 16, r * 16);
        else {
          bCtx.fillStyle = (r + c) % 2 === 0 ? '#5fa838' : '#579e32';
          bCtx.fillRect(c * 16, r * 16, 16, 16);
        }
      }
    }

    // 2. Vẽ 4 góc chuồng gà gỗ
    const barnColors = ['#d9534f', '#337ab7', '#f0ad4e', '#5cb85c'];
    const barnBorders = ['#902020', '#184a75', '#996611', '#2d682d'];
    const barnAreas = [
      { r: 1, c: 1 }, { r: 1, c: 9 }, { r: 9, c: 9 }, { r: 9, c: 1 }
    ];

    barnAreas.forEach((area, i) => {
      bCtx.fillStyle = '#613b19';
      bCtx.fillRect(area.c * 16 - 2, area.r * 16 - 2, 5 * 16 + 4, 5 * 16 + 4);
      bCtx.fillStyle = '#c2884a';
      bCtx.fillRect(area.c * 16, area.r * 16, 5 * 16, 5 * 16);
      bCtx.fillStyle = barnColors[i];
      bCtx.fillRect(area.c * 16 + 4, area.r * 16 + 4, 5 * 16 - 8, 5 * 16 - 8);
    });

    // 3. Vẽ 52 ô đường đi ruộng
    const pathSoil = spriteAtlas.get('tile_soil');
    this.trackCoords.forEach((pt, idx) => {
      if (pathSoil) {
        bCtx.drawImage(pathSoil, pt.c * 16, pt.r * 16);
      } else {
        bCtx.fillStyle = '#875026';
        bCtx.fillRect(pt.c * 16, pt.r * 16, 16, 16);
      }
      // Viền ô
      bCtx.strokeStyle = '#573114';
      bCtx.lineWidth = 1;
      bCtx.strokeRect(pt.c * 16 + 0.5, pt.r * 16 + 0.5, 15, 15);

      // Điểm xuất phát của 4 màu
      if (idx === 0) { bCtx.fillStyle = 'rgba(217, 83, 79, 0.4)'; bCtx.fillRect(pt.c * 16, pt.r * 16, 16, 16); }
      if (idx === 13) { bCtx.fillStyle = 'rgba(51, 122, 183, 0.4)'; bCtx.fillRect(pt.c * 16, pt.r * 16, 16, 16); }
      if (idx === 26) { bCtx.fillStyle = 'rgba(240, 173, 78, 0.4)'; bCtx.fillRect(pt.c * 16, pt.r * 16, 16, 16); }
      if (idx === 39) { bCtx.fillStyle = 'rgba(92, 184, 92, 0.4)'; bCtx.fillRect(pt.c * 16, pt.r * 16, 16, 16); }
    });

    // 4. Vẽ 4 đường bậc về chuồng nhà
    this.homeCoords.forEach((homeSteps, colorIdx) => {
      homeSteps.forEach((pt, step) => {
        bCtx.fillStyle = barnColors[colorIdx];
        bCtx.globalAlpha = 0.55;
        bCtx.fillRect(pt.c * 16 + 1, pt.r * 16 + 1, 14, 14);
        bCtx.globalAlpha = 1.0;
        bCtx.strokeStyle = barnBorders[colorIdx];
        bCtx.strokeRect(pt.c * 16 + 0.5, pt.r * 16 + 0.5, 15, 15);
      });
    });

    // 5. Chuồng đích trung tâm (7,7)
    bCtx.fillStyle = '#fbc531';
    bCtx.fillRect(7 * 16 - 2, 7 * 16 - 2, 20, 20);
    bCtx.strokeStyle = '#875026';
    bCtx.strokeRect(7 * 16 - 1.5, 7 * 16 - 1.5, 19, 19);
  }

  bindEvents() {
    this.canvas.addEventListener('click', (e) => this.onClick(e));
  }

  onClick(e) {
    const rect = this.canvas.getBoundingClientRect();
    const x = Math.floor((e.clientX - rect.left) / this.scale);
    const y = Math.floor((e.clientY - rect.top) / this.scale);
    const c = Math.floor(x / this.tileSize);
    const r = Math.floor(y / this.tileSize);

    // 1. Kiểm tra click vào xúc xắc giữa sân để đổ
    if (r >= 6 && r <= 8 && c >= 6 && c <= 8) {
      if (this.state && this.state.canRoll) {
        if (this.dotNetRef?.invokeMethodAsync) {
          this.dotNetRef.invokeMethodAsync('OnRollDiceClicked');
        } else if (typeof this.dotNetRef?.onRollDiceClicked === 'function') {
          this.dotNetRef.onRollDiceClicked();
        }
        return;
      }
    }

    // 2. Kiểm tra click vào các quân cờ có nước đi hợp lệ
    if (!this.state || !this.legalMoves || this.legalMoves.length === 0) return;

    for (const move of this.legalMoves) {
      const piecePos = this.getPieceScreenCoords(move.player, move.pieceId);
      if (piecePos) {
        const pr = Math.floor(piecePos.y / 16);
        const pc = Math.floor(piecePos.x / 16);
        if (Math.abs(r - pr) <= 1 && Math.abs(c - pc) <= 1) {
          sfx.playHop();
          if (this.dotNetRef?.invokeMethodAsync) {
            this.dotNetRef.invokeMethodAsync('OnSelectMove', move.pieceId);
          } else if (typeof this.dotNetRef?.onSelectMove === 'function') {
            this.dotNetRef.onSelectMove(move.pieceId);
          }
          return;
        }
      }
    }
  }

  getPieceScreenCoords(playerColor, pieceId) {
    if (!this.state || !this.state.players) return null;
    const player = this.state.players[playerColor];
    if (!player || !player.pieces) return null;
    const piece = player.pieces[pieceId];
    if (!piece) return null;

    if (piece.state === 0) {
      // In barn
      const pt = this.barnCoords[playerColor][pieceId];
      return { x: pt.c * 16, y: pt.r * 16 };
    } else if (piece.state === 1) {
      // On track
      if (piece.trackPos >= 0 && piece.trackPos < this.trackCoords.length) {
        const pt = this.trackCoords[piece.trackPos];
        return { x: pt.c * 16, y: pt.r * 16 };
      }
    } else if (piece.state === 2) {
      // In home track
      if (piece.homeStep >= 0 && piece.homeStep < 6) {
        const pt = this.homeCoords[playerColor][piece.homeStep];
        return { x: pt.c * 16, y: pt.r * 16 };
      }
    } else if (piece.state === 3) {
      // Finished in center goal
      return { x: 7 * 16 + (pieceId % 2) * 6, y: 7 * 16 + Math.floor(pieceId / 2) * 6 };
    }
    return null;
  }

  updateState(dto, legalMoves = []) {
    // Check if new move happened
    if (dto.lastMove) {
      if (dto.lastMove.isKick) {
        sfx.playKick();
        this.particles.triggerShake(4);
        // Emitting feather burst
        const pos = this.trackCoords[dto.lastMove.targetPos];
        if (pos) {
          this.particles.emitBurst(pos.c * 16 + 8, pos.r * 16 + 8, 20, ['#ffffff', '#fff3cd', '#ffd27f']);
        }
      } else {
        sfx.playHop();
      }
    }

    if (dto.lastDiceRoll > 0 && (!this.state || this.state.lastDiceRoll !== dto.lastDiceRoll)) {
      this.triggerDiceRoll(dto.lastDiceRoll);
    }

    if (dto.isGameOver && (!this.state || !this.state.isGameOver)) {
      sfx.playWin();
      this.particles.emitBurst(120, 120, 30);
    }

    this.state = dto;
    this.legalMoves = legalMoves || [];
  }

  triggerDiceRoll(targetFace) {
    sfx.playDiceRoll();
    this.diceAnim = {
      isRolling: true,
      startTime: performance.now(),
      duration: 500,
      targetFace: targetFace,
      currentFace: 1
    };
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

    // Dice animation update
    if (this.diceAnim.isRolling) {
      const elapsed = time - this.diceAnim.startTime;
      if (elapsed >= this.diceAnim.duration) {
        this.diceAnim.isRolling = false;
        this.diceAnim.currentFace = this.diceAnim.targetFace;
      } else {
        // Shuffle random face while rolling
        this.diceAnim.currentFace = Math.floor(Math.random() * 6) + 1;
      }
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

    // 1. Draw static board background
    if (this.bgCanvas) {
      ctx.drawImage(this.bgCanvas, 0, 0);
    }

    // 2. Draw all player chicks
    const chickColors = ['red', 'blue', 'yellow', 'green'];
    if (this.state && this.state.players) {
      this.state.players.forEach((player) => {
        const colorName = chickColors[player.color];
        player.pieces.forEach((piece) => {
          const pos = this.getPieceScreenCoords(player.color, piece.id);
          if (!pos) return;

          // Check if piece has a legal move -> pulse yellow highlight
          const isMovable = this.legalMoves.some(m => m.player === player.color && m.pieceId === piece.id);
          let bob = 0;
          if (isMovable) {
            bob = Math.round(Math.sin(time * 0.012) * 2);
            // Glowing pulsing ring under chick
            ctx.fillStyle = 'rgba(255, 235, 59, 0.5)';
            ctx.beginPath();
            ctx.ellipse(pos.x + 8, pos.y + 13, 7, 3, 0, 0, Math.PI * 2);
            ctx.fill();
          } else {
            // Normal shadow
            ctx.fillStyle = 'rgba(0, 0, 0, 0.25)';
            ctx.beginPath();
            ctx.ellipse(pos.x + 8, pos.y + 14, 6, 2.5, 0, 0, Math.PI * 2);
            ctx.fill();
          }

          // Draw chick sprite
          const spr = spriteAtlas.get(`ludo_chick_${colorName}_idle`);
          if (spr) {
            ctx.drawImage(spr, pos.x, pos.y + bob);
          }
        });
      });
    }

    // 3. Draw Center Dice & Indicator
    const diceSpr = spriteAtlas.get(`ludo_dice_${this.diceAnim.isRolling ? this.diceAnim.currentFace : (this.state ? this.state.lastDiceRoll || 1 : 1)}`);
    if (diceSpr) {
      // Wood frame under dice
      ctx.fillStyle = '#613b19';
      ctx.fillRect(7 * 16 - 2, 7 * 16 - 2, 20, 20);
      ctx.drawImage(diceSpr, 7 * 16, 7 * 16);

      // If canRoll, draw flashing prompt
      if (this.state && this.state.canRoll) {
        const pulse = 0.5 + 0.5 * Math.sin(time * 0.008);
        ctx.strokeStyle = `rgba(255, 235, 59, ${pulse})`;
        ctx.lineWidth = 1;
        ctx.strokeRect(7 * 16 - 2.5, 7 * 16 - 2.5, 21, 21);
      }
    }

    // 4. Draw turn arrow pointing at active player
    if (this.state) {
      const activeColor = this.state.currentTurnColor;
      const barnPt = this.barnCoords[activeColor][0];
      const arrowY = barnPt.r * 16 - 6 + Math.round(Math.sin(time * 0.01) * 2);
      ctx.fillStyle = '#fbc531';
      ctx.beginPath();
      ctx.moveTo(barnPt.c * 16 + 8, arrowY + 6);
      ctx.lineTo(barnPt.c * 16 + 4, arrowY);
      ctx.lineTo(barnPt.c * 16 + 12, arrowY);
      ctx.closePath();
      ctx.fill();
    }

    // 5. Draw particles (feathers, sparks, leaves)
    this.particles.render(ctx);

    // 6. Day/night ambient overlay
    farmEnvironment.renderOverlay(ctx, this.logicWidth, this.logicHeight);

    ctx.restore();
  }

  destroy() {
    if (this.animFrame) cancelAnimationFrame(this.animFrame);
    if (this.resizeObserver) this.resizeObserver.disconnect();
  }
}

export function initLudo(canvas, dotNetRef) {
  return new LudoRenderer(canvas, dotNetRef);
}
