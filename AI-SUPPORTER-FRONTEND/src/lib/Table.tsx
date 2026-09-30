import type { CSSProperties, ReactNode } from 'react';
import { C, R, FS, w, acc } from './theme.js';

const TR = 'all .18s cubic-bezier(.4,0,.2,1)';

export function TablePanel({ children, style }: { children: ReactNode; style?: CSSProperties }) {
  return (
    <div style={{
      background: C.panel, border: `1px solid ${C.line}`, borderRadius: R.lg,
      overflowY: 'auto', ...style,
    }}>{children}</div>
  );
}

export function TableHead({ cols, sortKey, sortDir, onSort }: {
  cols: { key: string; label: string; right?: boolean }[];
  sortKey: string;
  sortDir: 1 | -1;
  onSort: (key: string) => void;
}) {
  return (
    <div style={{ display: 'grid', gridTemplateColumns: cols.map(c => c.key).join(' '), position: 'sticky', top: 0, zIndex: 1, background: C.headerSolid, borderBottom: `1px solid ${C.line}` }}>
      {cols.map(({ key, label, right }) => (
        <SortHead key={key} label={label} active={sortKey === key} dir={sortKey === key ? sortDir : 1} right={right} onClick={() => onSort(key)} />
      ))}
    </div>
  );
}

export function SortHead({ label, active, dir, right, onClick }: {
  label: string; active?: boolean; dir: 1 | -1; right?: boolean; onClick?: () => void;
}) {
  return (
    <div onClick={onClick} style={{
      padding: '10px 14px', cursor: 'pointer', userSelect: 'none',
      display: 'flex', alignItems: 'center', gap: 4,
      justifyContent: right ? 'flex-end' : 'flex-start',
      color: active ? C.tableText : C.textDim, transition: TR,
      fontFamily: C.mono, fontSize: FS.s, letterSpacing: '.08em',
    }}>
      {label}
      <span style={{ fontSize: 9, color: active ? C.accent : 'transparent' }}>{dir === -1 ? '▼' : '▲'}</span>
    </div>
  );
}

export function TableRow({ children, cols, active, onClick, onContextMenu, style }: {
  children: ReactNode; cols: string; active?: boolean; onClick?: () => void; onContextMenu?: (e: React.MouseEvent) => void; style?: CSSProperties;
}) {
  return (
    <div
      onClick={onClick}
      onContextMenu={onContextMenu}
      style={{
        display: 'grid', gridTemplateColumns: cols, alignItems: 'center',
        borderBottom: `1px solid ${w(0.04)}`, transition: TR, cursor: 'default',
        background: active ? acc(0.06) : 'transparent',
        ...style,
      }}
      onMouseEnter={(e) => { if (!active) e.currentTarget.style.background = C.rowHover; }}
      onMouseLeave={(e) => { if (!active) e.currentTarget.style.background = 'transparent'; }}
    >{children}</div>
  );
}

export function Td({ children, right, mono, color, style }: {
  children?: ReactNode; right?: boolean; mono?: boolean; color?: string; style?: CSSProperties;
}) {
  return (
    <div style={{
      padding: '7px 14px', textAlign: right ? 'right' : 'left',
      fontFamily: mono ? C.mono : undefined, fontSize: mono ? FS.s : FS.l,
      color: color || C.tableText, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap',
      ...style,
    }}>{children}</div>
  );
}

export function MiniBar({ value, suffix }: { value: number | null; suffix: string }) {
  const v = Math.max(0, Math.min(100, value ?? 0));
  const color = v > 60 ? C.red : v > 35 ? C.amber : C.accent;
  return (
    <div style={{ display: 'inline-flex', alignItems: 'center', gap: 8 }}>
      <div style={{ width: 70, height: 4, background: w(0.07), borderRadius: 2, overflow: 'hidden' }}>
        <div style={{ height: '100%', width: `${value == null ? 0 : v}%`, background: color, borderRadius: 2, transition: 'width .6s' }} />
      </div>
      <span style={{ fontFamily: C.mono, fontSize: FS.s, color: value == null ? C.textDim : C.textMid, width: 42, textAlign: 'right' }}>
        {value == null ? '—' : `${value}${suffix}`}
      </span>
    </div>
  );
}

export function Badge({ color, bg, children }: { color: string; bg?: string; children: ReactNode }) {
  return (
    <span style={{
      display: 'inline-flex', alignItems: 'center', gap: 5,
      padding: '2px 8px', borderRadius: R.xs,
      fontFamily: C.mono, fontSize: FS.x, letterSpacing: '.1em',
      background: bg || w(0.06), color,
    }}>{children}</span>
  );
}

export function Dot({ color, size = 7 }: { color: string; size?: number }) {
  return (
    <span style={{
      width: size, height: size, borderRadius: '50%', flexShrink: 0,
      background: color, boxShadow: `0 0 8px ${color}`,
    }} />
  );
}

export function GlowDot({ color, size = 7, pulse = 'ariaDot 2s infinite' }: { color: string; size?: number; pulse?: string }) {
  return (
    <span style={{ position: 'relative', width: size, height: size, flexShrink: 0 }}>
      <span style={{ position: 'absolute', inset: 0, borderRadius: '50%', background: color }} />
      <span style={{ position: 'absolute', inset: 0, borderRadius: '50%', background: color, animation: pulse }} />
    </span>
  );
}

export function StatCard({ label, value, color, sub, onClick }: {
  label: string; value: string; color: string; sub?: string; onClick?: () => void;
}) {
  return (
    <div onClick={onClick} style={{
      background: 'rgba(10,15,26,.55)', border: `1px solid ${C.line}`, borderRadius: R.lg,
      padding: '14px 16px', cursor: onClick ? 'pointer' : 'default', transition: TR,
      display: 'flex', flexDirection: 'column', gap: 6,
    }}
      onMouseEnter={(e) => { if (onClick) e.currentTarget.style.borderColor = acc(0.25); }}
      onMouseLeave={(e) => { if (onClick) e.currentTarget.style.borderColor = C.line; }}
    >
      <span style={{ fontFamily: C.mono, fontSize: FS.x, letterSpacing: '.12em', color: C.textDim }}>{label}</span>
      <span style={{ fontFamily: C.mono, fontSize: FS.xxl, fontWeight: 600, color }}>{value}</span>
      {sub && <span style={{ fontFamily: C.mono, fontSize: FS.s, color: C.textDim, whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>{sub}</span>}
    </div>
  );
}

