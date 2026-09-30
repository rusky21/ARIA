import { useEffect, useRef, useState } from 'react';
import { C, acc, w, FS } from '../lib/theme.js';
import { SortHead as TSortHead, TableRow, Td, MiniBar } from '../lib/Table.js';
import type { AssistantState } from '../hooks/useAssistant.js';
import type { ProcessInfo, TaskLoad } from '../lib/data.js';
import { Icon } from './Icon.jsx';

type SysTab = 'processes' | 'performance' | 'tasks';

const HISTORY = 60;

const taskStatus = (t: TaskLoad): { text: string; color: string } => {
  if (!t.enabled) return { text: 'выключена', color: C.textDim };
  if (t.inFlight) return { text: 'выполняется', color: C.green };
  if (t.lastError) return { text: 'ошибка', color: C.red };
  if (t.continuous) return { text: 'постоянно', color: C.green };
  return { text: t.nextRun ? `след. ${t.nextRun}` : 'активна', color: C.textMid };
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
  const m = a.metrics;

  return (
    <div style={{ position: 'absolute', inset: 0, display: 'flex', flexDirection: 'column', animation: 'ariaFadeFast .22s ease both' }}>
      <div style={{ display: 'flex', alignItems: 'center', gap: 2, padding: '0 18px', borderBottom: `1px solid ${C.line}`, background: 'rgba(8,12,20,.55)', flexShrink: 0 }}>
        <div style={{ fontFamily: C.ui, fontSize: 13, fontWeight: 600, color: C.textHi, marginRight: 22 }}>Диспетчер задач</div>
        {([['processes', 'Процессы'], ['performance', 'Производительность'], ['tasks', 'Задачи ARIA']] as const).map(([k, label]) => (
          <button key={k} onClick={() => setTab(k)} style={{
            padding: '10px 16px', border: 'none', cursor: 'pointer', background: 'transparent',
            fontSize: 12.5, fontFamily: C.ui, color: tab === k ? C.textHi : C.textDim,
            borderBottom: tab === k ? `2px solid ${C.accent}` : '2px solid transparent',
          }}>{label}</button>
        ))}
      </div>

      {tab === 'processes' && <ProcessesTab a={a} />}
      {tab === 'performance' && <PerformanceTab a={a} />}
      {tab === 'tasks' && <TasksTab a={a} />}
    </div>
  );
}

/* ───────────────────────── Процессы ───────────────────────── */

function ProcessesTab({ a }: { a: AssistantState }) {
  const procSort = useSort('cpu');
  const [search, setSearch] = useState('');
  const procs = sortRows<ProcessInfo>(
    a.metrics.processes.filter((p) => !search || p.name.toLowerCase().includes(search.toLowerCase())),
    procSort.s,
  );

  return (
    <div style={{ flex: 1, display: 'flex', flexDirection: 'column', minHeight: 0 }}>
      <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', padding: '10px 20px', flexShrink: 0 }}>
        <div style={{ fontFamily: C.mono, fontSize: 11, color: C.textDim, letterSpacing: '.08em' }}>
          ПРОЦЕССОВ: {procs.length}
        </div>
        <input
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          placeholder="Введите имя для поиска…"
          style={{ background: C.inputBg, border: `1px solid ${C.line}`, borderRadius: 6, padding: '6px 11px', fontFamily: C.mono, fontSize: 12, color: C.tableText, outline: 'none', width: 260 }}
        />
      </div>
      <div style={{ flex: 1, overflowY: 'auto', margin: '0 14px 14px', background: C.panel, border: `1px solid ${C.line}`, borderRadius: 8 }}>
        <div style={{ display: 'grid', gridTemplateColumns: PROC_COLS, position: 'sticky', top: 0, zIndex: 1, background: C.headerSolid, borderBottom: `1px solid ${C.line}` }}>
          <SortHead label="ИМЯ" k="name" s={procSort.s} on={procSort.toggle} />
          <SortHead label="ЦП" k="cpu" s={procSort.s} on={procSort.toggle} right />
          <SortHead label="ПАМЯТЬ" k="ram" s={procSort.s} on={procSort.toggle} right />
          <SortHead label="ГРАФИКА" k="gpu" s={procSort.s} on={procSort.toggle} right />
          <SortHead label="СОСТОЯНИЕ" k="pid" s={procSort.s} on={procSort.toggle} />
        </div>
        {procs.map((p) => (
          <TableRow key={p.pid} cols={PROC_COLS}>
            <Td style={{ display: 'flex', alignItems: 'center', gap: 9, padding: '6px 14px', minWidth: 0 }}>
              <ProcDot name={p.name} />
              <span style={{ fontSize: FS.l, color: C.tableText, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>{p.name}</span>
            </Td>
            <Td right><MiniBar value={p.cpu} suffix="%" /></Td>
            <Td right mono color={C.textMid}>{p.ram ?? 0}% · {p.mem}</Td>
            <Td right mono color={C.textMid}>{p.gpu ?? 0}%</Td>
            <Td mono color={p.cpu > 50 ? C.amber : C.green}>{p.cpu > 50 ? 'высокая нагрузка' : 'выполняется'}</Td>
          </TableRow>
        ))}
        {procs.length === 0 && (
          <div style={{ padding: 40, textAlign: 'center', fontFamily: C.mono, fontSize: 12, color: C.textDim }}>Нет процессов</div>
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
      width: 22, height: 22, borderRadius: 5, flexShrink: 0,
      background: `linear-gradient(135deg, hsl(${hue} 60% 45%), hsl(${(hue + 40) % 360} 65% 35%))`,
      boxShadow: `inset 0 0 0 1px rgba(255,255,255,.15)`,
    }} />
  );
}

/* ─────────────────────── Производительность ─────────────────────── */

interface Sample { cpu: number; ram: number; gpu: number }

function PerformanceTab({ a }: { a: AssistantState }) {
  const m = a.metrics;
  const gpuAvail = m.vramLimit !== '';
  const histRef = useRef<Sample[]>([]);
  const [, force] = useState(0);

  useEffect(() => {
    histRef.current.push({ cpu: m.cpu, ram: m.ram, gpu: m.gpu });
    if (histRef.current.length > HISTORY) histRef.current.shift();
    force((n) => n + 1);
  }, [m]);

  const hist = histRef.current;
  const [sel, setSel] = useState<'cpu' | 'ram' | 'gpu'>('cpu');

  const cards = [
    { key: 'cpu' as const, label: 'ЦП', value: `${Math.round(m.cpu)}%`, sub: m.uptime || '', data: hist.map((s) => s.cpu), color: C.accent },
    { key: 'ram' as const, label: 'Память', value: `${Math.round(m.ram)}%`, sub: `${m.ramUsed} из ${m.ramTotal}`, data: hist.map((s) => s.ram), color: C.cyan },
    ...(gpuAvail ? [{ key: 'gpu' as const, label: 'Графика', value: `${Math.round(m.gpu)}%`, sub: `VRAM ${m.vramUsed} / ${m.vramLimit}`, data: hist.map((s) => s.gpu), color: C.purple }] : []),
  ];

  return (
    <div style={{ flex: 1, display: 'flex', minHeight: 0, gap: 14, padding: 14 }}>
      <div style={{ width: 240, flexShrink: 0, display: 'flex', flexDirection: 'column', gap: 10, overflowY: 'auto' }}>
        {cards.map((c) => (
          <button key={c.key} onClick={() => setSel(c.key)} style={{
            textAlign: 'left', padding: '12px 14px', cursor: 'pointer',
            background: sel === c.key ? acc(0.08) : C.panel,
            border: sel === c.key ? `1px solid ${acc(0.4)}` : `1px solid ${C.line}`,
            borderRadius: 8,
          }}>
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'baseline', marginBottom: 6 }}>
              <span style={{ fontSize: 12.5, fontWeight: 600, color: C.textHi }}>{c.label}</span>
              <span style={{ fontFamily: C.mono, fontSize: 11, color: C.textMid }}>{c.value}</span>
            </div>
            <Sparkline data={c.data} color={c.color} height={38} />
            <div style={{ marginTop: 6, fontFamily: C.mono, fontSize: 10, color: C.textDim, whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>{c.sub}</div>
          </button>
        ))}
      </div>

      <div style={{ flex: 1, minWidth: 0, display: 'flex', flexDirection: 'column', background: C.panel, border: `1px solid ${C.line}`, borderRadius: 8, overflowY: 'auto' }}>
        {(() => {
          const card = cards.find((c) => c.key === sel)!;
          return (
            <>
              <div style={{ padding: '16px 20px 6px' }}>
                <div style={{ fontSize: 17, fontWeight: 600, color: C.textHi }}>{card.label}</div>
                <div style={{ fontFamily: C.mono, fontSize: 11, color: C.textDim, marginTop: 2 }}>
                  {sel === 'cpu' && `${Math.round(m.cpu)}% загрузка · ${m.processes.length} процессов`}
                  {sel === 'ram' && `${Math.round(m.ram)}% занято · ${m.ramUsed} / ${m.ramTotal}`}
                  {sel === 'gpu' && `${Math.round(m.gpu)}% загрузка · VRAM ${m.vramUsed} / ${m.vramLimit}`}
                </div>
              </div>
              <div style={{ padding: '6px 20px 0' }}>
                <BigChart data={card.data} color={card.color} />
              </div>
              <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(170px, 1fr))', gap: 12, padding: '14px 20px 18px' }}>
                {(sel === 'cpu'
                  ? [['Загрузка', `${Math.round(m.cpu)}%`], ['Процессов', String(m.processes.length)], ['Задач ARIA', String(a.metrics.tasks.length)], ['Время работы', m.uptime || '—']]
                  : sel === 'ram'
                    ? [['Используется', m.ramUsed], ['Доступно', m.ramFree], ['Всего', m.ramTotal], ['Загрузка', `${Math.round(m.ram)}%`]]
                    : [['Загрузка GPU', `${Math.round(m.gpu)}%`], ['VRAM занято', m.vramUsed], ['VRAM всего', m.vramLimit], ['Статус', gpuAvail ? 'активна' : 'нет данных']]
                ).map(([k, v]) => (
                  <div key={k} style={{ background: 'rgba(0,0,0,.25)', border: `1px solid ${C.lineSoft}`, borderRadius: 8, padding: '10px 14px' }}>
                    <div style={{ fontFamily: C.mono, fontSize: 10, letterSpacing: '.1em', color: C.textDim, marginBottom: 5 }}>{String(k).toUpperCase()}</div>
                    <div style={{ fontFamily: C.mono, fontSize: 15, color: C.textHi }}>{v}</div>
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
    <svg width="100%" height={height} viewBox="0 0 100 100" preserveAspectRatio="none" style={{ display: 'block', background: 'rgba(0,0,0,.3)', borderRadius: 4 }}>
      {[25, 50, 75].map((y) => <line key={y} x1="0" y1={y} x2="100" y2={y} stroke="rgba(255,255,255,.05)" strokeWidth="0.5" />)}
      {pts.length > 1 && (
        <>
          <polygon points={`0,100 ${pts} 100,100`} fill={color} opacity="0.16" />
          <polyline points={pts} fill="none" stroke={color} strokeWidth="1.8" vectorEffect="non-scaling-stroke" />
        </>
      )}
    </svg>
  );
}

function BigChart({ data, color }: { data: number[]; color: string }) {
  const pts = normalize(data);
  return (
    <svg width="100%" height={210} viewBox="0 0 100 100" preserveAspectRatio="none" style={{ display: 'block', background: 'rgba(0,0,0,.3)', borderRadius: 6, border: `1px solid ${C.lineSoft}` }}>
      {[20, 40, 60, 80].map((y) => <line key={y} x1="0" y1={y} x2="100" y2={y} stroke="rgba(255,255,255,.06)" strokeWidth="0.5" />)}
      {pts.length > 1 && (
        <>
          <defs>
            <linearGradient id={`grad-${color.replace('#', '')}`} x1="0" y1="0" x2="0" y2="1">
              <stop offset="0%" stopColor={color} stopOpacity="0.4" />
              <stop offset="100%" stopColor={color} stopOpacity="0.02" />
            </linearGradient>
          </defs>
          <polygon points={`0,100 ${pts} 100,100`} fill={`url(#grad-${color.replace('#', '')})`} />
          <polyline points={pts} fill="none" stroke={color} strokeWidth="2" vectorEffect="non-scaling-stroke" />
        </>
      )}
    </svg>
  );
}

function normalize(data: number[]): string {
  if (data.length === 0) return '';
  const n = Math.max(HISTORY - 1, data.length - 1);
  return data.map((v, i) => {
    const x = (i / n) * 100;
    const y = 100 - Math.max(0, Math.min(100, v));
    return `${x.toFixed(2)},${y.toFixed(2)}`;
  }).join(' ');
}

/* ───────────────────────── Задачи ARIA ───────────────────────── */

function TasksTab({ a }: { a: AssistantState }) {
  const taskSort = useSort('cpu');
  const tasks = sortRows<TaskLoad>(a.metrics.tasks, taskSort.s);

  return (
    <div style={{ flex: 1, overflowY: 'auto', margin: '14px', background: C.panel, border: `1px solid ${C.line}`, borderRadius: 8 }}>
      <div style={{ display: 'grid', gridTemplateColumns: TASK_COLS, position: 'sticky', top: 0, zIndex: 1, background: C.headerSolid, borderBottom: `1px solid ${C.line}` }}>
        <SortHead label="ЗАДАЧА" k="name" s={taskSort.s} on={taskSort.toggle} />
        <SortHead label="ЦП" k="cpu" s={taskSort.s} on={taskSort.toggle} right />
        <SortHead label="ПАМЯТЬ" k="ram" s={taskSort.s} on={taskSort.toggle} right />
        <SortHead label="ГРАФИКА" k="gpu" s={taskSort.s} on={taskSort.toggle} right />
        <div style={{ padding: '11px 16px', fontFamily: C.mono, fontSize: 11, letterSpacing: '.08em', color: C.textDim }}>СОСТОЯНИЕ</div>
      </div>
      {tasks.map((t) => {
        const st = taskStatus(t);
        return (
          <TableRow key={t.id} cols={TASK_COLS} style={{ opacity: t.enabled ? 1 : 0.55 }}>
            <Td style={{ display: 'flex', alignItems: 'center', gap: 10, padding: '8px 14px' }}>
              <Icon name={t.icon as any} color={t.enabled ? C.accent : C.textFaint} size={17} />
              <span style={{ fontSize: FS.l, color: t.enabled ? C.tableText : C.textDim }}>{t.name}</span>
            </Td>
            <Td right><MiniBar value={t.enabled ? t.cpu : null} suffix="%" /></Td>
            <Td right mono color={t.ram == null ? C.textDim : C.textMid}>
              {t.ram == null ? '—' : `${t.ram}% · ${t.ramMb}`}
            </Td>
            <Td right mono color={t.gpu == null ? C.textDim : C.textMid}>
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
