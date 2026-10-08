// TriviaSync Web Audio Synthesizer
// Provides snappy, zero-latency sounds for Kahoot-style gameplay

class SoundEffects {
    constructor() {
        this.ctx = null;
        this.enabled = true;
    }

    init() {
        if (!this.ctx) {
            const AudioCtx = window.AudioContext || window.webkitAudioContext;
            if (AudioCtx) {
                this.ctx = new AudioCtx();
            }
        }
        if (this.ctx && this.ctx.state === 'suspended') {
            this.ctx.resume();
        }
    }

    playTone(freq, type = 'sine', duration = 0.1, gainVal = 0.15) {
        if (!this.enabled) return;
        this.init();
        if (!this.ctx) return;

        try {
            const osc = this.ctx.createOscillator();
            const gain = this.ctx.createGain();

            osc.type = type;
            osc.frequency.setValueAtTime(freq, this.ctx.currentTime);

            gain.gain.setValueAtTime(gainVal, this.ctx.currentTime);
            gain.gain.exponentialRampToValueAtTime(0.0001, this.ctx.currentTime + duration);

            osc.connect(gain);
            gain.connect(this.ctx.destination);

            osc.start();
            osc.stop(this.ctx.currentTime + duration);
        } catch (e) {
            // Audio error silently ignored
        }
    }

    // Tick sound for timer
    tick() {
        this.playTone(800, 'triangle', 0.05, 0.08);
    }

    // Hurry up tick when timer < 5s
    hurryTick() {
        this.playTone(1200, 'sawtooth', 0.07, 0.12);
    }

    // Answer tile clicked
    click() {
        this.playTone(600, 'sine', 0.08, 0.2);
    }

    // Correct answer fanfare
    correct() {
        this.init();
        if (!this.enabled || !this.ctx) return;
        const notes = [523.25, 659.25, 783.99, 1046.50]; // C5, E5, G5, C6
        notes.forEach((freq, idx) => {
            setTimeout(() => {
                this.playTone(freq, 'sine', 0.25, 0.2);
            }, idx * 90);
        });
    }

    // Incorrect answer buzzer
    wrong() {
        this.init();
        if (!this.enabled || !this.ctx) return;
        this.playTone(160, 'sawtooth', 0.35, 0.25);
        setTimeout(() => {
            this.playTone(130, 'sawtooth', 0.4, 0.25);
        }, 120);
    }

    // Podium celebration chime
    podium() {
        this.init();
        if (!this.enabled || !this.ctx) return;
        const melody = [440, 554.37, 659.25, 880, 659.25, 880];
        melody.forEach((freq, idx) => {
            setTimeout(() => {
                this.playTone(freq, 'triangle', 0.3, 0.25);
            }, idx * 130);
        });
    }
}

window.sounds = new SoundEffects();
document.addEventListener('click', () => window.sounds.init(), { once: true });
