import { C, protoColor, R } from '../lib/theme.js';
import type { Packet } from '../lib/data.js';

export const hexView = (raw?: string): string => {
  if (!raw) return '';
  const bytes: number[] = [];
  for (let i = 0; i + 1 < raw.length; i += 2) bytes.push(parseInt(raw.slice(i, i + 2), 16));
  const lines: string[] = [];
  for (let off = 0; off < bytes.length; off += 16) {
    const chunk = bytes.slice(off, off + 16)
    const hexPart = chunk.map((b, j) => (j === 8 ? ' ' : '') + b.toString(16).padStart(2, '0')).join(' ');
    const ascii = chunk.map(b => (b >= 32 && b < 127 ? String.fromCharCode(b) : '.')).join('');
    lines.push(`${off.toString(16).padStart(4, '0')}  ${hexPart.padEnd(49)}  ${ascii}`);
  }
  return lines.join('\n');
};

export function buildLayers(p: Packet): { layer: string; color: string; fields: string }[] {
  return [
    { layer: `Frame ${p.no}`, color: C.tableDim, fields: `Время захвата: ${p.time}\nДлина кадра: ${p.len} байт${p.ttl != null ? `\nTTL: ${p.ttl}` : ''}` },
    { layer: 'Internet Protocol v4', color: C.green, fields: `Source: ${p.src}${p.sport ? `:${p.sport}` : ''}\nDestination: ${p.dst}${p.dport ? `:${p.dport}` : ''}\nProtocol: ${p.proto}${p.ttl != null ? `\nTTL: ${p.ttl}` : ''}` },
    { layer: p.proto, color: protoColor(p.proto), fields: p.info },
  ];
}

export function PacketBreakdown({ p }: { p: Packet | null }) {
  if (!p) {
    return <div style={{ fontFamily: C.mono, fontSize: 12, color: C.textDim }}>Выберите пакет</div>;
  }
  const hex = hexView(p.raw);
  return (
    <>
      {buildLayers(p).map((d, i) => (
        <div key={i} style={{ marginBottom: 12 }}>
          <div style={{ fontFamily: C.mono, fontSize: 11, color: d.color, marginBottom: 5 }}>▸ {d.layer}</div>
          <div style={{ fontFamily: C.mono, fontSize: 11, color: C.tableDim, lineHeight: 1.7, paddingLeft: 14, whiteSpace: 'pre-wrap' }}>{d.fields}</div>
        </div>
      ))}
      {hex && (
        <div style={{ marginTop: 14, padding: 12, borderRadius: R.m, background: C.inputBg, fontFamily: C.mono, fontSize: 10.5, color: C.textDim, lineHeight: 1.7, letterSpacing: '.04em', whiteSpace: 'pre-wrap' }}>{hex}</div>
      )}
    </>
  );
}
