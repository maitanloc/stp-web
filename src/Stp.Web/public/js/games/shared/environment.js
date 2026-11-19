// Day/Night Ambient Tint and Farm Environmental Effects

export class FarmEnvironment {
  constructor() {
    this.timeOfDay = this.detectTimeOfDay();
  }

  detectTimeOfDay() {
    const hours = new Date().getHours() + new Date().getMinutes() / 60;
    if (hours >= 6 && hours < 16.5) return 'day';
    if (hours >= 16.5 && hours < 18.5) return 'sunset';
    return 'night';
  }

  getWindOffset(timeMs, offset = 0) {
    // Gentle sine wave wind swaying (8-12 fps feel)
    return Math.round(Math.sin((timeMs + offset * 200) * 0.003) * 1.2);
  }

  renderOverlay(ctx, width, height) {
    const current = this.detectTimeOfDay();
    if (current === 'day') {
      // Clear morning/afternoon, subtle warm sun glow
      ctx.fillStyle = 'rgba(255, 235, 170, 0.03)';
      ctx.fillRect(0, 0, width, height);
      return;
    }

    if (current === 'sunset') {
      // Warm golden hour / dusk amber tint
      ctx.fillStyle = 'rgba(240, 110, 30, 0.12)';
      ctx.fillRect(0, 0, width, height);
      return;
    }

    if (current === 'night') {
      // Deep cozy navy night overlay
      ctx.fillStyle = 'rgba(20, 28, 56, 0.28)';
      ctx.fillRect(0, 0, width, height);
    }
  }
}

export const farmEnvironment = new FarmEnvironment();
