import { useEffect, useState, type ReactNode } from 'react';
import { C, R, FS, acc, w, cyanA } from '../lib/theme.js';
import { Icon, MicIcon } from './Icon.jsx';
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

const NAV: { key: string; label: string; short: string; ic: string; idx: string; title: string; sub: string }[] = [
  { key: 'chat', label: 'Чат', short: 'ЧАТ', ic: 'chat', idx: '02', title: 'Чат с ARIA', sub: 'диалог · функции по API' },
  { key: 'network', label: 'Сеть', short: 'СЕТЬ', ic: 'network', idx: '03', title: 'Сетевой анализ', sub: 'захват пакетов · live' },
  { key: 'devices', label: 'Устройства', short: 'УСТР', ic: 'camera', idx: '03.5', title: 'Устройства сети', sub: 'камеры · компьютеры · ArtNet' },
  { key: 'system', label: 'Система', short: 'СИС', ic: 'system', idx: '04', title: 'Системный монитор', sub: 'ресурсы в реальном времени' },
  { key: 'terminal', label: 'Терминал', short: 'TERM', ic: 'terminal', idx: '05', title: 'Терминал агента', sub: 'aria@core shell' },
  { key: 'files', label: 'Файлы', short: 'ФАЙЛ', ic: 'files', idx: '06', title: 'Файлы и логи', sub: 'хранилище агента' },
  { key: 'tasks', label: 'Задачи', short: 'ЗАД', ic: 'tasks', idx: '07', title: 'Задачи и автоматизации', sub: 'планировщик ARIA' },
  { key: 'settings', label: 'Настройки', short: 'НАСТ', ic: 'settings', idx: '08', title: 'Настройки', sub: 'голос · API · модели' },
  { key: 'dev', label: 'Dev', short: 'DEV', ic: 'shield', idx: '09', title: 'Режим разработчика', sub: 'opencode · правки кода' },
]

export default function Shell({ a, children, onLogout, connected }: { a: AssistantState; children: ReactNode; onLogout: () => void; connected: boolean }) {
  const clock = useClock();
  const vp = useViewport();
  const compact = vp.compact;
  const meta = NAV.find((n) => n.key === a.screen) || { idx: '--', title: '', sub: '' };
  const nav = a.isAdmin ? NAV : NAV.filter((n) => n.key !== 'files' && n.key !== 'dev');

  const sideW = compact ? 60 : 78;

  return (
    <div style={{ position: 'absolute', inset: 0, zIndex: 3, display: 'flex' }}>
      <div style={{ ...sidebar, width: sideW }}>
        <button onClick={a.goHome} title="Ядро" style={coreBtn}
          onMouseEnter={(e) => (e.currentTarget.style.boxShadow = `0 0 26px ${acc(0.5)}`)}
          onMouseLeave={(e) => (e.currentTarget.style.boxShadow = `0 0 18px ${acc(0.25)}`)}>
          <svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="#ffb8ad" strokeWidth="1.4">
            <circle cx="12" cy="12" r="8" />
            <circle cx="12" cy="12" r="3.2" fill="#ff6b5b" stroke="none" />
          </svg>
        </button>

        {nav.map((n) => {
          const active = a.screen === n.key;
          return (
            <button key={n.key} onClick={() => a.go(n.key as any)} title={n.label}
              style={{ ...navBtn, width: sideW - 16, ...(active ? navActive : null) }}
              onMouseEnter={(e) => { if (!active) { e.currentTarget.style.background = w(0.05); e.currentTarget.style.color = C.textMid; } }}
              onMouseLeave={(e) => { if (!active) { e.currentTarget.style.background = 'transparent'; e.currentTarget.style.color = C.textFaint; } }}>
              <Icon name={n.ic as any} color={active ? C.accent : C.textFaint} size={20} />
              <span style={{ fontSize: compact ? 0 : 9, letterSpacing: '.06em' }}>{n.short}</span>
              {active && <span style={activeBar} />}
            </button>
          );
        })}

        <div style={{ flex: 1 }} />
        <button onClick={a.enterVoice} title="Голос" style={micBtn}
          onMouseEnter={(e) => (e.currentTarget.style.background = acc(0.2))}
          onMouseLeave={(e) => (e.currentTarget.style.background = acc(0.1))}>
          <MicIcon color={C.accent} size={20} />
        </button>
      </div>

      <div style={{ flex: 1, display: 'flex', flexDirection: 'column', minWidth: 0 }}>
        <div style={topbar}>
          <div style={{ display: 'flex', alignItems: 'center', gap: 16 }}>
            <span style={{ fontFamily: C.mono, fontSize: FS.s, color: C.accent, letterSpacing: '.12em' }}>{meta.idx}</span>
            <div>
              <div style={{ fontSize: FS.xxl, fontWeight: 600, letterSpacing: '.04em', lineHeight: 1.15 }}>{meta.title}</div>
              {!compact && <div style={{ fontFamily: C.mono, fontSize: FS.x, color: C.textDim, marginTop: 3 }}>{meta.sub}</div>}
            </div>
          </div>
          <div style={{ display: 'flex', alignItems: 'center', gap: compact ? 10 : 24, fontFamily: C.mono }}>
            <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
              <span style={{ width: 7, height: 7, borderRadius: '50%', background: connected ? C.green : C.red, boxShadow: connected ? `0 0 8px ${C.green}` : `0 0 8px ${C.red}` }} />
              <span style={{ fontSize: FS.x, color: C.textMid, letterSpacing: '.12em' }}>{connected ? 'ОНЛАЙН' : 'ОФЛАЙН'}</span>
            </div>
            <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
              <span style={{ fontSize: FS.x, color: C.cyan, letterSpacing: '.08em' }}>@{a.username}</span>
              {!compact && (
                <span style={{ fontSize: FS.x, padding: '2px 7px', borderRadius: R.xs, background: a.isAdmin ? acc(0.14) : cyanA(0.12), color: a.isAdmin ? C.accent : C.cyan, letterSpacing: '.1em' }}>
                  {a.isAdmin ? 'ADMIN' : 'USER'}
                </span>
              )}
            </div>
            <button onClick={onLogout} style={logoutBtn} title="Выйти"
              onMouseEnter={(e) => { e.currentTarget.style.background = w(0.06); e.currentTarget.style.borderColor = w(0.15); }}
              onMouseLeave={(e) => { e.currentTarget.style.background = w(0.03); e.currentTarget.style.borderColor = w(0.1); }}>
              <span style={{ fontSize: FS.x, letterSpacing: '.1em' }}>ВЫЙТИ</span>
            </button>
            {!compact && <div style={{ fontSize: FS.l, color: C.textHi, letterSpacing: '.04em' }}>{clock}</div>}
          </div>
        </div>

        <div style={{ flex: 1, overflow: 'hidden', position: 'relative' }}>{children}</div>
      </div>
    </div>
  );
}

const sidebar: React.CSSProperties = {
  flexShrink: 0, display: 'flex', flexDirection: 'column', alignItems: 'center',
  padding: '22px 0', gap: 6, borderRight: `1px solid ${w(0.06)}`,
  background: 'rgba(7,11,18,.66)', backdropFilter: 'blur(14px)', zIndex: 2,
  transition: 'width .2s',
};
const coreBtn: React.CSSProperties = {
  width: 46, height: 46, borderRadius: '50%', border: `1px solid ${acc(0.35)}`,
  background: `radial-gradient(circle at 50% 40%, ${acc(0.35)}, ${acc(0.05)})`,
  cursor: 'pointer', marginBottom: 12, display: 'flex', alignItems: 'center', justifyContent: 'center',
  boxShadow: `0 0 18px ${acc(0.25)}`, transition: 'box-shadow .2s',
};
const navBtn: React.CSSProperties = {
  display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center', gap: 4,
  height: 54, borderRadius: R.s, border: '1px solid transparent', background: 'transparent',
  color: C.textFaint, cursor: 'pointer', position: 'relative', fontFamily: C.ui,
  transition: 'all .18s cubic-bezier(.4,0,.2,1)',
};
const navActive: React.CSSProperties = { color: C.accent, background: acc(0.1), border: `1px solid ${acc(0.25)}` };
const activeBar: React.CSSProperties = { position: 'absolute', left: -1, top: 14, bottom: 14, width: 2, borderRadius: 2, background: C.accent, boxShadow: `0 0 8px ${C.accent}` };
const micBtn: React.CSSProperties = {
  width: 50, height: 50, borderRadius: R.m, border: `1px solid ${acc(0.3)}`,
  background: acc(0.1), cursor: 'pointer', display: 'flex', alignItems: 'center', justifyContent: 'center',
  transition: 'background .18s',
};
const topbar: React.CSSProperties = {
  display: 'flex', alignItems: 'center', justifyContent: 'space-between',
  padding: '16px 26px 14px', borderBottom: `1px solid ${w(0.06)}`,
  background: 'rgba(7,11,18,.4)', backdropFilter: 'blur(10px)',
};
const logoutBtn: React.CSSProperties = {
  padding: '7px 12px', borderRadius: R.s, border: `1px solid ${w(0.1)}`,
  background: w(0.03), color: C.textMid, cursor: 'pointer',
  fontFamily: C.mono, transition: 'all .18s cubic-bezier(.4,0,.2,1)',
};