import { useEffect, useRef, useState } from 'react';
import { C, acc, w, R } from '../lib/theme.js';
import { Icon } from './Icon.jsx';
import type { FloatingWindow as FW } from '../hooks/useAssistant.js';

const MIN_W = 360;
const MIN_H = 260;

export default function FloatingWindows({ windows, onClose, onFocus }:
  { windows: FW[]; onClose: (id: string) => void; onFocus: (id: string) => void }) {
  if (windows.length === 0) return null;

  return (
    <div style={{ position: 'fixed', inset: 0, zIndex: 40, pointerEvents: 'none' }}>
      {windows.map(w => (
        <FloatWindow key={w.id} w={w} onClose={onClose} onFocus={onFocus} />
      ))}
    </div>
  );
}

function useCameraClock(active: boolean) {
  const [t, setT] = useState(() => new Date());
  useEffect(() => {
    if (!active) return;
    const iv = setInterval(() => setT(new Date()), 1000);
    return () => clearInterval(iv);
  }, [active]);
  return t;
}

function FloatWindow({ w, onClose, onFocus }: { w: FW; onClose: (id: string) => void; onFocus: (id: string) => void }) {
  const [pos, setPos] = useState({ x: w.x, y: w.y });
  const [size, setSize] = useState({ width: w.width, height: w.height });
  const [minimized, setMinimized] = useState(false);
  const drag = useRef<{ ox: number; oy: number; sx: number; sy: number } | null>(null);
  const resize = useRef<{ sx: number; sy: number; ow: number; oh: number } | null>(null);
  const movedDist = useRef(0);

  const isCamera = w.kind === 'camera';
  const clock = useCameraClock(!minimized && isCamera);

  const startDrag = (e: React.PointerEvent) => {
    onFocus(w.id);
    movedDist.current = 0;
    drag.current = { ox: e.clientX - pos.x, oy: e.clientY - pos.y, sx: pos.x, sy: pos.y };
    (e.target as HTMLElement).setPointerCapture(e.pointerId);
  };

  const onDrag = (e: React.PointerEvent) => {
    if (!drag.current) return;
    const nx = Math.max(0, Math.min(window.innerWidth - 60, e.clientX - drag.current.ox));
    const ny = Math.max(0, Math.min(window.innerHeight - 40, e.clientY - drag.current.oy));
    movedDist.current += Math.abs(nx - pos.x) + Math.abs(ny - pos.y);
    setPos({ x: nx, y: ny });
  };

  const endDrag = () => {
    drag.current = null;
  };

  const startResize = (e: React.PointerEvent) => {
    onFocus(w.id);
    resize.current = { sx: e.clientX, sy: e.clientY, ow: size.width, oh: size.height };
    (e.target as HTMLElement).setPointerCapture(e.pointerId);
  };

  const onResize = (e: React.PointerEvent) => {
    if (!resize.current) return;
    setSize({
      width: Math.max(MIN_W, resize.current.ow + (e.clientX - resize.current.sx)),
      height: Math.max(MIN_H, resize.current.oh + (e.clientY - resize.current.sy)),
    });
  };

  const endResize = () => { resize.current = null; };

  if (minimized) {
    return (
      <div
        onClick={() => {
          if (movedDist.current < 5) { onFocus(w.id); setMinimized(false); }
          movedDist.current = 0;
        }}
        onPointerDown={startDrag}
        onPointerMove={onDrag}
        onPointerUp={endDrag}
        title={w.title}
        style={{
          position: 'fixed', left: pos.x, top: pos.y, zIndex: w.zIndex, pointerEvents: 'auto',
          padding: '6px 14px', cursor: 'pointer',
          background: C.panelAlt, border: `1px solid ${isCamera ? C.accentLine : C.line}`, borderRadius: R.m,
          display: 'flex', alignItems: 'center', gap: 8, userSelect: 'none',
          boxShadow: isCamera ? '0 0 26px rgba(255,107,91,.25)' : '0 8px 30px rgba(0,0,0,.5)',
          touchAction: 'none',
        }}
      >
        <Icon name={isCamera ? 'camera' : 'network'} color={C.accent} size={16} />
        <span style={{ fontFamily: C.mono, fontSize: 11, color: C.textMid, maxWidth: 220, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>{w.title}</span>
        <span style={{ fontSize: 10, color: C.textDim }}>▲</span>
      </div>
    );
  }

  return (
    <div
      onClick={() => onFocus(w.id)}
      style={{
        position: 'fixed', left: pos.x, top: pos.y, zIndex: w.zIndex, pointerEvents: 'auto',
        width: size.width, height: size.height,
        display: 'flex', flexDirection: 'column',
        background: C.panelSolid, border: `1px solid ${w.zIndex > 10 ? C.accentLine : C.line}`,
        borderRadius: R.lg,
        boxShadow: isCamera ? '0 0 40px rgba(255,107,91,.15), 0 18px 60px rgba(0,0,0,.6)' : '0 18px 60px rgba(0,0,0,.6)',
        overflow: 'hidden',
        fontFamily: C.ui,
      }}
    >
      <div
        onPointerDown={startDrag} onPointerMove={onDrag} onPointerUp={endDrag}
        style={{
          display: 'flex', alignItems: 'center', gap: 10, padding: '8px 10px',
          background: acc(0.08), borderBottom: `1px solid ${C.line}`,
          cursor: 'move', userSelect: 'none', flexShrink: 0, touchAction: 'none',
        }}
      >
        <Icon name={isCamera ? 'camera' : 'network'} color={C.accent} size={15} />
        <span style={{ flex: 1, fontSize: 12, color: C.textHi, letterSpacing: '.04em', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>{w.title}</span>
        {isCamera && (
          <span style={{
            display: 'flex', alignItems: 'center', gap: 5, padding: '2px 8px', borderRadius: 5,
            fontFamily: C.mono, fontSize: 9, letterSpacing: '.1em',
            background: 'rgba(255,84,112,.16)', color: C.red, border: '1px solid rgba(255,84,112,.25)',
          }}>
            <span style={{ width: 6, height: 6, borderRadius: '50%', background: C.red, boxShadow: '0 0 8px #ff5470', animation: 'ariaDot 1.1s infinite' }} />
            LIVE · {clock.toLocaleTimeString('ru-RU', { hour: '2-digit', minute: '2-digit', second: '2-digit' })}
          </span>
        )}
        <button onClick={() => setMinimized(true)} title="Свернуть" style={tb}>—</button>
        <button onClick={() => onClose(w.id)} title="Закрыть" style={{ ...tb, color: C.accent }}>✕</button>
      </div>

      <div style={{ flex: 1, position: 'relative', background: '#000', minHeight: 0 }}>
        {isCamera ? (
          <>
            <img src={w.url} alt={w.title} style={{ width: '100%', height: '100%', objectFit: 'contain', background: '#000' }} />
            <div style={{ position: 'absolute', left: 0, right: 0, top: 0, height: 44, background: 'linear-gradient(to bottom, rgba(0,0,0,.55), transparent)', pointerEvents: 'none' }} />
            <div style={{ position: 'absolute', left: 0, right: 0, bottom: 0, height: 44, background: 'linear-gradient(to top, rgba(0,0,0,.55), transparent)', pointerEvents: 'none' }} />
          </>
        ) : (
          <iframe
            src={w.url}
            title={w.title}
            style={{ width: '100%', height: '100%', border: 'none', background: '#fff' }}
            referrerPolicy="no-referrer"
          />
        )}
      </div>

      <div
        onPointerDown={startResize} onPointerMove={onResize} onPointerUp={endResize}
        style={{
          position: 'absolute', right: 0, bottom: 0, width: 16, height: 16,
          cursor: 'nwse-resize',
        }}
      />
    </div>
  );
}

const tb: React.CSSProperties = {
  width: 26, height: 26, borderRadius: R.s, border: 'none', cursor: 'pointer',
  background: w(0.06), color: C.textMid, fontSize: 13,
  display: 'inline-flex', alignItems: 'center', justifyContent: 'center',
};
