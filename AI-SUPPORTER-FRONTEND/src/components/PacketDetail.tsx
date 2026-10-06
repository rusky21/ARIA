import { C, protoColor, R } from '../lib/theme.js';
import type { Packet } from '../lib/data.js';

export const hexView = (raw?: string): string => {
  if (!raw) return '';
  const bytes: number[] = [];
  for (let i = 0; i + 1 < raw.length; i += 2) bytes.push(parseInt(raw.slice(i, i + 2), 16));
  const lines: string[] = [];
  for (let off = 0; off < bytes.length; off += 16) {
    const chunk = bytes.slice(off, off + 16);
    const hexPart = chunk.map((b, j) => (j === 8 ? ' ' : '') + b.toString(16).padStart(2, '0')).join(' ');
    const ascii = chunk.map(b => (b >= 32 && b < 127 ? String.fromCharCode(b) : '.')).join('');
    lines.push(`${off.toString(16).padStart(4, '0')}  ${hexPart.padEnd(49)}  ${ascii}`);
  }
  return lines.join('\n');
};

export function buildLayers(p: Packet): { layer: string; color: string; fields: string }[] {
  return [
    {
      layer: `Frame #${p.no}`,
      color: '#94a3b8',
      fields: `Время: ${p.time}\nДлина: ${p.len} байт${p.ttl != null ? `\nTTL: ${p.ttl}` : ''}`,
    },
    {
      layer: 'IPv4 Network Layer',
      color: '#39d98a',
      fields: `Source: ${p.src}${p.sport ? `:${p.sport}` : ''}\nDestination: ${p.dst}${p.dport ? `:${p.dport}` : ''}\nПротокол: ${p.proto}${p.ttl != null ? `\nTTL: ${p.ttl}` : ''}`,
    },
    {
      layer: `${p.proto.toUpperCase()} Payload`,
      color: protoColor(p.proto),
      fields: p.info,
    },
  ];
}

export function PacketBreakdown({ p }: { p: Packet | null }) {
  if (!p) {
    return (
      <div style={{
        fontFamily: C.mono,
        fontSize: 12,
        color: 'rgba(126,139,160,0.7)',
        textAlign: 'center',
        padding: '30px 10px',
      }}>
        Выберите пакет для инспекции
      </div>
    );
  }

  const hex = hexView(p.raw);

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
      {buildLayers(p).map((d, i) => (
        <div
          key={i}
          style={{
            background: 'rgba(255,255,255,0.02)',
            border: '1px solid rgba(255,255,255,0.06)',
            borderRadius: 10,
            padding: '10px 14px',
            boxShadow: '0 2px 8px rgba(0,0,0,0.2)',
          }}
        >
          <div style={{
            fontFamily: C.mono,
            fontSize: 11,
            fontWeight: 700,
            color: d.color,
            marginBottom: 6,
            display: 'flex',
            alignItems: 'center',
            gap: 6,
          }}>
            <span style={{ fontSize: 9 }}>▶</span>
            <span>{d.layer}</span>
          </div>
          <div style={{
            fontFamily: C.mono,
            fontSize: 11,
            color: '#cbd5e1',
            lineHeight: 1.6,
            paddingLeft: 12,
            whiteSpace: 'pre-wrap',
          }}>
            {d.fields}
          </div>
        </div>
      ))}

      {hex && (
        <div style={{ marginTop: 4 }}>
          <div style={{
            fontFamily: C.mono,
            fontSize: 10,
            color: 'rgba(255,255,255,0.4)',
            letterSpacing: '.14em',
            marginBottom: 6,
            fontWeight: 600,
          }}>
            HEX & ASCII DUMP
          </div>
          <div style={{
            padding: '12px 14px',
            borderRadius: 10,
            background: 'rgba(4, 7, 12, 0.9)',
            border: '1px solid rgba(255,255,255,0.07)',
            fontFamily: C.mono,
            fontSize: 10.5,
            color: '#94a3b8',
            lineHeight: 1.7,
            letterSpacing: '.04em',
            whiteSpace: 'pre-wrap',
            overflowX: 'auto',
            boxShadow: 'inset 0 1px 4px rgba(0,0,0,0.6)',
          }}>
            {hex}
          </div>
        </div>
      )}
    </div>
  );
}
