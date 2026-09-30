import { useEffect, useState } from 'react';
import { C, R, FS, acc, w } from '../lib/theme.js';
import { Icon, MicIcon } from './Icon.jsx';
import { GlowDot } from '../lib/Table.jsx';
import { useViewport } from '../lib/viewport.js';
import type { AssistantState } from '../hooks/useAssistant.js';

function useClock() {
  const [t, setT] = useState(() => new Date());
  useEffect(() => {
    const iv = setInterval(() => setT(new Date()), 1000);
    return () => clearInterval(iv);
  }, []);
  return t.toLocaleTimeString('ru-RU', { hour: '2-digit', minute: '2-digit', second: '2-digit' });
}

const MODULES: { key: string; title: string; sub: string; ic: string }[] = [
  { key: 'chat', title: 'Чат', sub: 'диалог · функции по API', ic: 'chat' },
  { key: 'network', title: 'Сеть', sub: 'захват пакетов · live', ic: 'network' },
  { key: 'devices', title: 'Устройства', sub: 'камеры · Art-Net', ic: 'camera' },
  { key: 'system', title: 'Система', sub: 'ресурсы в реальном времени', ic: 'system' },
  { key: 'terminal', title: 'Терминал', sub: 'aria@core shell', ic: 'terminal' },
  { key: 'files', title: 'Файлы', sub: 'хранилище агента', ic: 'files' },
  { key: 'tasks', title: 'Задачи', sub: 'планировщик ARIA', ic: 'tasks' },
  { key: 'settings', title: 'Настройки', sub: 'голос · API · модели', ic: 'settings' },
  { key: 'dev', title: 'Dev', sub: 'opencode · правки кода', ic: 'shield' },
]

export default function Home({ a }: { a: AssistantState }) {
  const clock = useClock();
  const vp = useViewport();
  const m = a.metrics;
  const cpuPct = Math.round(m.cpu);
  const ramPct = Math.round(m.ram);
  const online = a.devices.filter((d) => d.online).length;
  const running = a.tasks.filter((t) => t.enabled).length;
  const statusColor = a.speaking ? C.accent : a.listening ? C.cyan : C.green;
  const statusText = a.speaking ? 'говорю' : a.listening ? 'слушаю' : 'в ожидании';
  const modules = (a.isAdmin ? MODULES : MODULES.filter((t) => t.key !== 'files' && t.key !== 'dev'));

  const metricFor = (key: string): string => {
    switch (key) {
      case 'chat': return `${a.messages.length} сообщений`;
      case 'network': return a.captureRunning ? `${a.packets.length.toLocaleString('ru-RU')} пакетов` : 'захват выключен';
      case 'devices': return `${online} из ${a.devices.length} онлайн`;
      case 'system': return `CPU ${cpuPct}% · ОЗУ ${ramPct}%`;
      case 'tasks': return running ? `${running} выполняется` : `${a.tasks.length} задач`;
      default: return '';
    }
  };
  const metricColorFor = (key: string): string => {
    if (key === 'network') return a.captureRunning ? C.green : C.textDim;
    if (key === 'devices') return online ? C.green : C.red;
    if (key === 'system') return cpuPct > 70 ? C.red : cpuPct > 50 ? C.amber : C.cyan;
    if (key === 'tasks') return running ? C.green : C.textMid;
    return C.textDim;
  };

  return (
    <div style={{ position: 'absolute', inset: 0, zIndex: 3, display: 'flex', flexDirection: 'column', animation: 'ariaFadeFast .22s ease both', overflow: 'hidden' }}>

      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', padding: '26px 40px', gap: 24 }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: 13 }}>
          <GlowDot color={C.accent} size={9} pulse="ariaBreath 2.4s infinite" />
          <div>
            <div style={{ fontSize: FS.m, fontWeight: 600, letterSpacing: '.4em', color: C.textHi }}>A R I A</div>
            <div style={{ fontFamily: C.mono, fontSize: FS.x, letterSpacing: '.1em', color: C.textDim, marginTop: 3 }}>v4.1 · ядро активно</div>
          </div>
        </div>

        <div style={{ display: 'flex', gap: vp.compact ? 10 : 14, fontFamily: C.mono }}>
          <Chip label="СИСТЕМА" value={`CPU ${cpuPct}% · ОЗУ ${ramPct}%`} color={C.textHi} accent={cpuPct > 70 ? C.red : C.cyan} />
          <Chip label="СЕТЬ" value={a.captureRunning ? `${a.packets.length.toLocaleString('ru-RU')} пак` : 'стоп'} color={C.textHi} accent={a.captureRunning ? C.green : C.textDim} />
          <Chip label="ВРЕМЯ" value={clock} color={C.textHi} accent={C.cyan} hideOnCompact={vp.compact} />
        </div>
      </div>

      <div style={{ flex: 1, display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center', paddingTop: 0 }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: 11, padding: '7px 18px', borderRadius: R.pill, background: w(0.03), border: `1px solid ${w(0.08)}`, backdropFilter: 'blur(8px)' }}>
          <GlowDot color={statusColor} size={7} pulse="ariaDot 1.8s infinite" />
          <span style={{ fontFamily: C.mono, fontSize: FS.s, letterSpacing: '.22em', color: C.accentText, textTransform: 'uppercase' }}>{statusText}</span>
        </div>

        <div style={{
          marginTop: 22, fontSize: 'clamp(46px, 8vw, 84px)', fontWeight: 700, letterSpacing: '.14em', lineHeight: 1,
          background: 'linear-gradient(180deg, #fff 8%, #ffc6bb 55%, #ff6b5b 115%)',
          WebkitBackgroundClip: 'text', backgroundClip: 'text', WebkitTextFillColor: 'transparent',
          filter: 'drop-shadow(0 6px 50px rgba(255,107,91,.4))',
        }}>ARIA</div>

        <div style={{ position: 'relative', width: vp.compact ? 220 : 280, height: 4, marginTop: 20, borderRadius: 3, background: w(0.08), overflow: 'hidden' }}>
          <div style={{ width: a.wake ? '100%' : '38%', height: '100%', borderRadius: 3, background: statusColor, transition: 'width .4s ease, background .3s', opacity: a.wake ? 1 : 0.6 }} />
        </div>

        <div style={{ marginTop: 18, fontSize: FS.xl, color: C.textDim, textAlign: 'center', maxWidth: 480, lineHeight: 1.6 }}>
          {a.wake && !a.speaking && !a.listening
            ? <span style={{ fontFamily: C.mono, color: C.textMid, fontSize: FS.s, letterSpacing: '.14em' }}>слушаю «ария» · порог {Math.round(a.lastWakeScore * 100)}%</span>
            : 'Голосовой ИИ-ассистент. Спрашивайте, командуйте или работайте вручную через модули.'}
        </div>

        <button
          onClick={a.enterVoice}
          style={{
            marginTop: 30, display: 'flex', alignItems: 'center', gap: 13, padding: '14px 30px',
            borderRadius: R.pill, border: `1px solid ${acc(0.35)}`, background: acc(0.08),
            color: C.accentText, cursor: 'pointer', fontFamily: C.ui, fontSize: FS.l, fontWeight: 600,
            letterSpacing: '.06em', backdropFilter: 'blur(8px)',
            boxShadow: `0 0 24px ${acc(0.12)}`, transition: 'all .2s cubic-bezier(.4,0,.2,1)',
          }}
          onMouseEnter={(e) => { e.currentTarget.style.background = acc(0.16); e.currentTarget.style.boxShadow = `0 0 40px ${acc(0.3)}`; }}
          onMouseLeave={(e) => { e.currentTarget.style.background = acc(0.08); e.currentTarget.style.boxShadow = `0 0 24px ${acc(0.12)}`; }}>
          <MicIcon color={C.accent} size={20} /> Активировать голос
        </button>
      </div>

      <div style={{ padding: '0 40px 30px', display: 'flex', justifyContent: 'center' }}>
        <div style={{
          width: '100%', maxWidth: 1160, display: 'grid',
          gridTemplateColumns: `repeat(auto-fit, minmax(${vp.compact ? 168 : 208}px, 1fr))`,
          gap: vp.compact ? 10 : 14,
        }}>
          {modules.map((t) => (
            <button key={t.key} onClick={() => a.go(t.key as any)}
              style={{
                display: 'flex', alignItems: 'center', gap: 14, padding: '14px 16px', borderRadius: R.lg,
                background: 'rgba(10,15,26,.55)', border: `1px solid ${C.line}`, cursor: 'pointer',
                textAlign: 'left', transition: 'all .2s cubic-bezier(.4,0,.2,1)', position: 'relative', overflow: 'hidden',
              }}
              onMouseEnter={(e) => { e.currentTarget.style.borderColor = acc(0.3); e.currentTarget.style.background = w(0.04); e.currentTarget.style.transform = 'translateY(-2px)'; }}
              onMouseLeave={(e) => { e.currentTarget.style.borderColor = C.line; e.currentTarget.style.background = 'rgba(10,15,26,.55)'; e.currentTarget.style.transform = 'none'; }}
            >
              <span style={{ width: 40, height: 40, borderRadius: R.m, background: acc(0.08), border: `1px solid ${acc(0.18)}`, display: 'flex', alignItems: 'center', justifyContent: 'center', flexShrink: 0 }}>
                <Icon name={t.ic as any} color={C.accent} size={19} />
              </span>
              <span style={{ display: 'flex', flexDirection: 'column', gap: 3, minWidth: 0 }}>
                <span style={{ fontSize: FS.l, fontWeight: 600, color: C.textHi }}>{t.title}</span>
                {metricFor(t.key) && (
                  <span style={{ fontFamily: C.mono, fontSize: FS.x, color: metricColorFor(t.key), whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>
                    {metricFor(t.key)}
                  </span>
                )}
                <span style={{ fontSize: vp.compact ? 0 : FS.x, color: C.textDim, whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>{t.sub}</span>
              </span>
            </button>
          ))}
        </div>
      </div>
    </div>
  );
}

function Chip({ label, value, color, accent, hideOnCompact }: { label: string; value: string; color: string; accent: string; hideOnCompact?: boolean }) {
  if (hideOnCompact) return null;
  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 3, padding: '9px 14px', borderRadius: R.lg, background: 'rgba(10,15,26,.5)', border: `1px solid ${C.line}` }}>
      <div style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
        <span style={{ width: 5, height: 5, borderRadius: '50%', background: accent, boxShadow: `0 0 8px ${accent}` }} />
        <span style={{ fontSize: FS.x, letterSpacing: '.14em', color: C.textDim }}>{label}</span>
      </div>
      <div style={{ fontSize: FS.xl, color, letterSpacing: '.02em', whiteSpace: 'nowrap' }}>{value}</div>
    </div>
  );
}