import { useRef } from 'react';
import { C, acc, w, R } from '../lib/theme.js';
import { Icon, MicIcon } from './Icon.jsx';
import type { AssistantState } from '../hooks/useAssistant.js';

export default function Voice({ a }: { a: AssistantState }) {
  const inputRef = useRef<HTMLInputElement>(null);
  const voiceState = a.speaking ? 'ОТВЕЧАЮ' : a.listening ? 'СЛУШАЮ…' : 'ОБРАБОТКА';
  const showManualInput = !a.listening && !a.speaking;

  const handleManualSend = () => {
    if (!a.input.trim()) return;
    a.sendMsg();
    a.exitVoice();
  };

  return (
    <div style={{
      position: 'absolute',
      inset: 0,
      zIndex: 4,
      background: 'radial-gradient(circle at 50% 50%, rgba(6,8,13,0.3) 0%, rgba(6,8,13,0.75) 100%)',
      backdropFilter: 'blur(4px)',
      WebkitBackdropFilter: 'blur(4px)',
      animation: 'ariaFadeFast .22s ease both',
    }}>
      {/* ─── Top Status Badge & Real-time Transcript ─── */}
      <div style={{
        position: 'absolute',
        top: 36,
        left: 0,
        right: 0,
        display: 'flex',
        flexDirection: 'column',
        alignItems: 'center',
        gap: 18,
      }}>
        {/* Pulsing Voice State Pill */}
        <div style={{
          display: 'flex',
          alignItems: 'center',
          gap: 12,
          padding: '8px 22px',
          borderRadius: 999,
          background: 'rgba(255,107,91,0.12)',
          border: '1px solid rgba(255,107,91,0.35)',
          boxShadow: '0 0 24px rgba(255,107,91,0.2)',
          backdropFilter: 'blur(12px)',
        }}>
          <span style={{ position: 'relative', width: 9, height: 9, display: 'inline-block' }}>
            <span style={{ position: 'absolute', inset: 0, borderRadius: '50%', background: '#ff6b5b' }} />
            <span style={{
              position: 'absolute',
              inset: 0,
              borderRadius: '50%',
              background: '#ff6b5b',
              animation: 'ariaRipple 1.6s infinite',
            }} />
          </span>
          <span style={{
            fontFamily: C.mono,
            fontSize: 12,
            fontWeight: 700,
            letterSpacing: '.24em',
            color: '#ff8a7a',
            textTransform: 'uppercase',
          }}>
            {voiceState}
          </span>
        </div>

        {/* Big Transcript */}
        <div style={{
          minHeight: 44,
          maxWidth: 820,
          textAlign: 'center',
          fontFamily: "'Syne', sans-serif",
          fontSize: 'clamp(24px, 3.5vw, 36px)',
          fontWeight: 700,
          letterSpacing: '-0.02em',
          color: '#ffffff',
          lineHeight: 1.35,
          padding: '0 30px',
          textShadow: '0 4px 24px rgba(0,0,0,0.6)',
        }}>
          {a.transcript || '…'}
        </div>
      </div>

      {/* ─── Bottom Reply & Controls ─── */}
      <div style={{
        position: 'absolute',
        bottom: 80,
        left: 0,
        right: 0,
        display: 'flex',
        flexDirection: 'column',
        alignItems: 'center',
        gap: 22,
      }}>
        {a.ariaReply && (
          <div style={{
            fontFamily: "'Plus Jakarta Sans', sans-serif",
            fontSize: 14.5,
            color: '#cbd5e1',
            maxWidth: 680,
            textAlign: 'center',
            lineHeight: 1.6,
            padding: '12px 24px',
            background: 'rgba(10, 15, 24, 0.7)',
            borderRadius: 16,
            border: '1px solid rgba(255,255,255,0.08)',
            boxShadow: '0 8px 32px rgba(0,0,0,0.4)',
            backdropFilter: 'blur(16px)',
          }}>
            {a.ariaReply}
          </div>
        )}

        {showManualInput && (
          <div style={manualBar}>
            <input
              ref={inputRef}
              value={a.input}
              onChange={(e) => a.setInput(e.target.value)}
              onKeyDown={(e) => {
                if (e.key === 'Enter') handleManualSend();
              }}
              placeholder="Введите запрос или голосовую команду…"
              autoFocus
              style={{
                flex: 1,
                background: 'transparent',
                border: 'none',
                outline: 'none',
                color: '#fff',
                fontFamily: "'Plus Jakarta Sans', sans-serif",
                fontSize: 14.5,
              }}
            />
            <button onClick={a.retryListen} style={iconBtn} title="Повторить прослушивание">
              <MicIcon color="#94a3b8" size={19} />
            </button>
            <button onClick={handleManualSend} style={sendBtn} title="Отправить">
              <Icon name="send" color="#ffffff" size={18} width={2} />
            </button>
          </div>
        )}

        <button
          onClick={a.exitVoice}
          style={exitBtn}
          onMouseEnter={(e) => {
            e.currentTarget.style.background = 'rgba(255,255,255,0.08)';
            e.currentTarget.style.borderColor = 'rgba(255,255,255,0.2)';
            e.currentTarget.style.color = '#fff';
          }}
          onMouseLeave={(e) => {
            e.currentTarget.style.background = 'rgba(10, 15, 24, 0.8)';
            e.currentTarget.style.borderColor = 'rgba(255,255,255,0.1)';
            e.currentTarget.style.color = '#cbd5e1';
          }}
        >
          <Icon name="close" size={14} width={2} />
          <span>Завершить голосовой режим · Esc</span>
        </button>
      </div>

      {a.popups.map((p, i) => (
        <div key={i} style={p.style as React.CSSProperties}>
          <div style={{ display: 'flex', alignItems: 'center', gap: 9, marginBottom: 11 }}>
            <span style={{ width: 6, height: 6, borderRadius: '50%', background: p.accent, boxShadow: `0 0 8px ${p.accent}` }} />
            <span style={{ fontFamily: C.mono, fontSize: 10, letterSpacing: '.18em', color: p.accent, textTransform: 'uppercase' }}>
              {p.title}
            </span>
          </div>
          <div style={{ fontSize: 15, color: C.textHi, lineHeight: 1.5 }}>{p.body}</div>
          {p.hasMeta && <div style={{ marginTop: 9, fontFamily: C.mono, fontSize: 11, color: C.tableDim }}>{p.meta}</div>}
        </div>
      ))}
    </div>
  );
}

const exitBtn: React.CSSProperties = {
  display: 'flex',
  alignItems: 'center',
  gap: 10,
  padding: '11px 26px',
  borderRadius: 999,
  border: '1px solid rgba(255,255,255,0.1)',
  background: 'rgba(10, 15, 24, 0.8)',
  color: '#cbd5e1',
  cursor: 'pointer',
  fontFamily: "'Plus Jakarta Sans', sans-serif",
  fontSize: 13,
  fontWeight: 600,
  letterSpacing: '.04em',
  boxShadow: '0 8px 24px rgba(0,0,0,0.4)',
  backdropFilter: 'blur(16px)',
  transition: 'all .18s',
};

const manualBar: React.CSSProperties = {
  width: '90%',
  maxWidth: 620,
  display: 'flex',
  gap: 10,
  alignItems: 'center',
  background: 'rgba(10, 15, 24, 0.9)',
  border: '1px solid rgba(255,255,255,0.12)',
  borderRadius: 24,
  padding: '7px 8px 7px 20px',
  boxShadow: '0 12px 40px rgba(0,0,0,0.5)',
  backdropFilter: 'blur(20px)',
};

const iconBtn: React.CSSProperties = {
  width: 40,
  height: 40,
  borderRadius: 12,
  border: 'none',
  background: 'rgba(255,255,255,0.05)',
  color: '#94a3b8',
  cursor: 'pointer',
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'center',
  transition: 'all .18s',
};

const sendBtn: React.CSSProperties = {
  width: 40,
  height: 40,
  borderRadius: 12,
  border: 'none',
  background: 'linear-gradient(135deg, #ff6b5b 0%, #ff8b7b 100%)',
  color: '#ffffff',
  cursor: 'pointer',
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'center',
  boxShadow: '0 4px 16px rgba(255,107,91,0.4)',
  transition: 'all .18s',
};
