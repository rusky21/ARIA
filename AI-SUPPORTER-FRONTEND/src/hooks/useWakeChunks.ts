import { useRef, useEffect } from 'react';

const SAMPLE_RATE = 16000;
import { API_URL } from '../lib/config.js';
const WINDOW_SEC = 2.5;
const WINDOW_SAMPLES = SAMPLE_RATE * WINDOW_SEC; // 40000
const SEND_INTERVAL = 500;
const VAD_RMS_MIN = 0.008;

function rms(samples: Float32Array): number {
  let sum = 0;
  for (let i = 0; i < samples.length; i++) sum += samples[i] * samples[i];
  return Math.sqrt(sum / samples.length);
}

function encodeWav(samples: Float32Array, sampleRate: number): ArrayBuffer {
  const numSamples = samples.length;
  const buffer = new ArrayBuffer(44 + numSamples * 2);
  const view = new DataView(buffer);

  const writeStr = (off: number, str: string) => {
    for (let i = 0; i < str.length; i++) view.setUint8(off + i, str.charCodeAt(i));
  };

  writeStr(0, 'RIFF');
  view.setUint32(4, 36 + numSamples * 2, true);
  writeStr(8, 'WAVE');
  writeStr(12, 'fmt ');
  view.setUint32(16, 16, true);
  view.setUint16(20, 1, true);
  view.setUint16(22, 1, true);
  view.setUint32(24, sampleRate, true);
  view.setUint32(28, sampleRate * 2, true);
  view.setUint16(32, 2, true);
  view.setUint16(34, 16, true);
  writeStr(36, 'data');
  view.setUint32(40, numSamples * 2, true);

  for (let i = 0; i < numSamples; i++) {
    const s = Math.max(-1, Math.min(1, samples[i]));
    view.setInt16(44 + i * 2, s < 0 ? s * 0x8000 : s * 0x7FFF, true);
  }

  return buffer;
}

export function useWakeChunks(ws: WebSocket | null, enabled: boolean, onWake: () => void, onScore: (score: number) => void) {
  const streamRef = useRef<MediaStream | null>(null);
  const ctxRef = useRef<AudioContext | null>(null);
  const timerRef = useRef<ReturnType<typeof setInterval>>();
  const enabledRef = useRef(enabled);
  const onWakeRef = useRef(onWake);
  const onScoreRef = useRef(onScore);
  const inFlightRef = useRef(false);
  const wsRef = useRef(ws);
  const bufferRef = useRef(new Float32Array(WINDOW_SAMPLES));
  const posRef = useRef(0);
  const wakeSentRef = useRef(false);

  enabledRef.current = enabled;
  onWakeRef.current = onWake;
  onScoreRef.current = onScore;
  wsRef.current = ws;

  useEffect(() => {
    if (!ws) return;
    const handler = (e: MessageEvent) => {
      try {
        const parsed = JSON.parse(e.data);
        if (parsed.type !== 'wake:check:result') return;
        inFlightRef.current = false;
        onScoreRef.current(typeof parsed.score === 'number' ? parsed.score : 0);
        if (parsed.isHotword && !wakeSentRef.current) {
          wakeSentRef.current = true;
          if (timerRef.current) clearInterval(timerRef.current);
          try { streamRef.current?.getTracks().forEach(t => t.stop()); } catch {}
          try { ctxRef.current?.close(); } catch {}
          onWakeRef.current();
        }
      } catch {}
    };
    ws.addEventListener('message', handler);
    return () => ws.removeEventListener('message', handler);
  }, [enabled]);

  useEffect(() => {
    if (!enabled) return;
    wakeSentRef.current = false;

    let cancelled = false;

    const start = async () => {
      try {
        const stream = await navigator.mediaDevices.getUserMedia({ audio: true });
        if (cancelled) { stream.getTracks().forEach(t => t.stop()); return; }
        streamRef.current = stream;

        const ctx = new AudioContext({ sampleRate: SAMPLE_RATE });
        if (cancelled) { ctx.close(); return; }
        ctxRef.current = ctx;

        const src = ctx.createMediaStreamSource(stream);
        const node = ctx.createScriptProcessor(4096, 1, 1);

        node.onaudioprocess = (e) => {
          const ch = e.inputBuffer.getChannelData(0);
          const buf = bufferRef.current;
          let pos = posRef.current;
          for (let i = 0; i < ch.length; i++) {
            buf[pos] = ch[i];
            pos = (pos + 1) % WINDOW_SAMPLES;
          }
          posRef.current = pos;
        };

        src.connect(node);

        timerRef.current = setInterval(() => {
          if (!enabledRef.current) return;
          if (inFlightRef.current) return;

          const buf = bufferRef.current;
          const pos = posRef.current;
          const slice = new Float32Array(WINDOW_SAMPLES);
          for (let i = 0; i < WINDOW_SAMPLES; i++) {
            slice[i] = buf[(pos + i) % WINDOW_SAMPLES];
          }

          const wav = encodeWav(slice, SAMPLE_RATE);

          if (rms(slice) < VAD_RMS_MIN) { onScoreRef.current(0); return; } // тишина — не шлём в детектор

          const socket = wsRef.current;
          if (socket?.readyState === WebSocket.OPEN) {
            inFlightRef.current = true;
            const bin = new Uint8Array(wav);
            let binary = '';
            for (let i = 0; i < bin.length; i++) binary += String.fromCharCode(bin[i]);
            socket.send(JSON.stringify({ type: 'wake:check', audio: btoa(binary) }));
          }
        }, SEND_INTERVAL);
      } catch {}
    };

    start();

    return () => {
      cancelled = true;
      if (timerRef.current) clearInterval(timerRef.current);
      try { streamRef.current?.getTracks().forEach(t => t.stop()); } catch {}
      try { ctxRef.current?.close(); } catch {}
    };
  }, [enabled]);
}
