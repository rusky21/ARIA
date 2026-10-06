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

export function GptAvatar({ size = 32, glow }: { size?: number; glow?: boolean }) {
  return (
    <div style={{
      width: size,
      height: size,
      flexShrink: 0,
      borderRadius: '50%',
      position: 'relative',
      background: 'conic-gradient(from 210deg, #ff6b5b, #ff9d5b, #38bdf8, #ff6b5b)',
      display: 'flex',
      alignItems: 'center',
      justifyContent: 'center',
      boxShadow: glow
        ? '0 0 24px rgba(255,107,91,0.6), 0 0 8px rgba(255,107,91,0.4)'
        : '0 0 14px rgba(255,107,91,0.3)',
      transition: 'box-shadow .3s, transform .2s',
    }}>
      <div style={{
        width: size - 8,
        height: size - 8,
        borderRadius: '50%',
        background: '#090d15',
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        boxShadow: 'inset 0 1px 3px rgba(255,255,255,0.15)',
      }}>
        <div style={{
          width: Math.max(size / 3.5, 7),
          height: Math.max(size / 3.5, 7),
          borderRadius: '50%',
          background: 'radial-gradient(circle at 40% 35%, #ffd9a8 0%, #ff6b5b 80%)',
          boxShadow: '0 0 8px rgba(255,107,91,0.7)',
        }} />
      </div>
    </div>
  );
}

export function UserMsg({ text, image }: { text: string; image?: string }) {
  return (
    <div style={{ display: 'flex', justifyContent: 'flex-end', width: '100%' }}>
      <div style={{
        maxWidth: '82%',
        background: 'linear-gradient(135deg, rgba(255,107,91,0.18) 0%, rgba(255,84,112,0.09) 100%)',
        border: '1px solid rgba(255,107,91,0.28)',
        borderRadius: '20px 20px 4px 20px',
        padding: '12px 18px',
        fontSize: '14.5px',
        lineHeight: 1.6,
        color: '#f8fafc',
        boxShadow: '0 6px 24px rgba(0,0,0,0.25), 0 0 16px rgba(255,107,91,0.06)',
        whiteSpace: 'pre-wrap',
        wordBreak: 'break-word',
        fontFamily: "'Plus Jakarta Sans', sans-serif",
      }}>
        {image && (
          <img
            src={image}
            alt=""
            style={{
              display: 'block',
              maxWidth: 260,
              borderRadius: 12,
              marginBottom: text ? 10 : 0,
              border: '1px solid rgba(255,255,255,0.15)',
            }}
          />
        )}
        {text}
      </div>
    </div>
  );
}

export function AssistantMsg({ text, onLink, children }: { text?: string; onLink?: (url: string) => void; children?: React.ReactNode }) {
  return (
    <div style={{ display: 'flex', gap: 14, alignItems: 'flex-start', width: '100%' }}>
      <GptAvatar glow />
      <div style={{
        flex: 1,
        minWidth: 0,
        background: 'rgba(255,255,255,0.02)',
        border: '1px solid rgba(255,255,255,0.05)',
        borderRadius: 18,
        padding: '14px 18px',
        boxShadow: '0 4px 20px rgba(0,0,0,0.2)',
      }}>
        {children}
        {text != null && (
          <div style={{
            color: '#e2e8f0',
            fontSize: '14.5px',
            lineHeight: 1.65,
            fontFamily: "'Plus Jakarta Sans', sans-serif",
          }}>
            {renderMarkdown(text, onLink)}
          </div>
        )}
      </div>
    </div>
  );
}

export function ToolMsg({ name, result, ms }: { name: string; result: string; ms: number }) {
  const [open, setOpen] = useState(false);
  return (
    <div style={{ display: 'flex', gap: 14, alignItems: 'flex-start', paddingLeft: 46 }}>
      <button
        onClick={() => setOpen((v) => !v)}
        style={{
          display: 'flex',
          alignItems: 'center',
          gap: 9,
          padding: '7px 14px',
          borderRadius: 10,
          border: `1px solid ${cyanA(0.25)}`,
          background: 'rgba(56,189,248,0.06)',
          cursor: 'pointer',
          color: C.cyan,
          fontFamily: C.mono,
          fontSize: FS.s,
          transition: 'all .18s',
          boxShadow: '0 2px 10px rgba(0,0,0,0.2)',
        }}
        onMouseEnter={(e) => {
          e.currentTarget.style.background = 'rgba(56,189,248,0.12)';
          e.currentTarget.style.borderColor = 'rgba(56,189,248,0.4)';
        }}
        onMouseLeave={(e) => {
          e.currentTarget.style.background = 'rgba(56,189,248,0.06)';
          e.currentTarget.style.borderColor = cyanA(0.25);
        }}
      >
        <Icon name="tool" size={13} width={1.8} />
        <span style={{ fontWeight: 600 }}>{name}</span>
        <span style={{ color: C.green, fontFamily: C.mono, fontSize: 11 }}>✓ {ms}ms</span>
        <span style={{
          color: C.textDim,
          transform: open ? 'rotate(90deg)' : 'none',
          transition: 'transform .18s',
          fontSize: 10,
        }}>▶</span>
      </button>

      {open && (
        <div style={{
          flex: 1,
          minWidth: 0,
          background: 'rgba(6,9,15,0.85)',
          border: '1px solid rgba(255,255,255,0.08)',
          borderRadius: 12,
          padding: '12px 16px',
          fontFamily: C.mono,
          fontSize: FS.s,
          lineHeight: 1.6,
          color: '#cbd5e1',
          whiteSpace: 'pre-wrap',
          boxShadow: '0 8px 24px rgba(0,0,0,0.3)',
        }}>
          {result}
        </div>
      )}
    </div>
  );
}

export function ThinkingRow({ label }: { label?: string }) {
  return (
    <div style={{ display: 'flex', gap: 14, alignItems: 'center' }}>
      <GptAvatar glow />
      <div style={{
        display: 'inline-flex',
        alignItems: 'center',
        gap: 8,
        padding: '6px 14px',
        borderRadius: 999,
        background: 'rgba(255,107,91,0.08)',
        border: '1px solid rgba(255,107,91,0.2)',
      }}>
        {label ? (
          <span style={{ fontFamily: C.mono, fontSize: 11.5, color: '#f87171', letterSpacing: '.06em' }}>{label}</span>
        ) : (
          <>
            <span style={{ fontFamily: C.mono, fontSize: 11, color: '#f87171', letterSpacing: '.08em', fontWeight: 600 }}>
              ARIA АНАЛИЗИРУЕТ
            </span>
            <span style={{ display: 'inline-flex', gap: 4 }}>
              {[0, 0.18, 0.36].map((d) => (
                <span
                  key={d}
                  style={{
                    width: 5,
                    height: 5,
                    borderRadius: '50%',
                    background: '#ff6b5b',
                    animation: `ariaDot 1.2s infinite ${d}s`,
                    boxShadow: '0 0 6px #ff6b5b',
                  }}
                />
              ))}
            </span>
          </>
        )}
      </div>
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
    <div style={{ padding: '12px 24px 24px', position: 'relative', zIndex: 10 }}>
      <div style={{ maxWidth: 'min(860px, 94vw)', margin: '0 auto', position: 'relative' }}>
        {image && (
          <div style={{
            display: 'inline-flex',
            alignItems: 'center',
            gap: 12,
            marginBottom: 10,
            padding: '6px 12px 6px 6px',
            borderRadius: 14,
            background: 'rgba(15,22,34,0.95)',
            border: '1px solid rgba(255,255,255,0.12)',
            boxShadow: '0 8px 24px rgba(0,0,0,0.4)',
          }}>
            <img src={image} alt="" style={{ width: 56, height: 56, objectFit: 'cover', borderRadius: 10, display: 'block' }} />
            <button
              onClick={() => onImageChange?.(null)}
              title="Удалить изображение"
              style={{
                width: 28,
                height: 28,
                borderRadius: '50%',
                background: 'rgba(255,255,255,0.06)',
                border: '1px solid rgba(255,255,255,0.1)',
                color: C.textMid,
                cursor: 'pointer',
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'center',
              }}
            >
              <Icon name="close" size={13} />
            </button>
          </div>
        )}

        {/* Floating Obsidian Capsule Composer */}
        <div style={{
          display: 'flex',
          alignItems: 'flex-end',
          gap: 10,
          background: 'rgba(10, 15, 24, 0.88)',
          border: '1px solid rgba(255,255,255,0.1)',
          borderRadius: 24,
          padding: '8px 10px 8px 16px',
          boxShadow: '0 12px 42px rgba(0,0,0,0.55), inset 0 1px 0 rgba(255,255,255,0.1)',
          backdropFilter: 'blur(20px)',
          WebkitBackdropFilter: 'blur(20px)',
          transition: 'border-color .2s, box-shadow .2s',
        }}>
          <input ref={fileRef} type="file" accept="image/*" onChange={onFilePicked} style={{ display: 'none' }} />
          <button
            onClick={() => fileRef.current?.click()}
            title="Прикрепить изображение"
            style={{
              width: 36,
              height: 36,
              borderRadius: '50%',
              background: 'rgba(255,255,255,0.04)',
              border: '1px solid rgba(255,255,255,0.08)',
              color: '#94a3b8',
              cursor: 'pointer',
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'center',
              flexShrink: 0,
              marginBottom: 2,
              transition: 'all .18s',
            }}
            onMouseEnter={(e) => {
              e.currentTarget.style.background = 'rgba(255,255,255,0.09)';
              e.currentTarget.style.color = '#f1f5f9';
            }}
            onMouseLeave={(e) => {
              e.currentTarget.style.background = 'rgba(255,255,255,0.04)';
              e.currentTarget.style.color = '#94a3b8';
            }}
          >
            <span style={{ fontSize: 18, lineHeight: 1 }}>+</span>
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
              flex: 1,
              background: 'transparent',
              border: 'none',
              outline: 'none',
              resize: 'none',
              color: '#f8fafc',
              fontFamily: "'Plus Jakarta Sans', sans-serif",
              fontSize: '14.5px',
              lineHeight: 1.5,
              maxHeight: 200,
              paddingTop: 8,
              paddingBottom: 6,
            }}
          />

          {onMic && (
            <button
              onClick={onMic}
              title="Голосовой ввод"
              style={{
                width: 36,
                height: 36,
                borderRadius: '50%',
                background: 'rgba(255,255,255,0.04)',
                border: '1px solid rgba(255,255,255,0.08)',
                cursor: 'pointer',
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'center',
                flexShrink: 0,
                marginBottom: 2,
                transition: 'all .18s',
              }}
              onMouseEnter={(e) => {
                e.currentTarget.style.background = 'rgba(255,107,91,0.15)';
                e.currentTarget.style.borderColor = 'rgba(255,107,91,0.3)';
              }}
              onMouseLeave={(e) => {
                e.currentTarget.style.background = 'rgba(255,255,255,0.04)';
                e.currentTarget.style.borderColor = 'rgba(255,255,255,0.08)';
              }}
            >
              <MicIcon color="#94a3b8" size={18} />
            </button>
          )}

          <button
            onClick={trySend}
            disabled={!canSend}
            title="Отправить (Enter)"
            style={{
              width: 38,
              height: 38,
              borderRadius: '50%',
              border: 'none',
              flexShrink: 0,
              background: canSend
                ? 'linear-gradient(135deg, #ff6b5b 0%, #ff8b7b 100%)'
                : 'rgba(255,255,255,0.08)',
              color: canSend ? '#fff' : '#64748b',
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'center',
              cursor: canSend ? 'pointer' : 'default',
              boxShadow: canSend ? '0 4px 16px rgba(255,107,91,0.45)' : 'none',
              transition: 'all .18s',
              transform: canSend ? 'scale(1)' : 'scale(0.96)',
            }}
            onMouseEnter={(e) => {
              if (canSend) e.currentTarget.style.transform = 'scale(1.08)';
            }}
            onMouseLeave={(e) => {
              if (canSend) e.currentTarget.style.transform = 'scale(1)';
            }}
          >
            <Icon name="send" color={canSend ? '#ffffff' : '#64748b'} size={17} width={2.2} />
          </button>
        </div>

        {busyLabel && (
          <div style={{
            textAlign: 'center',
            marginTop: 8,
            fontFamily: C.mono,
            fontSize: FS.s,
            color: 'rgba(126,139,160,0.8)',
          }}>
            {busyLabel}
          </div>
        )}
      </div>
    </div>
  );
}
