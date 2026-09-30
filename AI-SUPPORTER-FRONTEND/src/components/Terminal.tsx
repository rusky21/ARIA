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
    <div style={{ position: 'absolute', inset: 0, display: 'flex', flexDirection: 'column', padding: 20, animation: 'ariaFade .4s ease both' }}>
      <div style={{ flex: 1, display: 'flex', flexDirection: 'column', background: 'rgba(4,7,13,.85)', border: `1px solid ${C.line}`, borderRadius: 14, overflow: 'hidden' }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: 8, padding: '10px 16px', borderBottom: `1px solid ${C.line}`, background: 'rgba(0,0,0,.3)' }}>
          <Dot c="#ff5470" /><Dot c="#ffb454" /><Dot c="#39d98a" />
          <span style={{ marginLeft: 10, fontFamily: C.mono, fontSize: 11, color: '#7e8ba0' }}>aria@core: ~/agent</span>
        </div>
        <div ref={scrollRef} style={{ flex: 1, overflowY: 'auto', padding: '18px 20px', fontFamily: C.mono, fontSize: 13, lineHeight: 1.75 }}>
          {a.termLines.map((l, i) => (
            <div key={i} style={{ whiteSpace: 'pre-wrap', color: l.color }}>{l.text}</div>
          ))}
          <div style={{ display: 'flex', alignItems: 'center', gap: 8, marginTop: 4 }}>
            <span style={{ color: C.accent }}>aria ➜</span>
            <input
              value={a.termInput}
              onChange={(e) => a.setTermInput(e.target.value)}
              onKeyDown={(e) => e.key === 'Enter' && a.submitTerm()}
              placeholder="введите команду…"
              autoFocus
              style={{ flex: 1, background: 'transparent', border: 'none', outline: 'none', color: C.textHi, fontFamily: C.mono, fontSize: 13 }}
            />
          </div>
        </div>
      </div>
    </div>
  );
}

const Dot = ({ c }: { c: string }) => <span style={{ width: 11, height: 11, borderRadius: '50%', background: c }} />;
