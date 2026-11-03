// Makes the trailer's sound track from film.csv: a generated music bed plus guns, hits, explosions and engines.
// node sound.js <film.csv> <out.wav>
const fs = require('fs');
const [csvPath, outPath] = process.argv.slice(2);
const rows = fs.readFileSync(csvPath, 'utf8').trim().split(/\r?\n/).slice(1).map(l => {
  const c = l.split(',');
  return { frame: +c[0], shot: +c[1], cam: c[2], speed: +c[3], guns: +c[4], hits: +c[5], kills: +c[6], title: c[7] || '' };
});
const FPS = 30, SR = 48000;
const nFrames = rows.length;
const dur = nFrames / FPS;
const N = Math.ceil(dur * SR);
const L = new Float32Array(N), R = new Float32Array(N);
const T = f => Math.round(f / FPS * SR);

let seed = 12345;
const rnd = () => { seed = (seed * 1664525 + 1013904223) >>> 0; return seed / 4294967296; };
const noise = () => rnd() * 2 - 1;
function add(i, v, pan = 0) { if (i < 0 || i >= N) return; L[i] += v * Math.min(1, 1 - pan); R[i] += v * Math.min(1, 1 + pan); }

// ---------- where things happen ----------
const endFrame = rows.findIndex(r => r.cam === 'end');
const slow = rows.map(r => r.speed < 0.99);
const titleFrames = rows.filter(r => r.title && r.title !== 'endcard').map(r => r.frame);
const groove0 = rows.findIndex(r => r.shot >= 4);   // the music proper starts after the four title words
const cuts = []; for (let i = 1; i < nFrames; i++) if (rows[i].shot !== rows[i - 1].shot) cuts.push(i);

// ---------- music ----------
const BPM = 132, beat = 60 / BPM;
const bar = beat * 4;
// D minor: Dm Bb F C, a bar each
const roots = [38, 34, 41, 36];       // MIDI notes (D2, Bb1, F2, C2)
const chords = [[50, 53, 57], [46, 50, 53], [53, 57, 60], [48, 52, 55]];
const hz = m => 440 * Math.pow(2, (m - 69) / 12);
const musicStart = groove0 / FPS;
const musicEnd = (endFrame > 0 ? endFrame : nFrames) / FPS;

// how much of the full band plays at time t: ducked under slow motion, building in the intro
const slowAt = t => { const f = Math.floor(t * FPS); return f >= 0 && f < nFrames && slow[f]; };
function kick(t0, g) { const i0 = Math.round(t0 * SR); for (let k = 0; k < SR * 0.35; k++) { const t = k / SR; const f = 45 + 110 * Math.exp(-t * 28); const ph = 2 * Math.PI * (45 * t + 110 * (1 - Math.exp(-t * 28)) / 28); add(i0 + k, g * Math.sin(ph) * Math.exp(-t * 7)); } }
function snare(t0, g) { const i0 = Math.round(t0 * SR); let lp = 0; for (let k = 0; k < SR * 0.25; k++) { const t = k / SR; const n = noise(); lp += 0.5 * (n - lp); add(i0 + k, g * ((n - lp) * 0.8 * Math.exp(-t * 18) + 0.4 * Math.sin(2 * Math.PI * 190 * t) * Math.exp(-t * 30)), 0.1); } }
function hat(t0, g) { const i0 = Math.round(t0 * SR); let lp = 0; for (let k = 0; k < SR * 0.05; k++) { const t = k / SR; const n = noise(); lp += 0.8 * (n - lp); add(i0 + k, g * (n - lp) * Math.exp(-t * 90), -0.3); } }
function saw(ph) { return 2 * (ph - Math.floor(ph + 0.5)); }
function bassNote(t0, len, m, g) {
  const i0 = Math.round(t0 * SR), f = hz(m); let lp = 0;
  for (let k = 0; k < SR * len; k++) { const t = k / SR; const env = Math.min(1, t * 200) * Math.exp(-t * 3); const s = saw(f * t) + 0.5 * saw(f * 1.005 * t); const cut = 0.04 + 0.25 * Math.exp(-t * 12); lp += cut * (s - lp); add(i0 + k, g * lp * env); }
}
function pad(t0, len, ms, g) {
  const i0 = Math.round(t0 * SR); const lps = ms.map(() => 0);
  for (let k = 0; k < SR * len; k++) {
    const t = k / SR; const env = Math.min(1, t / 0.4) * Math.min(1, (len - t) / 0.4);
    let s = 0; ms.forEach((m, j) => { const f = hz(m + 12); s += saw(f * t + j * 0.3) * 0.5 + saw(f * 1.004 * t) * 0.5; });
    lps[0] += 0.03 * (s - lps[0]);
    add(i0 + k, g * lps[0] * env * 0.33, Math.sin(t * 0.7) * 0.4);
  }
}
function arp(t0, m, g) { const i0 = Math.round(t0 * SR), f = hz(m); let lp = 0; for (let k = 0; k < SR * 0.16; k++) { const t = k / SR; const s = saw(f * t) * 0.6 + Math.sign(Math.sin(2 * Math.PI * f * 2 * t)) * 0.2; lp += 0.18 * (s - lp); add(i0 + k, g * lp * Math.exp(-t * 16), 0.35); } }

// intro: a low drone that swells to the groove
{ const i1 = Math.round(musicStart * SR); for (let k = 0; k < i1; k++) { const t = k / SR; const g = 0.05 + 0.12 * (k / i1); add(k, g * (Math.sin(2 * Math.PI * hz(26) * t) + 0.4 * saw(hz(38) * t) * 0.3)); } }
// the groove, bar by bar
for (let b = 0; ; b++) {
  const t0 = musicStart + b * bar; if (t0 >= musicEnd) break;
  const ch = b % 4;
  pad(t0, bar, chords[ch], 0.16);
  for (let q = 0; q < 8; q++) {
    const t = t0 + q * beat / 2; if (t >= musicEnd) break;
    const s = slowAt(t) ? 0.25 : 1;   // under slow motion only the pad and a quiet pulse
    if (q % 2 === 0) kick(t, 0.55 * s);
    if (q === 2 || q === 6) snare(t, 0.32 * s);
    hat(t, 0.10 * s);
    hat(t + beat / 4, 0.05 * s);
    bassNote(t, beat / 2 * 0.95, roots[ch] + (q === 7 ? 12 : 0), 0.22 * s);
    const tones = chords[ch].concat([chords[ch][0] + 12]);
    arp(t, tones[q % 4] + 12, 0.07 * s);
    arp(t + beat / 4, tones[(q + 2) % 4] + 12, 0.05 * s);
  }
}

// ---------- effects ----------
function impact(t0, g, low = 1) {   // a cinematic boom: sub drop and a burst of noise
  const i0 = Math.round(t0 * SR); let lp = 0;
  for (let k = 0; k < SR * 2.5; k++) { const t = k / SR; const ph = 2 * Math.PI * (30 * t + 60 * (1 - Math.exp(-t * 6)) / 6); lp += 0.02 * (noise() - lp);
    add(i0 + k, g * (0.9 * Math.sin(ph) * Math.exp(-t * 1.8) * low + 3.0 * lp * Math.exp(-t * 2.5))); }
}
function explosion(t0, g, slowK) {
  const i0 = Math.round(t0 * SR); let lp = 0, lp2 = 0;
  const len = 3.5 / Math.max(0.4, slowK);
  for (let k = 0; k < SR * len; k++) { const t = k / SR * slowK; const n = noise(); lp += (0.08 * slowK + 0.02) * (n - lp); lp2 += 0.01 * (lp - lp2);
    const crackle = rnd() < 0.002 * Math.exp(-t) ? noise() * 0.8 : 0;
    add(i0 + k, g * (4 * lp * Math.exp(-t * 1.6) + 6 * lp2 * Math.exp(-t * 0.9) + crackle + 0.7 * Math.sin(2 * Math.PI * 38 * t * 1) * Math.exp(-t * 2.5)), (rnd() - 0.5) * 0.3); }
}
function gunShot(t0, g, slowK, pan) {   // one round of a .50 / 20 mm: a crack and a thump
  const i0 = Math.round(t0 * SR); let lp = 0;
  const len = 0.09 / slowK;
  for (let k = 0; k < SR * len; k++) { const t = k / SR * slowK; const n = noise(); lp += 0.25 * (n - lp);
    add(i0 + k, g * (lp * 1.4 * Math.exp(-t * 60) + 0.8 * Math.sin(2 * Math.PI * 90 * t) * Math.exp(-t * 45)), pan); }
}
function clank(t0, g, slowK) { const i0 = Math.round(t0 * SR); for (let k = 0; k < SR * 0.18 / slowK; k++) { const t = k / SR * slowK; add(i0 + k, g * (Math.sin(2 * Math.PI * 1300 * t) * 0.4 + Math.sin(2 * Math.PI * 2170 * t) * 0.3 + noise() * 0.5) * Math.exp(-t * 35), 0.2); } }
function whoosh(t0, len, g) { const i0 = Math.round(t0 * SR); let lp = 0; for (let k = 0; k < SR * len; k++) { const u = k / (SR * len); const env = Math.sin(Math.PI * u) ** 2; lp += (0.02 + 0.2 * env) * (noise() - lp); add(i0 + k, g * lp * env * 2.5, (u - 0.5) * 1.6); } }

// title words: each lands on an impact; the end card on the biggest
titleFrames.forEach((f, i) => impact(f / FPS, 0.55 + 0.05 * i));
const endCard = rows.findIndex(r => r.title === 'endcard');
if (endCard >= 0) { impact(endCard / FPS, 0.95); explosion(endCard / FPS, 0.25, 0.6); }
// a riser into each slow motion, and the kill itself
rows.forEach((r, f) => {
  if (r.kills > 0) { explosion(f / FPS, 0.6, Math.max(0.35, r.speed)); impact(f / FPS, 0.5); }
  if (f > 0 && slow[f] && !slow[f - 1]) whoosh(f / FPS - 0.5, 0.8, 0.35);
});
// guns: the rounds fired near the camera this frame, spread across it
let gunGate = 0;
rows.forEach((r, f) => {
  if (r.cam === 'end') return;
  const k = Math.max(0.2, r.speed);
  if (r.guns > 0) gunGate = 0.25;   // a burst rattles on a little between tracer packets
  const rounds = r.guns > 0 ? Math.min(5, 2 + r.guns) : gunGate > 0 ? 1 : 0;
  gunGate -= 1 / FPS;
  for (let j = 0; j < rounds; j++) gunShot((f + j / rounds + rnd() * 0.1) / FPS, (r.cam === 'Cockpit' ? 0.5 : 0.32) * (0.8 + 0.2 * rnd()), k, r.cam === 'Cockpit' ? 0 : -0.2);
  for (let j = 0; j < r.hits; j++) clank((f + rnd()) / FPS, 0.18, k);
});
// flybys roar past; cuts get a short swish
cuts.forEach(f => { const r = rows[f]; if (r.cam === 'Flyby') whoosh(f / FPS + 0.3, 1.1, 0.45); else if (r.cam !== 'end') whoosh(f / FPS - 0.08, 0.22, 0.12); });
// the engine: a prop's drone under everything but the end card, louder in the cockpit, deeper in slow motion
{ let ph = 0, lp = 0; for (let i = 0; i < N; i++) { const f = Math.floor(i / SR * FPS); if (f >= nFrames || rows[f].cam === 'end') continue;
  const r = rows[f]; const k = Math.max(0.3, r.speed); const g = r.cam === 'Cockpit' ? 0.10 : r.cam === 'Chase' ? 0.06 : 0.035;
  ph += (38 * k) / SR; const s = Math.sign(Math.sin(2 * Math.PI * ph)) * 0.5 + Math.sin(2 * Math.PI * ph * 2) * 0.5 + noise() * 0.3; lp += 0.05 * (s - lp); add(i, g * lp); } }

// ---------- a little room, then out ----------
function reverb(buf) { const d = [1559, 2251, 3203, 4111].map(n => ({ n, b: new Float32Array(n), i: 0 })); const out = new Float32Array(buf.length);
  for (let i = 0; i < buf.length; i++) { let s = 0; for (const c of d) { const y = c.b[c.i]; c.b[c.i] = buf[i] + y * 0.72; c.i = (c.i + 1) % c.n; s += y; } out[i] = buf[i] + s * 0.06; } return out; }
const L2 = reverb(L), R2 = reverb(R);
let peak = 0; for (let i = 0; i < N; i++) peak = Math.max(peak, Math.abs(L2[i]), Math.abs(R2[i]));
const gain = 0.89 / peak;
const soft = x => Math.tanh(x * 1.3) / Math.tanh(1.3);
const buf = Buffer.alloc(44 + N * 4);
buf.write('RIFF', 0); buf.writeUInt32LE(36 + N * 4, 4); buf.write('WAVE', 8); buf.write('fmt ', 12); buf.writeUInt32LE(16, 16);
buf.writeUInt16LE(1, 20); buf.writeUInt16LE(2, 22); buf.writeUInt32LE(SR, 24); buf.writeUInt32LE(SR * 4, 28); buf.writeUInt16LE(4, 32); buf.writeUInt16LE(16, 34);
buf.write('data', 36); buf.writeUInt32LE(N * 4, 40);
for (let i = 0; i < N; i++) { buf.writeInt16LE(Math.round(soft(L2[i] * gain) * 32000), 44 + i * 4); buf.writeInt16LE(Math.round(soft(R2[i] * gain) * 32000), 46 + i * 4); }
fs.writeFileSync(outPath, buf);
console.log(`wrote ${outPath}: ${dur.toFixed(1)} s, ${nFrames} frames, ${titleFrames.length} titles, ${rows.filter(r => r.kills).length} kills`);
