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
  { key: 'chat', label: 'Чат', short: 'ЧАТ', ic: 'chat', idx: '02', title: 'Диалог ARIA', sub: 'нейросетевой ассистент · вызов инструментов' },
  { key: 'network', label: 'Сеть', short: 'СЕТЬ', ic: 'network', idx: '03', title: 'Сетевой телескоп', sub: 'захват пакетов · протоколы · инспектор' },
  { key: 'devices', label: 'Устройства', short: 'УСТР', ic: 'camera', idx: '03.5', title: 'Матрица устройств', sub: 'камеры · хосты · Art-Net · телеметрия' },
  { key: 'system', label: 'Система', short: 'СИС', ic: 'system', idx: '04', title: 'Системный монитор', sub: 'ядра CPU · память · потоки · задержки' },
  { key: 'terminal', label: 'Терминал', short: 'TERM', ic: 'terminal', idx: '05', title: 'Терминал агента', sub: 'aria@core shell · прямой доступ' },
  { key: 'files', label: 'Файлы', short: 'ФАЙЛ', ic: 'files', idx: '06', title: 'Файловый менеджер', sub: 'хранилище агента · просмотр и логи' },
  { key: 'tasks', label: 'Задачи', short: 'ЗАД', ic: 'tasks', idx: '07', title: 'Автоматизация', sub: 'планировщик ARIA · cron · пайплайны' },
  { key: 'settings', label: 'Настройки', short: 'НАСТ', ic: 'settings', idx: '08', title: 'Параметры ядра', sub: 'голос · ASR/TTS · модели · API' },
  { key: 'dev', label: 'Dev', short: 'DEV', ic: 'shield', idx: '09', title: 'Консоль инженера', sub: 'opencode · правки кода · безопасность' },
];

export default function Shell({ a, children, onLogout, connected }: { a: AssistantState; children: ReactNode; onLogout: () => void; connected: boolean }) {
  const clock = useClock();
  const vp = useViewport();
  const compact = vp.compact;
  const meta = NAV.find((n) => n.key === a.screen) || { idx: '--', title: '', sub: '' };
  const nav = a.isAdmin ? NAV : NAV.filter((n) => n.key !== 'files' && n.key !== 'dev');

  const sideW = compact ? 62 : 76;

  return (
    <div style={{ position: 'absolute', inset: 0, zIndex: 3, display: 'flex', overflow: 'hidden' }}>
      {/* ─── Ultra-Sleek Glass Sidebar ─── */}
      <aside style={{ ...sidebar, width: sideW }}>
        {/* Holographic Core Jewel Button */}
        <button
          onClick={a.goHome}
          title="Вернуться к Ядру ARIA"
          style={coreBtn}
          onMouseEnter={(e) => {
            e.currentTarget.style.boxShadow = `0 0 28px ${acc(0.65)}, inset 0 0 12px ${acc(0.4)}`;
            e.currentTarget.style.transform = 'scale(1.05)';
          }}
          onMouseLeave={(e) => {
            e.currentTarget.style.boxShadow = `0 0 16px ${acc(0.3)}, inset 0 0 6px ${acc(0.15)}`;
            e.currentTarget.style.transform = 'scale(1)';
          }}
        >
          <div style={coreInnerPulse} />
          <svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="#ffb8ad" strokeWidth="1.5">
            <circle cx="12" cy="12" r="8" strokeOpacity="0.8" />
            <circle cx="12" cy="12" r="3.2" fill="#ff6b5b" stroke="none" />
          </svg>
        </button>

        {/* Tab Navigation Icons */}
        <nav style={{ display: 'flex', flexDirection: 'column', gap: 6, width: '100%', alignItems: 'center' }}>
          {nav.map((n) => {
            const active = a.screen === n.key;
            return (
              <button
                key={n.key}
                onClick={() => a.go(n.key as any)}
                title={n.label}
                style={{
                  ...navBtn,
                  width: sideW - 16,
                  ...(active ? navActive : null),
                }}
                onMouseEnter={(e) => {
                  if (!active) {
                    e.currentTarget.style.background = 'rgba(255,255,255,0.05)';
                    e.currentTarget.style.color = C.textHi;
                    e.currentTarget.style.transform = 'translateX(2px)';
                  }
                }}
                onMouseLeave={(e) => {
                  if (!active) {
                    e.currentTarget.style.background = 'transparent';
                    e.currentTarget.style.color = C.textFaint;
                    e.currentTarget.style.transform = 'none';
                  }
                }}
              >
                <div style={{ position: 'relative', display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
                  <Icon name={n.ic as any} color={active ? C.accent : C.textFaint} size={20} />
                  {active && (
                    <span style={{
                      position: 'absolute', inset: -4, borderRadius: '50%',
                      background: 'radial-gradient(circle, rgba(255,107,91,0.3) 0%, transparent 70%)',
                      pointerEvents: 'none',
                    }} />
                  )}
                </div>
                <span style={{
                  fontSize: compact ? 0 : 9.5,
                  fontWeight: active ? 700 : 500,
                  letterSpacing: '.08em',
                  fontFamily: C.mono,
                  color: active ? C.accent : 'inherit',
                  transition: 'color .18s',
                }}>
                  {n.short}
                </span>
                {active && <span style={activePillIndicator} />}
              </button>
            );
          })}
        </nav>

        <div style={{ flex: 1 }} />

        {/* Voice Trigger Jewel */}
        <button
          onClick={a.enterVoice}
          title="Голосовой режим ARIA"
          style={micBtn}
          onMouseEnter={(e) => {
            e.currentTarget.style.background = acc(0.22);
            e.currentTarget.style.boxShadow = `0 0 20px ${acc(0.4)}`;
            e.currentTarget.style.transform = 'scale(1.06)';
          }}
          onMouseLeave={(e) => {
            e.currentTarget.style.background = acc(0.1);
            e.currentTarget.style.boxShadow = `0 0 10px ${acc(0.15)}`;
            e.currentTarget.style.transform = 'scale(1)';
          }}
        >
          <MicIcon color={C.accent} size={20} />
        </button>
      </aside>

      {/* ─── Main Content Canvas with Glass Topbar ─── */}
      <div style={{ flex: 1, display: 'flex', flexDirection: 'column', minWidth: 0, position: 'relative' }}>
        {/* Topbar */}
        <header style={topbar}>
          {/* Left Title & Breadcrumbs */}
          <div style={{ display: 'flex', alignItems: 'center', gap: 14 }}>
            <span style={indexPill}>
              {meta.idx}
            </span>
            <div>
              <div style={{
                fontFamily: "'Syne', sans-serif",
                fontSize: compact ? 17 : 20,
                fontWeight: 700,
                letterSpacing: '-0.02em',
                lineHeight: 1.15,
                color: '#f2f5fa',
              }}>
                {meta.title}
              </div>
              {!compact && (
                <div style={{
                  fontFamily: C.mono,
                  fontSize: 11,
                  color: 'rgba(126,139,160,0.85)',
                  marginTop: 3,
                  letterSpacing: '.03em',
                }}>
                  {meta.sub}
                </div>
              )}
            </div>
          </div>

          {/* Right Status & Meta Controls */}
          <div style={{ display: 'flex', alignItems: 'center', gap: compact ? 10 : 16 }}>
            {/* Live Connection Pill */}
            <div style={statusPill}>
              <span style={{
                width: 7, height: 7, borderRadius: '50%',
                background: connected ? '#39d98a' : '#ff5470',
                boxShadow: connected ? '0 0 10px #39d98a' : '0 0 10px #ff5470',
                display: 'inline-block',
              }} />
              <span style={{
                fontFamily: C.mono,
                fontSize: 10,
                letterSpacing: '.12em',
                fontWeight: 600,
                color: connected ? '#bbf7d0' : '#fecdd3',
              }}>
                {connected ? 'ОНЛАЙН' : 'ОФФЛАЙН'}
              </span>
            </div>

            {/* User Identity Pill */}
            <div style={userPill}>
              <span style={{
                fontFamily: C.mono,
                fontSize: 11,
                color: C.cyan,
                letterSpacing: '.06em',
                fontWeight: 600,
              }}>
                @{a.username}
              </span>
              {!compact && (
                <span style={{
                  fontFamily: C.mono,
                  fontSize: 9.5,
                  fontWeight: 700,
                  padding: '2px 7px',
                  borderRadius: 6,
                  background: a.isAdmin ? 'rgba(255,107,91,0.18)' : 'rgba(56,189,248,0.15)',
                  color: a.isAdmin ? '#ff8a7a' : '#38bdf8',
                  letterSpacing: '.1em',
                  border: `1px solid ${a.isAdmin ? 'rgba(255,107,91,0.3)' : 'rgba(56,189,248,0.25)'}`,
                }}>
                  {a.isAdmin ? 'ADMIN' : 'USER'}
                </span>
              )}
            </div>

            {/* Logout Pill Button */}
            <button
              onClick={onLogout}
              style={logoutBtn}
              title="Завершить сессию"
              onMouseEnter={(e) => {
                e.currentTarget.style.background = 'rgba(255,84,112,0.14)';
                e.currentTarget.style.borderColor = 'rgba(255,84,112,0.35)';
                e.currentTarget.style.color = '#ff8a9e';
              }}
              onMouseLeave={(e) => {
                e.currentTarget.style.background = 'rgba(255,255,255,0.03)';
                e.currentTarget.style.borderColor = 'rgba(255,255,255,0.08)';
                e.currentTarget.style.color = C.textMid;
              }}
            >
              ВЫЙТИ
            </button>

            {/* Precise Digital Clock */}
            {!compact && (
              <div style={{
                fontFamily: C.mono,
                fontSize: 13,
                fontWeight: 600,
                color: '#f2f5fa',
                letterSpacing: '.06em',
                padding: '6px 12px',
                borderRadius: 8,
                background: 'rgba(255,255,255,0.02)',
                border: '1px solid rgba(255,255,255,0.05)',
              }}>
                {clock}
              </div>
            )}
          </div>
        </header>

        {/* Tab View Container */}
        <main style={{ flex: 1, overflow: 'hidden', position: 'relative' }}>
          {children}
        </main>
      </div>
    </div>
  );
}

const sidebar: React.CSSProperties = {
  flexShrink: 0,
  display: 'flex',
  flexDirection: 'column',
  alignItems: 'center',
  padding: '18px 0',
  gap: 8,
  borderRight: '1px solid rgba(255,255,255,0.06)',
  background: 'rgba(6, 8, 13, 0.72)',
  backdropFilter: 'blur(20px)',
  WebkitBackdropFilter: 'blur(20px)',
  zIndex: 10,
  transition: 'width .2s cubic-bezier(.4,0,.2,1)',
};

const coreBtn: React.CSSProperties = {
  width: 46,
  height: 46,
  borderRadius: '50%',
  border: `1px solid ${acc(0.4)}`,
  background: `radial-gradient(circle at 45% 35%, ${acc(0.4)}, rgba(10,14,24,0.95))`,
  cursor: 'pointer',
  marginBottom: 10,
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'center',
  boxShadow: `0 0 16px ${acc(0.3)}, inset 0 0 6px ${acc(0.15)}`,
  position: 'relative',
  transition: 'all .25s cubic-bezier(.4,0,.2,1)',
};

const coreInnerPulse: React.CSSProperties = {
  position: 'absolute',
  inset: 4,
  borderRadius: '50%',
  border: '1px solid rgba(255,255,255,0.2)',
  pointerEvents: 'none',
};

const navBtn: React.CSSProperties = {
  display: 'flex',
  flexDirection: 'column',
  alignItems: 'center',
  justifyContent: 'center',
  gap: 4,
  height: 52,
  borderRadius: 14,
  border: '1px solid transparent',
  background: 'transparent',
  color: C.textFaint,
  cursor: 'pointer',
  position: 'relative',
  fontFamily: C.ui,
  transition: 'all .18s cubic-bezier(.4,0,.2,1)',
};

const navActive: React.CSSProperties = {
  color: C.accent,
  background: 'linear-gradient(135deg, rgba(255,107,91,0.16) 0%, rgba(255,107,91,0.05) 100%)',
  border: '1px solid rgba(255,107,91,0.3)',
  boxShadow: '0 4px 16px rgba(255,107,91,0.12)',
};

const activePillIndicator: React.CSSProperties = {
  position: 'absolute',
  left: -2,
  top: 14,
  bottom: 14,
  width: 3,
  borderRadius: '0 4px 4px 0',
  background: C.accent,
  boxShadow: `0 0 10px ${C.accent}`,
};

const micBtn: React.CSSProperties = {
  width: 48,
  height: 48,
  borderRadius: 16,
  border: `1px solid ${acc(0.35)}`,
  background: acc(0.1),
  cursor: 'pointer',
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'center',
  boxShadow: `0 0 10px ${acc(0.15)}`,
  transition: 'all .2s cubic-bezier(.4,0,.2,1)',
};

const topbar: React.CSSProperties = {
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'space-between',
  padding: '14px 24px',
  borderBottom: '1px solid rgba(255,255,255,0.06)',
  background: 'rgba(6, 8, 13, 0.55)',
  backdropFilter: 'blur(20px)',
  WebkitBackdropFilter: 'blur(20px)',
  zIndex: 5,
};

const indexPill: React.CSSProperties = {
  fontFamily: C.mono,
  fontSize: 11,
  fontWeight: 700,
  color: C.accent,
  letterSpacing: '.12em',
  padding: '4px 9px',
  borderRadius: 8,
  background: 'rgba(255,107,91,0.1)',
  border: '1px solid rgba(255,107,91,0.25)',
  boxShadow: '0 0 12px rgba(255,107,91,0.12)',
};

const statusPill: React.CSSProperties = {
  display: 'flex',
  alignItems: 'center',
  gap: 7,
  padding: '5px 11px',
  borderRadius: 999,
  background: 'rgba(255,255,255,0.03)',
  border: '1px solid rgba(255,255,255,0.07)',
};

const userPill: React.CSSProperties = {
  display: 'flex',
  alignItems: 'center',
  gap: 8,
  padding: '5px 12px',
  borderRadius: 999,
  background: 'rgba(255,255,255,0.03)',
  border: '1px solid rgba(255,255,255,0.07)',
};

const logoutBtn: React.CSSProperties = {
  padding: '6px 14px',
  borderRadius: 999,
  border: '1px solid rgba(255,255,255,0.08)',
  background: 'rgba(255,255,255,0.03)',
  color: C.textMid,
  cursor: 'pointer',
  fontFamily: C.mono,
  fontSize: 10.5,
  fontWeight: 600,
  letterSpacing: '.12em',
  transition: 'all .18s cubic-bezier(.4,0,.2,1)',
};
