import { useEffect, useRef, useState, type RefObject } from 'react';
import { C, R, FS, w, cyanA } from '../lib/theme.js';
import { renderMarkdown } from '../lib/markdown.js';
import { Icon, MicIcon } from './Icon.jsx';

export function useStickToBottom(deps: unknown[]): { ref: RefObject<HTMLDivElement> } {
  const ref = useRef<HTMLDivElement>(null);
  const stick = useRef(true);
  useEffect(() => {
    const el = ref.current;
    if (!el) return;
    const onScroll = () => {
      stick.current = el.scrollTop + el.clientHeight >= el.scrollHeight - 120;
    };
    el.addEventListener('scroll', onScroll);
    return () => el.removeEventListener('scroll', onScroll);
  }, []);
  useEffect(() => {
    const el = ref.current;
    if (el && stick.current) el.scrollTop = el.scrollHeight;
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, deps);
  return { ref };
}

export function GptAvatar({ size = 30, glow }: { size?: number; glow?: boolean }) {
  return (
    <div style={{
      width: size, height: size, flexShrink: 0, borderRadius: '50%', position: 'relative',
      background: 'conic-gradient(from 210deg, #ff6b5b, #ff9d5b, #ff6b5b)',
      display: 'flex', alignItems: 'center', justifyContent: 'center',
      boxShadow: glow ? '0 0 22px rgba(255,107,91,.55), 0 0 6px rgba(255,107,91,.4)' : '0 0 14px rgba(255,107,91,.3)',
      transition: 'box-shadow .3s',
    }}>
      <div style={{ width: size - 10, height: size - 10, borderRadius: '50%', background: C.panelAlt, display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
        <div style={{ width: size / 4, height: size / 4, borderRadius: '50%', background: 'radial-gradient(circle at 40% 35%, #ffd9a8, #ff6b5b)' }} />
      </div>
    </div>
  );
}

export function UserMsg({ text, image }: { text: string; image?: string }) {
  return (
    <div style={{ display: 'flex', justifyContent: 'flex-end' }}>
      <div style={userBubble}>
        {image && <img src={image} alt="" style={{ display: 'block', maxWidth: 240, borderRadius: 10, marginBottom: text ? 8 : 0 }} />}
        {text}
      </div>
    </div>
  );
}

export function AssistantMsg({ text, onLink, children }: { text?: string; onLink?: (url: string) => void; children?: React.ReactNode }) {
  return (
    <div style={{ display: 'flex', gap: 14, alignItems: 'flex-start' }}>
      <GptAvatar />
      <div style={{ flex: 1, minWidth: 0, paddingTop: 4 }}>
        {children}
        {text != null && (
          <div style={{ color: C.tableText, fontSize: FS.xl, lineHeight: 1.65 }}>{renderMarkdown(text, onLink)}</div>
        )}
      </div>
    </div>
  );
}

export function ToolMsg({ name, result, ms }: { name: string; result: string; ms: number }) {
  const [open, setOpen] = useState(false);
  return (
    <div style={{ display: 'flex', gap: 14, alignItems: 'flex-start' }}>
      <div style={{ width: 30, flexShrink: 0 }} />
      <button onClick={() => setOpen((v) => !v)} style={{
        display: 'flex', alignItems: 'center', gap: 9, padding: '7px 13px', borderRadius: R.s,
        border: `1px solid ${cyanA(0.22)}`, background: cyanA(0.06), cursor: 'pointer',
        color: C.cyan, fontFamily: C.mono, fontSize: FS.s, transition: 'all .18s',
      }}
        onMouseEnter={(e) => { e.currentTarget.style.background = cyanA(0.1); }}
        onMouseLeave={(e) => { e.currentTarget.style.background = cyanA(0.06); }}>
        <Icon name="tool" size={13} width={1.8} />
        <span>{name}</span>
        <span style={{ color: C.green }}>✓ {ms}ms</span>
        <span style={{ color: C.textDim, transform: open ? 'rotate(90deg)' : 'none', transition: 'transform .15s' }}>▶</span>
      </button>
      {open && (
        <div style={{ flex: 1, minWidth: 0, background: 'rgba(8,14,22,.7)', border: `1px solid ${C.line}`, borderRadius: R.m, padding: '10px 14px', fontFamily: C.mono, fontSize: FS.s, lineHeight: 1.6, color: C.tableDim, whiteSpace: 'pre-wrap' }}>
          {result}
        </div>
      )}
    </div>
  );
}

export function ThinkingRow({ label }: { label?: string }) {
  return (
    <div style={{ display: 'flex', gap: 14, alignItems: 'center' }}>
      <GptAvatar />
      {label ? (
        <span style={{ fontFamily: C.mono, fontSize: FS.s, color: C.textDim }}>{label}</span>
      ) : (
        <span style={{ display: 'inline-flex', gap: 5 }}>
          {[0, 0.18, 0.36].map((d) => (
            <span key={d} style={{ width: 8, height: 8, borderRadius: '50%', background: C.tableDim, animation: `ariaDot 1.2s infinite ${d}s` }} />
          ))}
        </span>
      )}
    </div>
  );
}

export function GptComposer({
  value, onChange, onSend, placeholder, onMic, disabled, busyLabel, image, onImageChange,
}: {
  value: string; onChange: (v: string) => void; onSend: () => void;
  placeholder: string; onMic?: () => void; disabled?: boolean; busyLabel?: string;
  image?: string | null; onImageChange?: (v: string | null) => void;
}) {
  const taRef = useRef<HTMLTextAreaElement>(null);
  const fileRef = useRef<HTMLInputElement>(null);
  useEffect(() => {
    const ta = taRef.current;
    if (!ta) return;
    ta.style.height = 'auto';
    ta.style.height = Math.min(ta.scrollHeight, 200) + 'px';
  }, [value]);

  const canSend = !disabled && (value.trim() || !!image);
  const trySend = () => { if (canSend) onSend(); };

  const onFilePicked = (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    if (!file || !onImageChange) return;
    const reader = new FileReader();
    reader.onload = () => onImageChange(String(reader.result));
    reader.readAsDataURL(file);
    e.target.value = '';
  };

  return (
    <div style={{ padding: '10px 24px 22px' }}>
      <div style={{ maxWidth: 'min(820px, 94vw)', margin: '0 auto', position: 'relative' }}>
        {image && (
          <div style={{ display: 'inline-flex', alignItems: 'center', gap: 10, marginBottom: 8, padding: 6, borderRadius: 14, background: 'rgba(20,27,41,.92)', border: `1px solid ${w(0.09)}` }}>
            <img src={image} alt="" style={{ width: 64, height: 64, objectFit: 'cover', borderRadius: 10, display: 'block' }} />
            <button onClick={() => onImageChange?.(null)} title="Удалить изображение" style={{ ...circleBtn, background: 'transparent', border: `1px solid ${w(0.12)}`, color: C.textMid }}>
              <Icon name="close" size={14} />
            </button>
          </div>
        )}
        <div style={{
          display: 'flex', alignItems: 'flex-end', gap: 8,
          background: 'rgba(20,27,41,.92)', border: `1px solid ${w(0.09)}`,
          borderRadius: R.xxl, padding: '9px 10px 9px 16px',
          boxShadow: '0 8px 32px rgba(0,0,0,.45)',
          backdropFilter: 'blur(10px)', transition: 'border-color .2s',
        }}>
          <input ref={fileRef} type="file" accept="image/*" onChange={onFilePicked} style={{ display: 'none' }} />
          <button onClick={() => fileRef.current?.click()} title="Прикрепить изображение" style={{ ...circleBtn, background: 'transparent', border: `1px solid ${w(0.12)}`, color: '#7e8ba0', marginBottom: 2 }}>
            <span style={{ fontSize: 17, lineHeight: 1 }}>+</span>
          </button>
          <textarea
            ref={taRef}
            rows={1}
            value={value}
            onChange={(e) => onChange(e.target.value)}
            onKeyDown={(e) => { if (e.key === 'Enter' && !e.shiftKey) { e.preventDefault(); trySend(); } }}
            placeholder={placeholder}
            autoFocus
            style={{
              flex: 1, background: 'transparent', border: 'none', outline: 'none', resize: 'none',
              color: C.textHi, fontFamily: C.ui, fontSize: FS.xl, lineHeight: 1.5,
              maxHeight: 200, paddingTop: 8, paddingBottom: 6,
            }}
          />
          {onMic && (
            <button onClick={onMic} title="Голосовой ввод" style={{ ...circleBtn, background: 'transparent', marginBottom: 2 }}>
              <MicIcon color="#7e8ba0" size={19} />
            </button>
          )}
          <button onClick={trySend} disabled={!canSend} title="Отправить"
            style={{ ...circleBtn, opacity: canSend ? 1 : 0.45, cursor: canSend ? 'pointer' : 'default' }}>
            <Icon name="send" color={C.onAccent} size={18} width={2} />
          </button>
        </div>
        {busyLabel && (
          <div style={{ textAlign: 'center', marginTop: 8, fontFamily: C.mono, fontSize: FS.s, color: C.textDim }}>{busyLabel}</div>
        )}
      </div>
    </div>
  );
}

const circleBtn: React.CSSProperties = {
  width: 38, height: 38, borderRadius: '50%', border: 'none', flexShrink: 0,
  background: C.accent, color: C.onAccent, display: 'flex', alignItems: 'center', justifyContent: 'center',
};

const userBubble: React.CSSProperties = {
  maxWidth: '78%', background: w(0.085), borderRadius: R.xxl,
  padding: '11px 17px', fontSize: FS.xl, lineHeight: 1.55, color: C.tableText,
  whiteSpace: 'pre-wrap', wordBreak: 'break-word',
};