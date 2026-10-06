import { useEffect, useRef, useState } from 'react';
import { C, acc, w, FS } from '../lib/theme.js';
import { SortHead as TSortHead, TableRow, Td, MiniBar } from '../lib/Table.js';
import type { AssistantState } from '../hooks/useAssistant.js';
import type { ProcessInfo, TaskLoad } from '../lib/data.js';
import { Icon } from './Icon.jsx';

type SysTab = 'processes' | 'performance' | 'tasks';

const HISTORY = 60;

const taskStatus = (t: TaskLoad): { text: string; color: string } => {
  if (!t.enabled) return { text: 'выключена', color: 'rgba(255,255,255,0.4)' };
  if (t.inFlight) return { text: 'выполняется', color: '#39d98a' };
  if (t.lastError) return { text: 'ошибка', color: '#ff5470' };
  if (t.continuous) return { text: 'постоянно', color: '#39d98a' };
  return { text: t.nextRun ? `след. ${t.nextRun}` : 'активна', color: '#94a3b8' };
};

type SortState = { key: string; dir: 1 | -1 };

const useSort = (initialKey: string) => {
  const [s, setS] = useState<SortState>({ key: initialKey, dir: -1 });
  const toggle = (key: string) =>
    setS((p) => (p.key === key ? { key, dir: p.dir === 1 ? -1 : 1 } : { key, dir: -1 }));
  return { s, toggle };
};

const cmp = (a: unknown, b: unknown) => {
  if (typeof a === 'number' && typeof b === 'number') return a - b;
  return String(a ?? '').localeCompare(String(b ?? ''), 'ru');
};

const sortRows = <T extends Record<string, any>>(rows: T[], s: SortState) =>
  [...rows].sort((x, y) => cmp(x[s.key], y[s.key]) * s.dir);

export default function System({ a }: { a: AssistantState }) {
  const [tab, setTab] = useState<SysTab>('processes');

  return (
    <div style={{ position: 'absolute', inset: 0, display: 'flex', flexDirection: 'column', animation: 'ariaFadeFast .22s ease both' }}>
      {/* ─── Topbar Navigation ─── */}
      <div style={{
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'space-between',
        padding: '12px 28px',
        borderBottom: '1px solid rgba(255,255,255,0.06)',
        background: 'rgba(6, 8, 13, 0.55)',
        backdropFilter: 'blur(16px)',
        flexShrink: 0,
      }}>
        <div style={{
          display: 'flex',
          gap: 4,
          background: 'rgba(255,255,255,0.03)',
          padding: 3,
          borderRadius: 10,
          border: '1px solid rgba(255,255,255,0.05)',
        }}>
          {([
            ['processes', 'Процессы'],
            ['performance', 'Производительность'],
            ['tasks', 'Задачи ARIA'],
          ] as const).map(([k, label]) => {
            const active = tab === k;
            return (
              <button
                key={k}
                onClick={() => setTab(k)}
                style={{
                  padding: '5px 14px',
                  borderRadius: 7,
                  border: 'none',
                  cursor: 'pointer',
                  fontSize: 11,
                  fontFamily: C.mono,
                  letterSpacing: '.06em',
                  fontWeight: active ? 700 : 500,
                  background: active ? 'rgba(255,107,91,0.18)' : 'transparent',
                  color: active ? '#ff8a7a' : 'rgba(255,255,255,0.5)',
                  boxShadow: active ? '0 2px 10px rgba(255,107,91,0.15)' : 'none',
                  transition: 'all .18s',
                }}
              >
                {label}
              </button>
            );
          })}
        </div>

        {/* Live Heartbeat Indicator */}
        <div style={{
          display: 'flex',
          alignItems: 'center',
          gap: 8,
          padding: '4px 12px',
          borderRadius: 999,
          background: 'rgba(255,255,255,0.02)',
          border: '1px solid rgba(255,255,255,0.06)',
        }}>
          <span style={{
            width: 6,
            height: 6,
            borderRadius: '50%',
            background: '#39d98a',
            boxShadow: '0 0 8px #39d98a',
          }} />
          <span style={{
            fontFamily: C.mono,
            fontSize: 10.5,
            color: 'rgba(255,255,255,0.6)',
            letterSpacing: '.08em',
          }}>
            CPU {Math.round(a.metrics.cpu)}% · RAM {Math.round(a.metrics.ram)}%
          </span>
        </div>
      </div>

      {tab === 'processes' && <ProcessesTab a={a} />}
      {tab === 'performance' && <PerformanceTab a={a} />}
      {tab === 'tasks' && <TasksTab a={a} />}
    </div>
  );
}

/* ─── Processes Tab ─── */

function ProcessesTab({ a }: { a: AssistantState }) {
  const procSort = useSort('cpu');
  const [search, setSearch] = useState('');
  const procs = sortRows<ProcessInfo>(
    a.metrics.processes.filter((p) => !search || p.name.toLowerCase().includes(search.toLowerCase())),
    procSort.s,
  );

  return (
    <div style={{ flex: 1, display: 'flex', flexDirection: 'column', minHeight: 0 }}>
      {/* Controls */}
      <div style={{
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'space-between',
        padding: '12px 28px',
        flexShrink: 0,
      }}>
        <div style={{
          fontFamily: C.mono,
          fontSize: 11,
          color: 'rgba(255,255,255,0.45)',
          letterSpacing: '.1em',
          fontWeight: 600,
        }}>
          АКТИВНЫХ ПРОЦЕССОВ: {procs.length}
        </div>

        <div style={{
          display: 'flex',
          alignItems: 'center',
          gap: 8,
          background: 'rgba(255,255,255,0.03)',
          border: '1px solid rgba(255,255,255,0.08)',
          borderRadius: 8,
          padding: '6px 12px',
          width: 280,
        }}>
          <span style={{ color: 'rgba(255,255,255,0.3)', fontSize: 12 }}>⌕</span>
          <input
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder="Поиск по имени процесса…"
            style={{
              flex: 1,
              background: 'transparent',
              border: 'none',
              outline: 'none',
              fontFamily: C.mono,
              fontSize: 11.5,
              color: '#f8fafc',
            }}
          />
        </div>
      </div>

      {/* Table */}
      <div style={{
        flex: 1,
        overflowY: 'auto',
        margin: '0 28px 20px',
        background: 'rgba(6, 9, 15, 0.5)',
        border: '1px solid rgba(255,255,255,0.06)',
        borderRadius: 14,
        boxShadow: '0 8px 32px rgba(0,0,0,0.3)',
      }}>
        <div style={{
          display: 'grid',
          gridTemplateColumns: PROC_COLS,
          position: 'sticky',
          top: 0,
          zIndex: 1,
          background: 'rgba(10, 15, 24, 0.95)',
          backdropFilter: 'blur(16px)',
          borderBottom: '1px solid rgba(255,255,255,0.08)',
        }}>
          <SortHead label="ИМЯ ПРОЦЕССА" k="name" s={procSort.s} on={procSort.toggle} />
          <SortHead label="ЦП" k="cpu" s={procSort.s} on={procSort.toggle} right />
          <SortHead label="ПАМЯТЬ" k="ram" s={procSort.s} on={procSort.toggle} right />
          <SortHead label="GPU" k="gpu" s={procSort.s} on={procSort.toggle} right />
          <SortHead label="СТАТУС" k="pid" s={procSort.s} on={procSort.toggle} />
        </div>

        {procs.map((p) => (
          <TableRow key={p.pid} cols={PROC_COLS}>
            <Td style={{ display: 'flex', alignItems: 'center', gap: 10, padding: '7px 16px', minWidth: 0 }}>
              <ProcDot name={p.name} />
              <span style={{ fontSize: 13, color: '#f1f5f9', fontWeight: 500, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                {p.name}
              </span>
            </Td>
            <Td right><MiniBar value={p.cpu} suffix="%" /></Td>
            <Td right mono color="#cbd5e1">
              {p.ram ?? 0}% · {p.mem}
            </Td>
            <Td right mono color="#cbd5e1">
              {p.gpu ?? 0}%
            </Td>
            <Td mono color={p.cpu > 50 ? '#ffb454' : '#39d98a'}>
              {p.cpu > 50 ? 'высокая нагрузка' : 'работает'}
            </Td>
          </TableRow>
        ))}

        {procs.length === 0 && (
          <div style={{ padding: 48, textAlign: 'center', fontFamily: C.mono, fontSize: 12, color: C.textDim }}>
            Процессы не найдены
          </div>
        )}
      </div>
    </div>
  );
}

const PROC_COLS = 'minmax(220px,2.2fr) minmax(120px,1fr) minmax(130px,1fr) minmax(90px,.7fr) 150px';

function ProcDot({ name }: { name: string }) {
  const hue = [...name].reduce((h, ch) => (h * 31 + ch.charCodeAt(0)) % 360, 7);
  return (
    <div style={{
      width: 20,
      height: 20,
      borderRadius: 6,
      flexShrink: 0,
      background: `linear-gradient(135deg, hsl(${hue} 70% 50%), hsl(${(hue + 40) % 360} 75% 40%))`,
      boxShadow: 'inset 0 1px 2px rgba(255,255,255,0.25), 0 2px 6px rgba(0,0,0,0.3)',
    }} />
  );
}

/* ─── Performance Tab ─── */

function PerformanceTab({ a }: { a: AssistantState }) {
  const [sel, setSel] = useState<'cpu' | 'ram' | 'gpu'>('cpu');
  const m = a.metrics;
  const gpuAvail = m.gpu != null && m.gpu >= 0;

  const cards: { key: 'cpu' | 'ram' | 'gpu'; label: string; value: string; sub: string; data: number[]; color: string }[] = [
    { key: 'cpu', label: 'Процессор (CPU)', value: `${Math.round(m.cpu)}%`, sub: `${m.processes.length} активных процессов`, data: m.history.cpu, color: '#ff6b5b' },
    { key: 'ram', label: 'Оперативная память (RAM)', value: `${Math.round(m.ram)}%`, sub: `${m.ramUsed} / ${m.ramTotal}`, data: m.history.ram, color: '#38bdf8' },
    { key: 'gpu', label: 'Графика (GPU)', value: gpuAvail ? `${Math.round(m.gpu)}%` : 'н/д', sub: gpuAvail ? `VRAM ${m.vramUsed} / ${m.vramLimit}` : 'нет данных', data: m.history.gpu, color: '#a882ff' },
  ];

  return (
    <div style={{ flex: 1, display: 'flex', gap: 16, padding: '20px 28px', minHeight: 0 }}>
      {/* Sidebar Metric Selector Cards */}
      <div style={{ width: 270, flexShrink: 0, display: 'flex', flexDirection: 'column', gap: 12 }}>
        {cards.map((c) => (
          <button
            key={c.key}
            onClick={() => setSel(c.key)}
            style={{
              padding: '14px 16px',
              borderRadius: 14,
              border: sel === c.key ? `1px solid ${c.color}55` : '1px solid rgba(255,255,255,0.06)',
              background: sel === c.key ? `linear-gradient(135deg, ${c.color}18 0%, rgba(255,255,255,0.02) 100%)` : 'rgba(255,255,255,0.02)',
              cursor: 'pointer',
              textAlign: 'left',
              boxShadow: sel === c.key ? `0 4px 20px ${c.color}1a` : 'none',
              transition: 'all .18s',
            }}
          >
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'baseline', marginBottom: 8 }}>
              <span style={{ fontSize: 13, fontWeight: 600, color: '#f1f5f9' }}>{c.label}</span>
              <span style={{ fontFamily: C.mono, fontSize: 13, fontWeight: 700, color: c.color }}>{c.value}</span>
            </div>
            <Sparkline data={c.data} color={c.color} height={42} />
            <div style={{ marginTop: 8, fontFamily: C.mono, fontSize: 10, color: 'rgba(255,255,255,0.4)', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
              {c.sub}
            </div>
          </button>
        ))}
      </div>

      {/* Main Graph & Telemetry Details */}
      <div style={{
        flex: 1,
        minWidth: 0,
        display: 'flex',
        flexDirection: 'column',
        background: 'rgba(6, 9, 15, 0.5)',
        border: '1px solid rgba(255,255,255,0.06)',
        borderRadius: 14,
        overflowY: 'auto',
      }}>
        {(() => {
          const card = cards.find((c) => c.key === sel)!;
          return (
            <>
              <div style={{ padding: '20px 24px 8px' }}>
                <div style={{
                  fontFamily: "'Syne', sans-serif",
                  fontSize: 20,
                  fontWeight: 700,
                  color: '#f8fafc',
                  letterSpacing: '-0.02em',
                }}>
                  {card.label}
                </div>
                <div style={{ fontFamily: C.mono, fontSize: 11.5, color: 'rgba(126,139,160,0.85)', marginTop: 4 }}>
                  {sel === 'cpu' && `${Math.round(m.cpu)}% загрузка · ${m.processes.length} активных процессов`}
                  {sel === 'ram' && `${Math.round(m.ram)}% занято · ${m.ramUsed} / ${m.ramTotal}`}
                  {sel === 'gpu' && `${Math.round(m.gpu)}% загрузка · VRAM ${m.vramUsed} / ${m.vramLimit}`}
                </div>
              </div>

              <div style={{ padding: '8px 24px 0' }}>
                <BigChart data={card.data} color={card.color} />
              </div>

              {/* Bento Stats Grid */}
              <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(170px, 1fr))', gap: 12, padding: '20px 24px 24px' }}>
                {(sel === 'cpu'
                  ? [['Загрузка', `${Math.round(m.cpu)}%`], ['Процессов', String(m.processes.length)], ['Задач ARIA', String(a.metrics.tasks.length)], ['Время работы', m.uptime || '—']]
                  : sel === 'ram'
                  ? [['Используется', m.ramUsed], ['Доступно', m.ramFree], ['Всего', m.ramTotal], ['Загрузка', `${Math.round(m.ram)}%`]]
                  : [['Загрузка GPU', `${Math.round(m.gpu)}%`], ['VRAM Занято', m.vramUsed], ['VRAM Всего', m.vramLimit], ['Статус', gpuAvail ? 'активна' : 'нет данных']]
                ).map(([k, v]) => (
                  <div key={k} style={{
                    background: 'rgba(255,255,255,0.02)',
                    border: '1px solid rgba(255,255,255,0.06)',
                    borderRadius: 12,
                    padding: '12px 16px',
                  }}>
                    <div style={{ fontFamily: C.mono, fontSize: 10, letterSpacing: '.12em', color: 'rgba(255,255,255,0.4)', marginBottom: 6 }}>
                      {String(k).toUpperCase()}
                    </div>
                    <div style={{ fontFamily: C.mono, fontSize: 16, fontWeight: 700, color: '#f8fafc' }}>
                      {v}
                    </div>
                  </div>
                ))}
              </div>
            </>
          );
        })()}
      </div>
    </div>
  );
}

function Sparkline({ data, color, height }: { data: number[]; color: string; height: number }) {
  const pts = normalize(data);
  return (
    <svg width="100%" height={height} viewBox="0 0 100 100" preserveAspectRatio="none" style={{ display: 'block', background: 'rgba(0,0,0,0.3)', borderRadius: 6 }}>
      {[25, 50, 75].map((y) => (
        <line key={y} x1="0" y1={y} x2="100" y2={y} stroke="rgba(255,255,255,0.04)" strokeWidth="0.5" />
      ))}
      {pts.length > 1 && (
        <>
          <polygon points={`0,100 ${pts} 100,100`} fill={color} opacity="0.18" />
          <polyline points={pts} fill="none" stroke={color} strokeWidth="2" vectorEffect="non-scaling-stroke" />
        </>
      )}
    </svg>
  );
}

function BigChart({ data, color }: { data: number[]; color: string }) {
  const pts = normalize(data);
  const gradId = `grad-${color.replace('#', '')}`;
  return (
    <svg width="100%" height={210} viewBox="0 0 100 100" preserveAspectRatio="none" style={{ display: 'block', background: 'rgba(0,0,0,0.3)', borderRadius: 10, border: '1px solid rgba(255,255,255,0.06)' }}>
      {[20, 40, 60, 80].map((y) => (
        <line key={y} x1="0" y1={y} x2="100" y2={y} stroke="rgba(255,255,255,0.05)" strokeWidth="0.5" />
      ))}
      {pts.length > 1 && (
        <>
          <defs>
            <linearGradient id={gradId} x1="0" y1="0" x2="0" y2="1">
              <stop offset="0%" stopColor={color} stopOpacity="0.45" />
              <stop offset="100%" stopColor={color} stopOpacity="0.01" />
            </linearGradient>
          </defs>
          <polygon points={`0,100 ${pts} 100,100`} fill={`url(#${gradId})`} />
          <polyline points={pts} fill="none" stroke={color} strokeWidth="2" vectorEffect="non-scaling-stroke" />
        </>
      )}
    </svg>
  );
}

function normalize(data: number[]): string {
  if (data.length === 0) return '';
  const n = Math.max(HISTORY - 1, data.length - 1);
  return data
    .map((v, i) => {
      const x = (i / n) * 100;
      const y = 100 - Math.max(0, Math.min(100, v));
      return `${x.toFixed(2)},${y.toFixed(2)}`;
    })
    .join(' ');
}

/* ─── Tasks Tab ─── */

function TasksTab({ a }: { a: AssistantState }) {
  const taskSort = useSort('cpu');
  const tasks = sortRows<TaskLoad>(a.metrics.tasks, taskSort.s);

  return (
    <div style={{
      flex: 1,
      overflowY: 'auto',
      margin: '20px 28px',
      background: 'rgba(6, 9, 15, 0.5)',
      border: '1px solid rgba(255,255,255,0.06)',
      borderRadius: 14,
    }}>
      <div style={{
        display: 'grid',
        gridTemplateColumns: TASK_COLS,
        position: 'sticky',
        top: 0,
        zIndex: 1,
        background: 'rgba(10, 15, 24, 0.95)',
        backdropFilter: 'blur(16px)',
        borderBottom: '1px solid rgba(255,255,255,0.08)',
      }}>
        <SortHead label="ИМЯ ЗАДАЧИ" k="name" s={taskSort.s} on={taskSort.toggle} />
        <SortHead label="ЦП" k="cpu" s={taskSort.s} on={taskSort.toggle} right />
        <SortHead label="ПАМЯТЬ" k="ram" s={taskSort.s} on={taskSort.toggle} right />
        <SortHead label="GPU" k="gpu" s={taskSort.s} on={taskSort.toggle} right />
        <div style={{ padding: '11px 16px', fontFamily: C.mono, fontSize: 11, letterSpacing: '.08em', color: 'rgba(255,255,255,0.45)', fontWeight: 700 }}>
          СОСТОЯНИЕ
        </div>
      </div>
      {tasks.map((t) => {
        const st = taskStatus(t);
        return (
          <TableRow key={t.id} cols={TASK_COLS} style={{ opacity: t.enabled ? 1 : 0.55 }}>
            <Td style={{ display: 'flex', alignItems: 'center', gap: 10, padding: '8px 16px' }}>
              <Icon name={t.icon as any} color={t.enabled ? C.accent : C.textFaint} size={17} />
              <span style={{ fontSize: 13, color: t.enabled ? '#f1f5f9' : C.textDim, fontWeight: 500 }}>{t.name}</span>
            </Td>
            <Td right><MiniBar value={t.enabled ? t.cpu : null} suffix="%" /></Td>
            <Td right mono color={t.ram == null ? C.textDim : '#cbd5e1'}>
              {t.ram == null ? '—' : `${t.ram}% · ${t.ramMb}`}
            </Td>
            <Td right mono color={t.gpu == null ? C.textDim : '#cbd5e1'}>
              {t.gpu == null ? '—' : `${t.gpu}%`}
            </Td>
            <Td mono color={st.color}>{st.text}</Td>
          </TableRow>
        );
      })}
    </div>
  );
}

const TASK_COLS = 'minmax(220px,2fr) minmax(120px,1fr) minmax(130px,1fr) minmax(90px,.8fr) 150px';

function SortHead({ label, k, s, on, right }: { label: string; k: string; s: SortState; on: (k: string) => void; right?: boolean }) {
  return <TSortHead label={label} active={s.key === k} dir={s.key === k ? s.dir : 1} right={right} onClick={() => on(k)} />;
}
