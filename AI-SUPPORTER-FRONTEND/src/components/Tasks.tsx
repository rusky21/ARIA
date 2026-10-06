import { C, w, R } from '../lib/theme.js';
import { Icon } from './Icon.jsx';
import type { AssistantState } from '../hooks/useAssistant.js';

export default function Tasks({ a }: { a: AssistantState }) {
  return (
    <div style={{ position: 'absolute', inset: 0, overflowY: 'auto', padding: '28px 32px', animation: 'ariaFadeFast .22s ease both' }}>
      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(420px, 1fr))', gap: 20, maxWidth: 1100, margin: '0 auto' }}>
        {a.tasks.map((t) => {
          const on = t.enabled;
          return (
            <div
              key={t.id}
              style={{
                background: on
                  ? 'linear-gradient(135deg, rgba(255,107,91,0.08) 0%, rgba(10,15,24,0.85) 100%)'
                  : 'rgba(10, 15, 24, 0.65)',
                border: on ? '1px solid rgba(255,107,91,0.28)' : '1px solid rgba(255,255,255,0.07)',
                borderRadius: 18,
                padding: '22px 24px',
                boxShadow: on ? '0 8px 32px rgba(255,107,91,0.1), 0 2px 10px rgba(0,0,0,0.3)' : '0 4px 20px rgba(0,0,0,0.2)',
                backdropFilter: 'blur(16px)',
                transition: 'all .2s cubic-bezier(.4,0,.2,1)',
              }}
            >
              {/* Header with Icon, Name & Controls */}
              <div style={{ display: 'flex', alignItems: 'flex-start', justifyContent: 'space-between', gap: 16 }}>
                <div style={{ display: 'flex', alignItems: 'center', gap: 14 }}>
                  <div style={{
                    width: 44,
                    height: 44,
                    borderRadius: 12,
                    background: on ? 'rgba(255,107,91,0.16)' : 'rgba(255,255,255,0.04)',
                    border: on ? '1px solid rgba(255,107,91,0.35)' : '1px solid rgba(255,255,255,0.08)',
                    display: 'flex',
                    alignItems: 'center',
                    justifyContent: 'center',
                    boxShadow: on ? '0 0 16px rgba(255,107,91,0.25)' : 'none',
                    transition: 'all .2s',
                  }}>
                    <Icon name={t.icon as any} color={on ? '#ff8a7a' : 'rgba(255,255,255,0.4)'} size={22} />
                  </div>

                  <div>
                    <div style={{
                      fontFamily: "'Syne', sans-serif",
                      fontSize: 16,
                      fontWeight: 700,
                      color: '#f8fafc',
                      letterSpacing: '-0.01em',
                    }}>
                      {t.name}
                    </div>
                    <div style={{
                      fontFamily: C.mono,
                      fontSize: 11,
                      color: on ? '#38bdf8' : 'rgba(126,139,160,0.7)',
                      marginTop: 3,
                    }}>
                      ⏱ {t.schedule}
                    </div>
                  </div>
                </div>

                <div style={{ display: 'flex', alignItems: 'center', gap: 12 }}>
                  <button
                    onClick={() => a.runTask(t.id)}
                    title="Запустить немедленно"
                    style={{
                      padding: '6px 12px',
                      borderRadius: 8,
                      border: '1px solid rgba(255,255,255,0.1)',
                      cursor: 'pointer',
                      fontFamily: "'Plus Jakarta Sans', sans-serif",
                      fontSize: 11.5,
                      fontWeight: 600,
                      display: 'inline-flex',
                      alignItems: 'center',
                      gap: 6,
                      background: 'rgba(255,255,255,0.04)',
                      color: '#cbd5e1',
                      transition: 'all .18s',
                    }}
                    onMouseEnter={(e) => {
                      e.currentTarget.style.background = 'rgba(56,189,248,0.15)';
                      e.currentTarget.style.borderColor = 'rgba(56,189,248,0.35)';
                      e.currentTarget.style.color = '#38bdf8';
                    }}
                    onMouseLeave={(e) => {
                      e.currentTarget.style.background = 'rgba(255,255,255,0.04)';
                      e.currentTarget.style.borderColor = 'rgba(255,255,255,0.1)';
                      e.currentTarget.style.color = '#cbd5e1';
                    }}
                  >
                    <Icon name="play" size={10} />
                    <span>Пуск</span>
                  </button>

                  <Switch on={on} onClick={() => a.toggleTask(t.id)} />
                </div>
              </div>

              {/* Description */}
              <div style={{
                fontFamily: "'Plus Jakarta Sans', sans-serif",
                fontSize: 13,
                color: 'rgba(203,213,225,0.85)',
                lineHeight: 1.6,
                margin: '16px 0',
              }}>
                {t.desc}
              </div>

              {/* Error Callout */}
              {t.lastError && (
                <div style={{
                  fontFamily: C.mono,
                  fontSize: 11,
                  color: '#ff8a9e',
                  background: 'rgba(255,84,112,0.1)',
                  border: '1px solid rgba(255,84,112,0.25)',
                  borderRadius: 8,
                  padding: '8px 12px',
                  marginBottom: 14,
                }}>
                  Ошибка: {t.lastError}
                </div>
              )}

              {/* Footer Meta */}
              <div style={{
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'space-between',
                borderTop: '1px solid rgba(255,255,255,0.05)',
                paddingTop: 14,
              }}>
                <span style={{ fontFamily: C.mono, fontSize: 11, color: 'rgba(126,139,160,0.8)' }}>
                  запуск: {t.lastRun || 'никогда'}
                  {t.nextRun ? ` · след: ${t.nextRun}` : ''}
                </span>

                <span style={{ display: 'flex', alignItems: 'center', gap: 7, fontFamily: C.mono, fontSize: 11, fontWeight: 600, color: on ? '#39d98a' : 'rgba(255,255,255,0.4)' }}>
                  <span style={{
                    width: 6,
                    height: 6,
                    borderRadius: '50%',
                    background: on ? '#39d98a' : 'rgba(255,255,255,0.3)',
                    boxShadow: on ? '0 0 8px #39d98a' : 'none',
                  }} />
                  {on ? 'АКТИВНА' : 'ПАУЗА'}
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
    <button
      onClick={onClick}
      style={{
        width: 44,
        height: 24,
        borderRadius: 14,
        border: 'none',
        cursor: 'pointer',
        position: 'relative',
        background: on ? 'linear-gradient(135deg, #ff6b5b 0%, #ff8b7b 100%)' : 'rgba(255,255,255,0.12)',
        boxShadow: on ? '0 0 12px rgba(255,107,91,0.4)' : 'none',
        transition: 'all .22s cubic-bezier(.4,0,.2,1)',
        flexShrink: 0,
      }}
    >
      <span style={{
        position: 'absolute',
        top: 3,
        left: on ? 23 : 3,
        width: 18,
        height: 18,
        borderRadius: '50%',
        background: '#ffffff',
        boxShadow: '0 2px 6px rgba(0,0,0,0.4)',
        transition: 'left .22s cubic-bezier(.4,0,.2,1)',
      }} />
    </button>
  );
}
