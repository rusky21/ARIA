import { C, w, R } from '../lib/theme.js';
import { Icon } from './Icon.jsx';
import type { AssistantState } from '../hooks/useAssistant.js';

export default function Tasks({ a }: { a: AssistantState }) {
  return (
    <div style={{ position: 'absolute', inset: 0, overflowY: 'auto', padding: '26px 30px', animation: 'ariaFadeFast .22s ease both' }}>
      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(2,1fr)', gap: 18, maxWidth: 980 }}>
        {a.tasks.map((t) => {
          const on = t.enabled;
          return (
            <div key={t.id} style={{ background: C.panel, border: `1px solid ${on ? 'rgba(255,107,91,.18)' : C.line}`, borderRadius: R.lg, padding: 20 }}>
              <div style={{ display: 'flex', alignItems: 'flex-start', justifyContent: 'space-between', gap: 14 }}>
                <div style={{ display: 'flex', alignItems: 'center', gap: 12 }}>
                  <div style={{ width: 40, height: 40, borderRadius: 11, background: on ? 'rgba(255,107,91,.12)' : w(0.05), display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
                    <Icon name={t.icon as any} color={on ? C.accent : C.textFaint} size={20} />
                  </div>
                  <div>
                    <div style={{ fontSize: 16, fontWeight: 600, color: C.textHi }}>{t.name}</div>
                    <div style={{ fontFamily: C.mono, fontSize: 11, color: C.textDim, marginTop: 2 }}>{t.schedule}</div>
                  </div>
                </div>
                <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
                  <button onClick={() => a.runTask(t.id)} style={{
                    padding: '5px 11px', borderRadius: 6, border: `1px solid ${C.line}`, cursor: 'pointer',
                    fontFamily: C.ui, fontSize: 10.5, display: 'inline-flex', alignItems: 'center', gap: 5,
                    background: w(0.05), color: C.textMid,
                  }}><Icon name="play" size={10} /> Запустить</button>
                  <Switch on={on} onClick={() => a.toggleTask(t.id)} />
                </div>
              </div>
              <div style={{ fontSize: 13, color: C.tableDim, lineHeight: 1.55, margin: '14px 0' }}>{t.desc}</div>
              {t.lastError && (
                <div style={{ fontFamily: C.mono, fontSize: 11, color: C.red, background: 'rgba(255,84,112,.08)', border: '1px solid rgba(255,84,112,.2)', borderRadius: 6, padding: '6px 10px', marginBottom: 12 }}>
                  ошибка: {t.lastError}
                </div>
              )}
              <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', borderTop: `1px solid ${w(0.05)}`, paddingTop: 12 }}>
                <span style={{ fontFamily: C.mono, fontSize: 11, color: C.textDim }}>
                  последний запуск: {t.lastRun || 'никогда'}
                  {t.nextRun ? ` · следующий: ${t.nextRun}` : ''}
                </span>
                <span style={{ display: 'flex', alignItems: 'center', gap: 7, fontFamily: C.mono, fontSize: 11, color: on ? C.green : C.textDim }}>
                  <span style={{ width: 6, height: 6, borderRadius: '50%', background: on ? C.green : C.textDim }} />
                  {on ? 'активна' : 'на паузе'}
                </span>
              </div>
            </div>
          );
        })}
      </div>
    </div>
  );
}

export function Switch({ on, onClick }: { on: boolean; onClick: () => void }) {
  return (
    <button onClick={onClick} style={{ width: 44, height: 24, borderRadius: 14, border: 'none', cursor: 'pointer', position: 'relative', background: on ? C.accent : w(0.12), transition: 'background .2s', flexShrink: 0 }}>
      <span style={{ position: 'absolute', top: 3, left: on ? 23 : 3, width: 18, height: 18, borderRadius: '50%', background: '#fff', transition: 'left .2s' }} />
    </button>
  );
}
