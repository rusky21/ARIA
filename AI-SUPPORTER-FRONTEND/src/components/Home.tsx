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
  { key: 'chat', title: 'Чат и LLM', sub: 'диалог · функции по API', ic: 'chat' },
  { key: 'network', title: 'Сетевой радар', sub: 'захват пакетов · live', ic: 'network' },
  { key: 'devices', title: 'Устройства', sub: 'камеры · Art-Net узлы', ic: 'camera' },
  { key: 'system', title: 'Монитор системы', sub: 'ресурсы в реальном времени', ic: 'system' },
  { key: 'terminal', title: 'Терминал ядра', sub: 'aria@core shell', ic: 'terminal' },
  { key: 'files', title: 'Файлы и база', sub: 'хранилище агента', ic: 'files' },
  { key: 'tasks', title: 'Автоматизация', sub: 'планировщик ARIA', ic: 'tasks' },
  { key: 'settings', title: 'Конфигурация', sub: 'голос · API · модели', ic: 'settings' },
  { key: 'dev', title: 'Dev Studio', sub: 'opencode · правки кода', ic: 'shield' },
];

export default function Home({ a }: { a: AssistantState }) {
  const clock = useClock();
  const vp = useViewport();
  const compact = vp.compact;
  const m = a.metrics;
  const cpuPct = Math.round(m.cpu);
  const ramPct = Math.round(m.ram);
  const online = a.devices.filter((d) => d.online).length;
  const running = a.tasks.filter((t) => t.enabled).length;

  const statusColor = a.speaking ? C.accent : a.listening ? C.cyan : C.green;
  const statusText = a.speaking ? 'говорю' : a.listening ? 'слушаю' : 'ядро активно';
  const modules = a.isAdmin ? MODULES : MODULES.filter((t) => t.key !== 'files' && t.key !== 'dev');

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
    <div style={pageContainer}>
      {/* ===== 1. TOP HEADER (Editorial Brand & Telemetry Chips) ===== */}
      <header style={headerStyle}>
        <div style={{ display: 'flex', alignItems: 'center', gap: 14 }}>
          <div style={brandTag}>ctrl // aria</div>
          <div style={liveBadge}>
            <GlowDot color={statusColor} size={7} pulse="ariaDot 1.8s infinite" />
            <span style={liveText}>{statusText}</span>
          </div>
        </div>

        {/* Center Pill Navigation */}
        {!compact && (
          <nav style={centerNav}>
            {modules.slice(0, 5).map((t) => (
              <button
                key={t.key}
                onClick={() => a.go(t.key as any)}
                style={navLinkBtn}
                onMouseEnter={(e) => {
                  e.currentTarget.style.color = '#ffffff';
                  e.currentTarget.style.background = 'rgba(255,255,255,0.06)';
                }}
                onMouseLeave={(e) => {
                  e.currentTarget.style.color = C.textMid;
                  e.currentTarget.style.background = 'transparent';
                }}
              >
                {t.title}
              </button>
            ))}
          </nav>
        )}

        {/* Right Telemetry & Clock */}
        <div style={headerRight}>
          <div style={metricPill}>
            <span style={{ color: C.textDim, fontSize: 10 }}>CPU</span>
            <span style={{ color: cpuPct > 70 ? C.red : C.textHi, fontWeight: 600 }}>{cpuPct}%</span>
            <span style={{ color: C.textDim, fontSize: 10, marginLeft: 4 }}>RAM</span>
            <span style={{ color: C.textHi, fontWeight: 600 }}>{ramPct}%</span>
          </div>
          <div style={metricPill}>
            <span style={{ color: a.captureRunning ? C.green : C.textDim, fontSize: 11, fontWeight: 600 }}>
              {a.captureRunning ? `${a.packets.length.toLocaleString('ru-RU')} ПАК` : 'СЕТЬ СТОП'}
            </span>
          </div>
          <div style={clockBadge}>{clock}</div>
        </div>
      </header>

      {/* Scrollable Editorial Body */}
      <div style={scrollContent}>
        {/* ===== 2. HERO SECTION (Asymmetric Reference Layout) ===== */}
        <section style={heroSection(compact)}>
          {/* Left Column: Bold Display Headline & Magnetic Action */}
          <div style={heroLeft}>
            <div style={subSectionTag}>01 // AUTONOMOUS CYBER COPILOT</div>
            <h1 style={heroHeadline(compact)}>
              we command <br />
              systems that <br />
              break through
            </h1>
            <p style={heroSubtext(compact)}>
              Автономный кибер-интеллект и системный сопроцессор. Синхронный анализ сетевого трафика,
              изоляция угроз и прямое голосовое управление инфраструктурой.
            </p>

            <div style={ctaRow}>
              <button onClick={a.enterVoice} style={btnPrimary}>
                <MicIcon color="#ffffff" size={18} />
                <span>Активировать голос</span>
              </button>

              <button onClick={() => a.go('chat')} style={btnSecondary}>
                <span>Открыть диалог</span>
                <span style={{ fontSize: 15 }}>↗</span>
              </button>
            </div>

            {a.wake && (
              <div style={wakeStatusPill}>
                <span style={{ width: 6, height: 6, borderRadius: '50%', background: C.cyan, boxShadow: `0 0 8px ${C.cyan}` }} />
                <span>слушаю «ария» · точность {Math.round(a.lastWakeScore * 100)}%</span>
              </div>
            )}
          </div>

          {/* Center Column: Visual Breathing Area for the 3D Chromatic Orb Canvas */}
          <div style={heroCenterSpacer(compact)} />

          {/* Right Column: Meta statement & quick deep links */}
          <div style={heroRight}>
            <div style={rightMetaTag}>LET'S SECURE TOGETHER</div>
            <h3 style={rightMetaTitle}>
              bring your infra <br />
              to autonomy — <br />
              aria@core.local
            </h3>
            <p style={rightMetaDesc}>
              Непрерывная верификация пакетов, zero-trust телеметрия и локальный синтез ответов без внешних утечек.
            </p>

            <div style={metaSpecs}>
              <div style={specRow}>
                <span style={specLabel}>ЯДРО ИНФЕРЕНСА</span>
                <span style={specVal}>Ollama / Qwen 2.5</span>
              </div>
              <div style={specRow}>
                <span style={specLabel}>УЗЛЫ И КАМЕРЫ</span>
                <span style={specVal}>{online} из {a.devices.length} онлайн</span>
              </div>
              <div style={specRow}>
                <span style={specLabel}>ПЛАНИРОВЩИК</span>
                <span style={specVal}>{running} задач в работе</span>
              </div>
            </div>

            <div style={{ display: 'flex', gap: 14 }}>
              <button onClick={() => a.go('network')} style={quickLinkBtn}>
                <span>сетевой радар ↗</span>
              </button>
              <button onClick={() => a.go('devices')} style={quickLinkBtn}>
                <span>устройства ↗</span>
              </button>
            </div>
          </div>
        </section>

        {/* ===== 3. HORIZONTAL PILLS MODULE BAR ===== */}
        <section style={modulePillsSection}>
          <div style={pillsContainer}>
            {modules.map((item) => (
              <button
                key={item.key}
                onClick={() => a.go(item.key as any)}
                style={categoryPill}
                onMouseEnter={(e) => {
                  e.currentTarget.style.borderColor = 'rgba(255, 255, 255, 0.28)';
                  e.currentTarget.style.color = '#ffffff';
                  e.currentTarget.style.background = 'rgba(255, 255, 255, 0.08)';
                  e.currentTarget.style.transform = 'translateY(-2px)';
                }}
                onMouseLeave={(e) => {
                  e.currentTarget.style.borderColor = 'rgba(255, 255, 255, 0.12)';
                  e.currentTarget.style.color = C.textMid;
                  e.currentTarget.style.background = 'rgba(255, 255, 255, 0.02)';
                  e.currentTarget.style.transform = 'translateY(0)';
                }}
              >
                {item.title.toUpperCase()}
              </button>
            ))}
          </div>
        </section>

        {/* ===== 4. EDITORIAL STATS & METRICS SECTION ===== */}
        <section style={statsSection}>
          <div style={statsGrid(compact)}>
            <div style={statsLeft}>
              <div style={subSectionTag}>02 // КЛЮЧЕВЫЕ ПОКАЗАТЕЛИ</div>
              <h2 style={statsMainHeading}>
                turning telemetry into impactful <br />
                protective intelligence
              </h2>
              <p style={statsSubParagraph}>
                Архитектура ARIA объединяет прямой перехват низкоуровневых пакетов,
                мониторинг аппаратных узлов и автоматическое выполнение пользовательских
                сценариев в защищённом контуре.
              </p>
            </div>

            <div style={statsNumbersGrid}>
              <div style={statNumberBox}>
                <div style={bigNumber}>12 ms</div>
                <div style={numberLabel}>ЗАДЕРЖКА ШИНЫ СОКЕТОВ</div>
              </div>
              <div style={statNumberBox}>
                <div style={bigNumber}>99.98%</div>
                <div style={numberLabel}>UPTIME АВТОНОМНОГО ЯДРА</div>
              </div>
              <div style={statNumberBox}>
                <div style={bigNumber}>{a.packets.length.toLocaleString('ru-RU')}</div>
                <div style={numberLabel}>ПАКЕТОВ ПРОИНСПЕКТИРОВАНО</div>
              </div>
              <div style={statNumberBox}>
                <div style={bigNumber}>{online} / {a.devices.length}</div>
                <div style={numberLabel}>АКТИВНЫХ УСТРОЙСТВ ОНЛАЙН</div>
              </div>
            </div>
          </div>
        </section>

        {/* ===== 5. CAPABILITIES BENTO GRID ===== */}
        <section style={servicesSection}>
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-end', marginBottom: 36 }}>
            <div>
              <div style={subSectionTag}>03 // СИСТЕМНЫЕ МОДУЛИ</div>
              <h2 style={servicesHeading}>bold digital for resilient infrastructure</h2>
            </div>
            <button onClick={() => a.go('tasks')} style={quickLinkBtn}>
              <span>все сценарии ↗</span>
            </button>
          </div>

          <div style={cardsGrid}>
            {modules.map((t, idx) => (
              <div
                key={t.key}
                onClick={() => a.go(t.key as any)}
                style={cardItem}
                onMouseEnter={(e) => {
                  e.currentTarget.style.borderColor = 'rgba(255, 107, 91, 0.4)';
                  e.currentTarget.style.transform = 'translateY(-3px)';
                  e.currentTarget.style.boxShadow = '0 16px 40px rgba(0, 0, 0, 0.5)';
                }}
                onMouseLeave={(e) => {
                  e.currentTarget.style.borderColor = 'rgba(255, 255, 255, 0.08)';
                  e.currentTarget.style.transform = 'translateY(0)';
                  e.currentTarget.style.boxShadow = 'none';
                }}
              >
                <div style={cardTop}>
                  <div style={iconBadge}>
                    <Icon name={t.ic as any} color={C.accent} size={20} />
                  </div>
                  <span style={cardIndex}>{String(idx + 1).padStart(2, '0')}</span>
                </div>
                <div>
                  <h4 style={cardTitle}>{t.title}</h4>
                  <div style={cardSubtext}>{t.sub}</div>
                </div>
                {metricFor(t.key) && (
                  <div style={{ ...metricBadge, color: metricColorFor(t.key) }}>
                    {metricFor(t.key)}
                  </div>
                )}
              </div>
            ))}
          </div>
        </section>

        {/* Footer */}
        <footer style={footerStyle}>
          <div style={{ color: C.textDim, fontSize: 12, fontFamily: C.mono }}>
            AI-SUPPORTER · ARIA NEURAL CORE v4.1.0 · AWARDS EDITION
          </div>
          <div style={{ display: 'flex', gap: 24, fontSize: 12, color: C.textDim }}>
            <span style={{ cursor: 'pointer' }} onClick={() => a.go('settings')}>КОНФИГУРАЦИЯ</span>
            <span style={{ cursor: 'pointer' }} onClick={() => a.go('files')}>ХРАНИЛИЩЕ</span>
            <span style={{ color: C.accent }}>STATUS: RESILIENT</span>
          </div>
        </footer>
      </div>
    </div>
  );
}

// ===== Styles =====

const pageContainer: React.CSSProperties = {
  position: 'absolute',
  inset: 0,
  zIndex: 3,
  display: 'flex',
  flexDirection: 'column',
  overflow: 'hidden',
  animation: 'ariaFade 0.6s cubic-bezier(0.16, 1, 0.3, 1) both',
};

const headerStyle: React.CSSProperties = {
  display: 'flex',
  justifyContent: 'space-between',
  alignItems: 'center',
  padding: '20px clamp(20px, 4vw, 56px)',
  background: 'linear-gradient(180deg, rgba(6, 8, 13, 0.85) 0%, rgba(6, 8, 13, 0) 100%)',
  zIndex: 10,
  flexShrink: 0,
};

const brandTag: React.CSSProperties = {
  fontFamily: "'Syne', sans-serif",
  fontSize: 22,
  fontWeight: 800,
  letterSpacing: '-0.04em',
  color: '#ffffff',
  textTransform: 'lowercase',
};

const liveBadge: React.CSSProperties = {
  display: 'flex',
  alignItems: 'center',
  gap: 8,
  padding: '4px 12px',
  borderRadius: 9999,
  background: 'rgba(255, 255, 255, 0.04)',
  border: '1px solid rgba(255, 255, 255, 0.08)',
};

const liveText: React.CSSProperties = {
  fontFamily: "'JetBrains Mono', monospace",
  fontSize: 11,
  color: '#8e9eaf',
  textTransform: 'uppercase',
  letterSpacing: '0.12em',
};

const centerNav: React.CSSProperties = {
  display: 'flex',
  alignItems: 'center',
  gap: 6,
  padding: '4px 8px',
  borderRadius: 9999,
  background: 'rgba(14, 20, 32, 0.5)',
  border: '1px solid rgba(255, 255, 255, 0.08)',
  backdropFilter: 'blur(16px)',
};

const navLinkBtn: React.CSSProperties = {
  padding: '6px 16px',
  borderRadius: 9999,
  border: 'none',
  background: 'transparent',
  color: '#8e9eaf',
  cursor: 'pointer',
  fontSize: 12,
  fontWeight: 500,
  letterSpacing: '0.04em',
  transition: 'all 0.2s',
};

const headerRight: React.CSSProperties = {
  display: 'flex',
  alignItems: 'center',
  gap: 12,
  fontFamily: "'JetBrains Mono', monospace",
};

const metricPill: React.CSSProperties = {
  display: 'flex',
  alignItems: 'center',
  gap: 6,
  padding: '6px 14px',
  borderRadius: 9999,
  background: 'rgba(255, 255, 255, 0.03)',
  border: '1px solid rgba(255, 255, 255, 0.06)',
  fontSize: 12,
};

const clockBadge: React.CSSProperties = {
  padding: '6px 14px',
  borderRadius: 9999,
  background: 'rgba(255, 107, 91, 0.1)',
  border: '1px solid rgba(255, 107, 91, 0.25)',
  color: '#ffd9d2',
  fontSize: 12,
  fontWeight: 600,
};

const scrollContent: React.CSSProperties = {
  flex: 1,
  overflowY: 'auto',
  overflowX: 'hidden',
  display: 'flex',
  flexDirection: 'column',
};

const heroSection = (compact: boolean): React.CSSProperties => ({
  display: 'grid',
  gridTemplateColumns: compact ? '1fr' : '1.2fr 1fr 0.9fr',
  alignItems: 'center',
  padding: compact 
    ? '24px 24px 60px' 
    : 'clamp(24px, 4vh, 48px) clamp(24px, 5vw, 64px) clamp(50px, 8vh, 90px)',
  position: 'relative',
  gap: compact ? 32 : 32,
});

const heroLeft: React.CSSProperties = {
  display: 'flex',
  flexDirection: 'column',
  justifyContent: 'center',
  zIndex: 4,
};

const subSectionTag: React.CSSProperties = {
  fontFamily: "'JetBrains Mono', monospace",
  fontSize: 11,
  letterSpacing: '0.18em',
  color: '#ff7a6b',
  textTransform: 'uppercase',
  marginBottom: 16,
};

const heroHeadline = (compact: boolean): React.CSSProperties => ({
  fontFamily: "'Syne', sans-serif",
  fontSize: compact ? 'clamp(34px, 6vw, 48px)' : 'clamp(38px, 4.4vw, 68px)',
  fontWeight: 800,
  lineHeight: 1.08,
  letterSpacing: '-0.04em',
  color: '#ffffff',
  textTransform: 'lowercase',
  marginBottom: 20,
});

const heroSubtext = (compact: boolean): React.CSSProperties => ({
  fontSize: compact ? 14 : 'clamp(14px, 1vw, 16px)',
  color: '#8e9eaf',
  lineHeight: 1.6,
  maxWidth: 460,
  marginBottom: 28,
});

const ctaRow: React.CSSProperties = {
  display: 'flex',
  alignItems: 'center',
  gap: 14,
  flexWrap: 'wrap',
  marginBottom: 14,
};

const btnPrimary: React.CSSProperties = {
  display: 'flex',
  alignItems: 'center',
  gap: 12,
  padding: '15px 34px',
  borderRadius: 9999,
  border: 'none',
  background: 'linear-gradient(135deg, #ff6b5b 0%, #ff3d68 100%)',
  color: '#ffffff',
  cursor: 'pointer',
  fontSize: 14,
  fontWeight: 700,
  letterSpacing: '0.04em',
  boxShadow: '0 0 32px rgba(255, 107, 91, 0.45)',
  transition: 'transform 0.2s cubic-bezier(0.16, 1, 0.3, 1), box-shadow 0.2s',
};

const btnSecondary: React.CSSProperties = {
  display: 'flex',
  alignItems: 'center',
  gap: 8,
  padding: '15px 26px',
  borderRadius: 9999,
  border: '1px solid rgba(255, 255, 255, 0.15)',
  background: 'rgba(255, 255, 255, 0.03)',
  color: '#f2f5fa',
  cursor: 'pointer',
  fontSize: 14,
  fontWeight: 600,
  backdropFilter: 'blur(12px)',
  transition: 'all 0.2s',
};

const wakeStatusPill: React.CSSProperties = {
  display: 'inline-flex',
  alignItems: 'center',
  gap: 8,
  fontFamily: "'JetBrains Mono', monospace",
  fontSize: 11,
  color: '#718096',
  letterSpacing: '0.08em',
};

const heroCenterSpacer = (compact: boolean): React.CSSProperties => ({
  minHeight: compact ? 200 : 340,
  pointerEvents: 'none',
});

const heroRight: React.CSSProperties = {
  display: 'flex',
  flexDirection: 'column',
  alignItems: 'flex-start',
  paddingLeft: 10,
  zIndex: 4,
};

const rightMetaTag: React.CSSProperties = {
  fontFamily: "'JetBrains Mono', monospace",
  fontSize: 11,
  letterSpacing: '0.15em',
  color: '#8e9eaf',
  textTransform: 'uppercase',
  marginBottom: 12,
};

const rightMetaTitle: React.CSSProperties = {
  fontFamily: "'Syne', sans-serif",
  fontSize: 'clamp(20px, 1.8vw, 26px)',
  fontWeight: 700,
  lineHeight: 1.25,
  color: '#ffffff',
  letterSpacing: '-0.02em',
  marginBottom: 14,
};

const rightMetaDesc: React.CSSProperties = {
  fontSize: 13,
  color: '#718096',
  lineHeight: 1.6,
  maxWidth: 320,
  marginBottom: 20,
};

const metaSpecs: React.CSSProperties = {
  width: '100%',
  maxWidth: 320,
  borderTop: '1px solid rgba(255, 255, 255, 0.08)',
  borderBottom: '1px solid rgba(255, 255, 255, 0.08)',
  padding: '12px 0',
  marginBottom: 20,
  display: 'flex',
  flexDirection: 'column',
  gap: 8,
};

const specRow: React.CSSProperties = {
  display: 'flex',
  justifyContent: 'space-between',
  fontFamily: "'JetBrains Mono', monospace",
  fontSize: 11,
};

const specLabel: React.CSSProperties = {
  color: '#586477',
};

const specVal: React.CSSProperties = {
  color: '#f2f5fa',
  fontWeight: 600,
};

const quickLinkBtn: React.CSSProperties = {
  display: 'flex',
  alignItems: 'center',
  gap: 6,
  background: 'transparent',
  border: 'none',
  color: '#ffffff',
  cursor: 'pointer',
  fontFamily: "'Syne', sans-serif",
  fontSize: 13,
  fontWeight: 700,
  letterSpacing: '0.02em',
  padding: 0,
};

const modulePillsSection: React.CSSProperties = {
  padding: '28px clamp(20px, 5vw, 64px)',
  display: 'flex',
  justifyContent: 'center',
  borderTop: '1px solid rgba(255, 255, 255, 0.06)',
  borderBottom: '1px solid rgba(255, 255, 255, 0.06)',
  background: 'rgba(6, 8, 13, 0.6)',
  position: 'relative',
  zIndex: 5,
};

const pillsContainer: React.CSSProperties = {
  display: 'flex',
  gap: 10,
  flexWrap: 'wrap',
  justifyContent: 'center',
  maxWidth: 1240,
};

const categoryPill: React.CSSProperties = {
  padding: '10px 22px',
  borderRadius: 9999,
  border: '1px solid rgba(255, 255, 255, 0.12)',
  background: 'rgba(255, 255, 255, 0.02)',
  color: '#8e9eaf',
  cursor: 'pointer',
  fontFamily: "'JetBrains Mono', monospace",
  fontSize: 11,
  fontWeight: 600,
  letterSpacing: '0.1em',
  backdropFilter: 'blur(10px)',
  transition: 'all 0.25s cubic-bezier(0.16, 1, 0.3, 1)',
};

const statsSection: React.CSSProperties = {
  padding: '80px clamp(24px, 5vw, 64px)',
};

const statsGrid = (compact: boolean): React.CSSProperties => ({
  display: 'grid',
  gridTemplateColumns: compact ? '1fr' : '1.2fr 1.8fr',
  gap: compact ? 40 : 60,
  alignItems: 'flex-start',
});

const statsLeft: React.CSSProperties = {
  display: 'flex',
  flexDirection: 'column',
};

const statsMainHeading: React.CSSProperties = {
  fontFamily: "'Syne', sans-serif",
  fontSize: 'clamp(28px, 3.2vw, 44px)',
  fontWeight: 800,
  lineHeight: 1.15,
  letterSpacing: '-0.03em',
  color: '#ffffff',
  textTransform: 'lowercase',
  marginBottom: 20,
};

const statsSubParagraph: React.CSSProperties = {
  fontSize: 15,
  color: '#8e9eaf',
  lineHeight: 1.65,
  maxWidth: 440,
};

const statsNumbersGrid: React.CSSProperties = {
  display: 'grid',
  gridTemplateColumns: 'repeat(2, 1fr)',
  gap: '40px 30px',
};

const statNumberBox: React.CSSProperties = {
  display: 'flex',
  flexDirection: 'column',
  gap: 6,
};

const bigNumber: React.CSSProperties = {
  fontFamily: "'Syne', sans-serif",
  fontSize: 'clamp(36px, 4vw, 56px)',
  fontWeight: 800,
  letterSpacing: '-0.04em',
  color: '#ffffff',
};

const numberLabel: React.CSSProperties = {
  fontFamily: "'JetBrains Mono', monospace",
  fontSize: 11,
  letterSpacing: '0.14em',
  color: '#586477',
};

const servicesSection: React.CSSProperties = {
  padding: '60px clamp(24px, 5vw, 64px) 90px',
  borderTop: '1px solid rgba(255, 255, 255, 0.05)',
};

const servicesHeading: React.CSSProperties = {
  fontFamily: "'Syne', sans-serif",
  fontSize: 'clamp(26px, 3vw, 38px)',
  fontWeight: 800,
  letterSpacing: '-0.03em',
  color: '#ffffff',
  textTransform: 'lowercase',
  marginTop: 6,
};

const cardsGrid: React.CSSProperties = {
  display: 'grid',
  gridTemplateColumns: 'repeat(auto-fit, minmax(260px, 1fr))',
  gap: 18,
};

const cardItem: React.CSSProperties = {
  padding: 24,
  borderRadius: 18,
  background: 'rgba(14, 20, 32, 0.55)',
  border: '1px solid rgba(255, 255, 255, 0.08)',
  cursor: 'pointer',
  backdropFilter: 'blur(16px)',
  display: 'flex',
  flexDirection: 'column',
  gap: 12,
  transition: 'all 0.25s cubic-bezier(0.16, 1, 0.3, 1)',
};

const cardTop: React.CSSProperties = {
  display: 'flex',
  justifyContent: 'space-between',
  alignItems: 'center',
};

const iconBadge: React.CSSProperties = {
  width: 42,
  height: 42,
  borderRadius: 12,
  background: 'rgba(255, 107, 91, 0.1)',
  border: '1px solid rgba(255, 107, 91, 0.22)',
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'center',
};

const cardIndex: React.CSSProperties = {
  fontFamily: "'JetBrains Mono', monospace",
  fontSize: 12,
  color: '#586477',
};

const cardTitle: React.CSSProperties = {
  fontFamily: "'Syne', sans-serif",
  fontSize: 18,
  fontWeight: 700,
  letterSpacing: '-0.02em',
  color: '#ffffff',
  textTransform: 'lowercase',
};

const cardSubtext: React.CSSProperties = {
  fontSize: 12,
  color: '#718096',
  lineHeight: 1.5,
};

const metricBadge: React.CSSProperties = {
  marginTop: 'auto',
  fontFamily: "'JetBrains Mono', monospace",
  fontSize: 11,
  fontWeight: 600,
  padding: '4px 10px',
  borderRadius: 6,
  background: 'rgba(255, 255, 255, 0.03)',
  border: '1px solid rgba(255, 255, 255, 0.05)',
  width: 'fit-content',
};

const footerStyle: React.CSSProperties = {
  display: 'flex',
  justifyContent: 'space-between',
  alignItems: 'center',
  padding: '30px clamp(24px, 5vw, 64px)',
  borderTop: '1px solid rgba(255, 255, 255, 0.06)',
  background: 'rgba(4, 6, 10, 0.9)',
};