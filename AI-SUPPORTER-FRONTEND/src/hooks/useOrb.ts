import { useEffect, type RefObject } from 'react';

interface Point {
  x: number;
  y: number;
  z: number;
  tw: number;
  cool: boolean;
}

interface Particle {
  x: number;
  y: number;
  vx: number;
  vy: number;
  r: number;
  a: number;
}

interface Scene {
  ctx: CanvasRenderingContext2D | null;
  W: number;
  H: number;
  amp: number;
  mx: number;
  my: number;
  pts: Point[];
  edges: [number, number][];
  parts: Particle[];
  raf: number;
}

export function useOrb(
  canvasRef: RefObject<HTMLCanvasElement | null>,
  stateRef: RefObject<{ screen: string; listening: boolean; speaking: boolean }>
) {
  useEffect(() => {
    const cv = canvasRef.current;
    if (!cv) return;
    const canvas = cv;

    const S: Scene = {
      ctx: null,
      W: 0,
      H: 0,
      amp: 0.16,
      mx: 0,
      my: 0,
      pts: [],
      edges: [],
      parts: [],
      raf: 0,
    };

    const n = 84;
    for (let i = 0; i < n; i++) {
      const y = 1 - (i / (n - 1)) * 2;
      const r = Math.sqrt(Math.max(0, 1 - y * y));
      const th = i * 2.399963;
      S.pts.push({ x: Math.cos(th) * r, y, z: Math.sin(th) * r, tw: Math.random() * 6.28, cool: Math.random() < 0.16 });
    }
    for (let i = 0; i < n; i++) {
      for (let j = i + 1; j < n; j++) {
        const a = S.pts[i];
        const b = S.pts[j];
        if (Math.hypot(a.x - b.x, a.y - b.y, a.z - b.z) < 0.46) S.edges.push([i, j]);
      }
    }
    for (let i = 0; i < 70; i++) {
      S.parts.push({
        x: Math.random(), y: Math.random(),
        vx: (Math.random() - 0.5) * 0.00006, vy: (Math.random() - 0.5) * 0.00006,
        r: Math.random() * 1.4 + 0.3, a: Math.random() * 0.4 + 0.1,
      });
    }

    const onMove = (e: MouseEvent) => {
      S.mx = (e.clientX / window.innerWidth - 0.5) * 2;
      S.my = (e.clientY / window.innerHeight - 0.5) * 2;
    };
    window.addEventListener('mousemove', onMove);

    function drawAmbient(t: number) {
      const c = S.ctx!;
      const w = S.W;
      const h = S.H;
      c.save();
      c.strokeStyle = 'rgba(255,255,255,0.018)';
      c.lineWidth = 1;
      for (let x = 0; x < w; x += 64) { c.beginPath(); c.moveTo(x, 0); c.lineTo(x, h); c.stroke(); }
      for (let y = 0; y < h; y += 64) { c.beginPath(); c.moveTo(0, y); c.lineTo(w, y); c.stroke(); }
      S.parts.forEach((p) => {
        p.x += p.vx * 16;
        p.y += p.vy * 16;
        if (p.x < 0) p.x = 1;
        if (p.x > 1) p.x = 0;
        if (p.y < 0) p.y = 1;
        if (p.y > 1) p.y = 0;
        c.beginPath();
        c.fillStyle = 'rgba(159,176,196,' + p.a * 0.5 + ')';
        c.arc(p.x * w, p.y * h, p.r, 0, 6.28);
        c.fill();
      });
      c.restore();
    }

    function drawOrb(cx: number, cy: number, R: number, t: number) {
      const c = S.ctx!;
      const amp = S.amp;
      const A = t * 0.00016 * (0.4 + amp * 1.4);
      const tilt = 0.32 + S.my * 0.14;
      const yaw = S.mx * 0.25;
      const cosA = Math.cos(A + yaw);
      const sinA = Math.sin(A + yaw);
      const cosT = Math.cos(tilt);
      const sinT = Math.sin(tilt);
      const rr = R * (1 + Math.sin(t * 0.002) * 0.015 + amp * 0.05);
      const proj = S.pts.map((p) => {
        const x1 = p.x * cosA - p.z * sinA;
        const z1 = p.x * sinA + p.z * cosA;
        const y1 = p.y * cosT - z1 * sinT;
        const z2 = p.y * sinT + z1 * cosT;
        const persp = 1 / (1.7 - z2 * 0.55);
        const j = amp * Math.sin(t * 0.004 + p.tw) * 0.012;
        return { sx: cx + (x1 + j) * rr * persp, sy: cy + (y1 + j) * rr * persp, z: z2, cool: p.cool };
      });

      const g = c.createRadialGradient(cx, cy, 0, cx, cy, rr * 1.7);
      g.addColorStop(0, 'rgba(255,107,91,' + (0.34 + amp * 0.3) + ')');
      g.addColorStop(0.35, 'rgba(255,107,91,' + (0.12 + amp * 0.12) + ')');
      g.addColorStop(1, 'rgba(255,107,91,0)');
      c.fillStyle = g;
      c.beginPath();
      c.arc(cx, cy, rr * 1.7, 0, 6.28);
      c.fill();

      c.save();
      for (let k = 0; k < 3; k++) {
        const ringR = rr * (0.55 + k * 0.26) * (1 + amp * 0.12 * Math.sin(t * 0.004 + k * 1.3));
        c.beginPath();
        c.strokeStyle = 'rgba(255,123,103,' + (0.26 - k * 0.05 + amp * 0.12) + ')';
        c.lineWidth = 1.2;
        c.arc(cx, cy, ringR, 0, 6.28);
        c.stroke();
      }
      c.restore();

      c.lineWidth = 1;
      S.edges.forEach(([i, j]) => {
        const a = proj[i];
        const b = proj[j];
        const fb = (a.z + b.z) / 2;
        const al = ((fb + 1) / 2) * (0.3 + amp * 0.4);
        c.strokeStyle = 'rgba(255,123,103,' + al + ')';
        c.beginPath();
        c.moveTo(a.sx, a.sy);
        c.lineTo(b.sx, b.sy);
        c.stroke();
      });

      proj.sort((a, b) => a.z - b.z).forEach((p) => {
        const fr = (p.z + 1) / 2;
        const rad = (1.4 + fr * 2.6) * (0.9 + amp * 0.5);
        const al = 0.4 + fr * 0.6;
        c.beginPath();
        if (p.cool) { c.fillStyle = 'rgba(63,214,255,' + al + ')'; c.shadowColor = 'rgba(63,214,255,0.8)'; }
        else { c.fillStyle = 'rgba(255,140,120,' + al + ')'; c.shadowColor = 'rgba(255,107,91,0.9)'; }
        c.shadowBlur = fr > 0.6 ? 10 : 0;
        c.arc(p.sx, p.sy, rad, 0, 6.28);
        c.fill();
      });
      c.shadowBlur = 0;

      c.beginPath();
      c.fillStyle = 'rgba(255,225,215,' + (0.7 + amp * 0.3) + ')';
      c.shadowColor = '#ff6b5b';
      c.shadowBlur = 18;
      c.arc(cx, cy, 3 + amp * 4, 0, 6.28);
      c.fill();
      c.shadowBlur = 0;
    }

    function drawWave(cx: number, cy: number, halfW: number, t: number) {
      const c = S.ctx!;
      const amp = S.amp;
      const n2 = 80;
      const maxH = S.H * 0.13;
      for (let i = 0; i < n2; i++) {
        const fx = i / (n2 - 1);
        const xx = cx - halfW + fx * halfW * 2;
        const env = Math.sin(fx * Math.PI);
        let v = Math.abs(Math.sin(i * 0.38 + t * 0.006) + 0.5 * Math.sin(i * 0.13 + t * 0.013));
        v = (0.18 + 0.82 * v) * amp * env * (0.7 + Math.random() * 0.3);
        const bh = v * maxH;
        c.strokeStyle = 'rgba(255,107,91,' + (0.3 + env * 0.5) + ')';
        c.lineWidth = 2.4;
        c.lineCap = 'round';
        c.beginPath();
        c.moveTo(xx, cy - bh);
        c.lineTo(xx, cy + bh);
        c.stroke();
      }
    }

    function loop() {
      if (!S.ctx) S.ctx = canvas.getContext('2d')!;
      const c = S.ctx;
      const dpr = Math.min(2, window.devicePixelRatio || 1);
      const w = canvas.clientWidth;
      const h = canvas.clientHeight;
      if (canvas.width !== Math.round(w * dpr)) { canvas.width = Math.round(w * dpr); canvas.height = Math.round(h * dpr); }
      c.setTransform(dpr, 0, 0, dpr, 0, 0);
      S.W = w;
      S.H = h;
      const t = performance.now();
      c.clearRect(0, 0, w, h);
      c.fillStyle = '#05080f';
      c.fillRect(0, 0, w, h);
      drawAmbient(t);
      const st = stateRef.current ?? { screen: 'home', listening: false, speaking: false };
      const target = st.speaking ? 0.95 : st.listening ? 0.6
        : st.screen === 'voice' ? 0.44 : st.screen === 'home' ? 0.34 : 0.12;
      S.amp += (target - S.amp) * 0.07;
      if (st.screen === 'home') drawOrb(w * 0.5, h * 0.42, Math.min(w, h) * 0.155, t);
      else if (st.screen === 'voice') {
        drawOrb(w * 0.5, h * 0.43, Math.min(w, h) * 0.2, t);
        drawWave(w * 0.5, h * 0.62, Math.min(w * 0.42, 560), t);
      }
      S.raf = requestAnimationFrame(loop);
    }

    loop();

    return () => {
      cancelAnimationFrame(S.raf);
      window.removeEventListener('mousemove', onMove);
    };
  }, [canvasRef, stateRef]);
}
