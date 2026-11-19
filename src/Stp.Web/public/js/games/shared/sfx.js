// sfx.js - WebAudio Procedural Chip-tune Sound Synthesizer (Chỉ thị [11])
// Tự tạo âm thanh retro 8-bit trực tiếp bằng code, không cần file wav/mp3 ngoài. Mặc định TẮT âm thanh.

class SoundEngine {
    constructor() {
        this.ctx = null;
        this.isMuted = true; // Mặc định tắt âm thanh theo quy định văn phòng
        this.volume = 0.25;
    }

    init() {
        if (!this.ctx && typeof window !== 'undefined') {
            const AudioContext = window.AudioContext || window.webkitAudioContext;
            if (AudioContext) {
                this.ctx = new AudioContext();
            }
        }
    }

    setMuted(muted) {
        this.isMuted = muted;
        if (!muted && (!this.ctx || this.ctx.state === 'suspended')) {
            this.init();
            if (this.ctx && this.ctx.state === 'suspended') {
                this.ctx.resume();
            }
        }
        try {
            localStorage.setItem('game:sound:muted', muted ? 'true' : 'false');
        } catch {}
    }

    toggleMute() {
        this.setMuted(!this.isMuted);
        return this.isMuted;
    }

    playTone(freq, type = 'square', duration = 0.1, delay = 0, startVol = 1, endVol = 0.01) {
        if (this.isMuted) return;
        this.init();
        if (!this.ctx) return;

        const now = this.ctx.currentTime + delay;
        const osc = this.ctx.createOscillator();
        const gain = this.ctx.createGain();

        osc.type = type;
        osc.frequency.setValueAtTime(freq, now);

        gain.gain.setValueAtTime(startVol * this.volume, now);
        gain.gain.exponentialRampToValueAtTime(Math.max(0.0001, endVol * this.volume), now + duration);

        osc.connect(gain);
        gain.connect(this.ctx.destination);

        osc.start(now);
        osc.stop(now + duration);
    }

    // Nước đi / Đặt quân cờ
    playMove() {
        this.playTone(320, 'triangle', 0.06, 0, 0.8, 0.01);
        this.playTone(480, 'sine', 0.08, 0.04, 0.5, 0.01);
    }

    // Ăn mồi / Thu hoạch
    playEat() {
        this.playTone(440, 'square', 0.06, 0, 0.6, 0.1);
        this.playTone(660, 'square', 0.06, 0.05, 0.7, 0.1);
        this.playTone(880, 'square', 0.12, 0.10, 0.8, 0.01);
    }

    // Gộp nông sản trong 2048
    playMerge() {
        this.playTone(330, 'triangle', 0.08, 0, 0.7, 0.2);
        this.playTone(495, 'triangle', 0.08, 0.06, 0.8, 0.2);
        this.playTone(660, 'sine', 0.15, 0.12, 0.9, 0.01);
    }

    // Đổ xúc xắc
    playDiceRoll() {
        for (let i = 0; i < 5; i++) {
            const freq = 200 + Math.random() * 200;
            this.playTone(freq, 'square', 0.03, i * 0.05, 0.4, 0.01);
        }
    }

    // Đá quân gà đối thủ về chuồng
    playKick() {
        this.playTone(180, 'square', 0.12, 0, 1.0, 0.1);
        this.playTone(120, 'sawtooth', 0.18, 0.06, 0.9, 0.01);
    }

    // Gà nhảy từng bước
    playHop() {
        this.playTone(400, 'sine', 0.05, 0, 0.5, 0.01);
    }

    // Thắng trận / Ăn mừng
    playWin() {
        const notes = [523, 659, 784, 1046];
        notes.forEach((freq, idx) => {
            this.playTone(freq, 'triangle', 0.18, idx * 0.12, 0.8, 0.01);
        });
    }

    // Thua cuộc / Game over
    playGameOver() {
        const notes = [440, 415, 392, 349];
        notes.forEach((freq, idx) => {
            this.playTone(freq, 'sawtooth', 0.2, idx * 0.15, 0.6, 0.01);
        });
    }
}

export const SFX = new SoundEngine();
