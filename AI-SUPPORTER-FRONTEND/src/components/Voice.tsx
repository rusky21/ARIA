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
    <div style={{ position: 'absolute', inset: 0, zIndex: 4, background: 'rgba(3,5,11,.55)', backdropFilter: 'blur(2px)', animation: 'ariaFadeFast .22s ease both' }}>
      <div style={{ position: 'absolute', top: 30, left: 0, right: 0, display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 14 }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: 11, padding: '9px 20px', borderRadius: R.pill, background: acc(0.1), border: `1px solid ${acc(0.3)}` }}>
          <span style={{ position: 'relative', width: 9, height: 9 }}>
            <span style={{ position: 'absolute', inset: 0, borderRadius: '50%', background: C.accent }} />
            <span style={{ position: 'absolute', inset: 0, borderRadius: '50%', background: C.accent, animation: 'ariaRipple 1.6s infinite' }} />
          </span>
          <span style={{ fontFamily: C.mono, fontSize: 13, letterSpacing: '.28em', color: C.accentText, textTransform: 'uppercase' }}>{voiceState}</span>
        </div>
        <div style={{ minHeight: 34, maxWidth: 760, textAlign: 'center', fontSize: 26, fontWeight: 500, color: C.textHi, lineHeight: 1.4, padding: '0 30px' }}>
          {a.transcript || '…'}
        </div>
      </div>

      <div style={{ position: 'absolute', bottom: 96, left: 0, right: 0, display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 22 }}>
        <div style={{ fontFamily: C.mono, fontSize: 12, color: C.tableDim, maxWidth: 680, textAlign: 'center', lineHeight: 1.55 }}>{a.ariaReply}</div>

        {showManualInput && (
          <div style={manualBar}>
            <input
              ref={inputRef}
              value={a.input}
              onChange={(e) => a.setInput(e.target.value)}
              onKeyDown={(e) => { if (e.key === 'Enter') handleManualSend(); }}
              placeholder="Введите запрос…"
              autoFocus
              style={{ flex: 1, background: 'transparent', border: 'none', outline: 'none', color: C.textHi, fontFamily: C.ui, fontSize: 15 }}
            />
            <button onClick={a.retryListen} style={iconBtn} title="Повторить"><MicIcon color={C.tableDim} size={19} /></button>
            <button onClick={handleManualSend} style={sendBtn} title="Отправить"><Icon name="send" color={C.onAccent} size={19} width={2} /></button>
          </div>
        )}

        <button onClick={a.exitVoice} style={exitBtn}>
          <Icon name="close" size={15} width={2} /> Завершить · Esc
        </button>
      </div>

      {a.popups.map((p, i) => (
        <div key={i} style={p.style as React.CSSProperties}>
          <div style={{ display: 'flex', alignItems: 'center', gap: 9, marginBottom: 11 }}>
            <span style={{ width: 6, height: 6, borderRadius: '50%', background: p.accent, boxShadow: `0 0 8px ${p.accent}` }} />
            <span style={{ fontFamily: C.mono, fontSize: 10, letterSpacing: '.18em', color: p.accent, textTransform: 'uppercase' }}>{p.title}</span>
          </div>
          <div style={{ fontSize: 15, color: C.textHi, lineHeight: 1.5 }}>{p.body}</div>
          {p.hasMeta && <div style={{ marginTop: 9, fontFamily: C.mono, fontSize: 11, color: C.tableDim }}>{p.meta}</div>}
        </div>
      ))}
    </div>
  );
}

const exitBtn: React.CSSProperties = {
  display: 'flex', alignItems: 'center', gap: 10, padding: '12px 26px', borderRadius: R.pill,
  border: `1px solid ${w(0.12)}`, background: C.panel, color: C.textMid,
  cursor: 'pointer', fontFamily: C.ui, fontSize: 13, letterSpacing: '.06em', backdropFilter: 'blur(8px)',
};

const manualBar: React.CSSProperties = {
  width: '90%', maxWidth: 600, display: 'flex', gap: 10, alignItems: 'center',
  background: C.panelAlt, border: `1px solid ${w(0.12)}`,
  borderRadius: R.xl, padding: '8px 8px 8px 18px',
};

const iconBtn: React.CSSProperties = {
  width: 42, height: 42, borderRadius: R.lg, border: 'none',
  background: w(0.06), color: C.tableDim,
  cursor: 'pointer', display: 'flex', alignItems: 'center', justifyContent: 'center',
};

const sendBtn: React.CSSProperties = {
  width: 42, height: 42, borderRadius: R.lg, border: 'none',
  background: C.accent, color: C.onAccent,
  cursor: 'pointer', display: 'flex', alignItems: 'center', justifyContent: 'center',
};
