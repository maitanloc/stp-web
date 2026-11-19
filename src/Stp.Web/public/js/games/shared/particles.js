// Pixel-Art 2x2 Particle Engine for Stardew-inspired Games
// Max 300 particles to guarantee 60fps on office laptops.

export class ParticleSystem {
  constructor(maxParticles = 300) {
    this.maxParticles = maxParticles;
    this.particles = [];
    this.reducedMotion = false;
    this.screenShake = 0;
  }

  setReducedMotion(enabled) {
    this.reducedMotion = !!enabled;
  }

  triggerShake(intensity = 4) {
    if (this.reducedMotion) return;
    this.screenShake = Math.max(this.screenShake, intensity);
  }

  getShakeOffset() {
    if (this.screenShake <= 0) return { x: 0, y: 0 };
    const sx = Math.round((Math.random() - 0.5) * this.screenShake);
    const sy = Math.round((Math.random() - 0.5) * this.screenShake);
    return { x: sx, y: sy };
  }

  emitBurst(x, y, count = 12, colors = ['#ffcc00', '#f36e21', '#ffffff']) {
    if (this.reducedMotion) count = Math.min(count, 4);
    for (let i = 0; i < count; i++) {
      if (this.particles.length >= this.maxParticles) break;
      const angle = (Math.PI * 2 * i) / count + (Math.random() - 0.5) * 0.5;
      const speed = 0.5 + Math.random() * 1.5;
      const color = colors[Math.floor(Math.random() * colors.length)];
      this.particles.push({
        type: 'pixel',
        x,
        y,
        vx: Math.cos(angle) * speed,
        vy: Math.sin(angle) * speed,
        life: 20 + Math.floor(Math.random() * 15),
        maxLife: 35,
        color,
        size: 2,
      });
    }
  }

  emitScore(x, y, text = '+10', color = '#ffd124') {
    if (this.particles.length >= this.maxParticles) return;
    this.particles.push({
      type: 'text',
      x,
      y,
      vx: 0,
      vy: -0.6,
      life: 30,
      maxLife: 30,
      text,
      color,
    });
  }

  emitFeathers(x, y, count = 8, color = '#dc3545') {
    if (this.reducedMotion) count = 3;
    for (let i = 0; i < count; i++) {
      if (this.particles.length >= this.maxParticles) break;
      const angle = Math.random() * Math.PI * 2;
      const speed = 0.6 + Math.random() * 1.2;
      this.particles.push({
        type: 'feather',
        x,
        y,
        vx: Math.cos(angle) * speed,
        vy: Math.sin(angle) * speed - 0.5,
        gravity: 0.04,
        life: 25 + Math.floor(Math.random() * 10),
        maxLife: 35,
        color,
      });
    }
  }

  emitAmbient(width, height) {
    if (this.reducedMotion) return;
    // Ambient falling leaf or firefly occasionally
    if (Math.random() < 0.04 && this.particles.length < this.maxParticles) {
      this.particles.push({
        type: 'leaf',
        x: Math.random() * width,
        y: -4,
        vx: (Math.random() - 0.3) * 0.4,
        vy: 0.3 + Math.random() * 0.4,
        life: 180,
        maxLife: 180,
        color: Math.random() > 0.5 ? '#78c657' : '#e69900',
      });
    }
  }

  update() {
    if (this.screenShake > 0) {
      this.screenShake *= 0.82;
      if (this.screenShake < 0.3) this.screenShake = 0;
    }

    for (let i = this.particles.length - 1; i >= 0; i--) {
      const p = this.particles[i];
      p.x += p.vx;
      p.y += p.vy;
      if (p.gravity) {
        p.vy += p.gravity;
      }
      p.life--;
      if (p.life <= 0) {
        this.particles.splice(i, 1);
      }
    }
  }

  render(ctx) {
    const origSmoothing = ctx.imageSmoothingEnabled;
    ctx.imageSmoothingEnabled = false;

    for (const p of this.particles) {
      const alpha = Math.max(0, p.life / p.maxLife);
      ctx.globalAlpha = alpha;

      const px = Math.round(p.x);
      const py = Math.round(p.y);

      if (p.type === 'pixel') {
        ctx.fillStyle = p.color;
        ctx.fillRect(px, py, p.size || 2, p.size || 2);
      } else if (p.type === 'feather') {
        ctx.fillStyle = p.color;
        ctx.fillRect(px, py, 3, 2);
        ctx.fillStyle = '#ffffff';
        ctx.fillRect(px + 1, py, 1, 2);
      } else if (p.type === 'leaf') {
        ctx.fillStyle = p.color;
        ctx.fillRect(px, py, 2, 2);
        ctx.fillRect(px + 1, py + 1, 1, 1);
      } else if (p.type === 'text') {
        ctx.font = 'bold 9px "Pixelify Sans", monospace';
        ctx.fillStyle = '#2c1810'; // 1px outline/shadow
        ctx.fillText(p.text, px + 1, py + 1);
        ctx.fillStyle = p.color;
        ctx.fillText(p.text, px, py);
      }
    }

    ctx.globalAlpha = 1.0;
    ctx.imageSmoothingEnabled = origSmoothing;
  }
}
