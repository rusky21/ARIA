import { useEffect, useRef } from 'react';
import { C } from '../lib/theme.js';
import type { AssistantState } from '../hooks/useAssistant.js';

export default function Terminal({ a }: { a: AssistantState }) {
  const scrollRef = useRef<HTMLDivElement>(null);
  useEffect(() => {
    const el = scrollRef.current;
    if (el) el.scrollTop = el.scrollHeight;
  }, [a.termLines]);

  return (
    <div style={{ position: 'absolute', inset: 0, display: 'flex', flexDirection: 'column', padding: 24, animation: 'ariaFadeFast .22s ease both' }}>
      {/* ─── Obsidian Glass Terminal Window ─── */}
      <div style={{
        flex: 1,
        display: 'flex',
        flexDirection: 'column',
        background: 'rgba(5, 8, 14, 0.92)',
        border: '1px solid rgba(255,255,255,0.08)',
        borderRadius: 16,
        overflow: 'hidden',
        boxShadow: '0 16px 48px rgba(0,0,0,0.6), inset 0 1px 0 rgba(255,255,255,0.08)',
        backdropFilter: 'blur(24px)',
      }}>
        {/* Terminal Header Bar */}
        <div style={{
          display: 'flex',
          alignItems: 'center',
          gap: 10,
          padding: '12px 20px',
          borderBottom: '1px solid rgba(255,255,255,0.06)',
          background: 'rgba(0, 0, 0, 0.4)',
        }}>
          <div style={{ display: 'flex', alignItems: 'center', gap: 7 }}>
            <Dot c="#ff5470" />
            <Dot c="#ffb454" />
            <Dot c="#39d98a" />
          </div>

          <div style={{
            marginLeft: 12,
            fontFamily: C.mono,
            fontSize: 11,
            color: 'rgba(126,139,160,0.85)',
            letterSpacing: '.06em',
            display: 'flex',
            alignItems: 'center',
            gap: 8,
          }}>
            <span style={{ color: '#38bdf8', fontWeight: 600 }}>aria@core</span>
            <span style={{ color: 'rgba(255,255,255,0.3)' }}>:</span>
            <span>~/agent</span>
            <span style={{
              fontSize: 9,
              padding: '1px 6px',
              borderRadius: 4,
              background: 'rgba(255,107,91,0.15)',
              color: '#ff8a7a',
              border: '1px solid rgba(255,107,91,0.25)',
              marginLeft: 8,
            }}>
              BASH SHELL
            </span>
          </div>
        </div>

        {/* Scrollable Terminal Output */}
        <div
          ref={scrollRef}
          style={{
            flex: 1,
            overflowY: 'auto',
            padding: '20px 24px',
            fontFamily: C.mono,
            fontSize: 12.5,
            lineHeight: 1.8,
            color: '#e2e8f0',
          }}
        >
          {a.termLines.map((l, i) => (
            <div key={i} style={{ whiteSpace: 'pre-wrap', color: l.color || '#cbd5e1' }}>
              {l.text}
            </div>
          ))}

          {/* Interactive Input Prompt */}
          <div style={{ display: 'flex', alignItems: 'center', gap: 10, marginTop: 6 }}>
            <span style={{ color: '#ff6b5b', fontWeight: 700, textShadow: '0 0 10px rgba(255,107,91,0.5)' }}>
              aria ➜
            </span>
            <input
              value={a.termInput}
              onChange={(e) => a.setTermInput(e.target.value)}
              onKeyDown={(e) => e.key === 'Enter' && a.submitTerm()}
              placeholder="введите shell команду…"
              autoFocus
              style={{
                flex: 1,
                background: 'transparent',
                border: 'none',
                outline: 'none',
                color: '#f8fafc',
                fontFamily: C.mono,
                fontSize: 12.5,
                caretColor: '#ff6b5b',
              }}
            />
          </div>
        </div>
      </div>
    </div>
  );
}

function Dot({ c }: { c: string }) {
  return (
    <span style={{
      width: 10,
      height: 10,
      borderRadius: '50%',
      background: c,
      boxShadow: `0 0 8px ${c}66`,
      display: 'inline-block',
    }} />
  );
}
