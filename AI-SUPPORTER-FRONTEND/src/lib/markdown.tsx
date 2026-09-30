import type { ReactNode } from 'react';

const URL_RE = /(https?:\/\/[^\s<>"')\]]+)/g;
const BOLD_RE = /\*\*(.+?)\*\*/g;

function renderInline(text: string, onLink?: (url: string) => void, keyBase = ''): ReactNode[] {
  const out: ReactNode[] = [];
  let last = 0;
  let m: RegExpExecArray | null;
  URL_RE.lastIndex = 0;
  while ((m = URL_RE.exec(text)) !== null) {
    if (m.index > last) out.push(...renderStyled(text.slice(last, m.index), onLink, `${keyBase}t${out.length}`));
    const url = m[1];
    out.push(
      <a
        key={`${keyBase}a${m.index}`}
        href={url}
        onClick={(e) => {
          e.preventDefault();
          onLink ? onLink(url) : window.open(url, '_blank', 'noopener');
        }}
        style={{ color: '#3fd6ff', textDecoration: 'underline', textUnderlineOffset: 2, wordBreak: 'break-all', cursor: 'pointer' }}
      >
        {url}
      </a>,
    );
    last = m.index + url.length;
  }
  if (last < text.length) out.push(...renderStyled(text.slice(last), onLink, `${keyBase}e${out.length}`));
  return out;
}

function renderStyled(text: string, onLink?: (url: string) => void, keyBase = ''): ReactNode[] {
  const out: ReactNode[] = [];
  let rest = text;
  let k = 0;
  while (rest) {
    const code = rest.indexOf('`');
    if (code === -1) {
      out.push(...renderBold(rest, keyBase + k));
      break;
    }
    if (code > 0) out.push(...renderBold(rest.slice(0, code), keyBase + k));
    const end = rest.indexOf('`', code + 1);
    if (end === -1) {
      out.push(...renderBold(rest.slice(code), keyBase + k));
      break;
    }
    out.push(
      <code key={`${keyBase}c${k}`} style={{ fontFamily: "'JetBrains Mono', monospace", fontSize: '.92em', background: 'rgba(255,255,255,.09)', borderRadius: 5, padding: '1px 6px' }}>
        {rest.slice(code + 1, end)}
      </code>,
    );
    rest = rest.slice(end + 1);
    k++;
  }
  return out;
}

function renderBold(text: string, keyBase: string): ReactNode[] {
  const out: ReactNode[] = [];
  let last = 0;
  let m: RegExpExecArray | null;
  BOLD_RE.lastIndex = 0;
  while ((m = BOLD_RE.exec(text)) !== null) {
    if (m.index > last) out.push(text.slice(last, m.index));
    out.push(<strong key={`${keyBase}b${m.index}`}>{m[1]}</strong>);
    last = m.index + m[0].length;
  }
  if (last < text.length) out.push(text.slice(last));
  return out;
}

export function renderMarkdown(text: string, onLink?: (url: string) => void): ReactNode {
  if (!text) return null;
  const blocks: ReactNode[] = [];
  const parts = text.split(/```(\w*)\n?/);
  for (let i = 0; i < parts.length; i += 2) {
    const prose = parts[i];
    const code = parts[i + 2] !== undefined ? parts[i + 1] : undefined;
    if (prose) blocks.push(...proseToNodes(prose, onLink, `p${i}`));
    if (i + 2 < parts.length && code !== undefined) {
      const body = parts[i + 2] ?? '';
      blocks.push(
        <pre key={`pre${i}`} style={{ background: '#0d1117', border: '1px solid rgba(255,255,255,.08)', borderRadius: 10, padding: '12px 14px', overflowX: 'auto', margin: '8px 0' }}>
          {code ? <div style={{ fontFamily: 'monospace', fontSize: 10, color: '#6e7681', marginBottom: 6 }}>{code}</div> : null}
          <code style={{ fontFamily: "'JetBrains Mono', monospace", fontSize: 12, lineHeight: 1.6, color: '#dbe3ee', whiteSpace: 'pre' }}>{body.replace(/\n$/, '')}</code>
        </pre>,
      );
    }
  }
  return <>{blocks}</>;
}

function proseToNodes(prose: string, onLink?: (url: string) => void, keyBase = ''): ReactNode[] {
  const nodes: ReactNode[] = [];
  const lines = prose.split('\n');
  let para: string[] = [];

  const flush = (key: string) => {
    if (para.length === 0) return;
    nodes.push(
      <p key={key} style={{ margin: '0 0 10px', lineHeight: 1.65 }}>
        {renderInline(para.join('\n'), onLink, key)}
      </p>,
    );
    para = [];
  };

  lines.forEach((line, li) => {
    const trimmed = line.trim();
    if (/^#{1,4}\s/.test(trimmed)) {
      flush(`${keyBase}f${li}`);
      nodes.push(
        <div key={`${keyBase}h${li}`} style={{ fontWeight: 700, fontSize: 15, margin: '14px 0 6px', color: '#e8edf5' }}>
          {renderInline(trimmed.replace(/^#{1,4}\s/, ''), onLink, `${keyBase}hi${li}`)}
        </div>,
      );
      return;
    }
    const li_ = trimmed.match(/^[-*•]\s+(.*)$/);
    if (li_) {
      flush(`${keyBase}f${li}`);
      nodes.push(
        <div key={`${keyBase}l${li}`} style={{ display: 'flex', gap: 8, margin: '0 0 5px', paddingLeft: 6, lineHeight: 1.6 }}>
          <span style={{ color: '#ff6b5b' }}>•</span>
          <span>{renderInline(li_[1], onLink, `${keyBase}li${li}`)}</span>
        </div>,
      );
      return;
    }
    const oi = trimmed.match(/^(\d+)[.)]\s+(.*)$/);
    if (oi) {
      flush(`${keyBase}f${li}`);
      nodes.push(
        <div key={`${keyBase}o${li}`} style={{ display: 'flex', gap: 8, margin: '0 0 5px', paddingLeft: 6, lineHeight: 1.6 }}>
          <span style={{ color: '#ff6b5b', fontFamily: 'monospace', fontSize: 13 }}>{oi[1]}.</span>
          <span>{renderInline(oi[2], onLink, `${keyBase}oi${li}`)}</span>
        </div>,
      );
      return;
    }
    if (trimmed === '') flush(`${keyBase}f${li}`);
    else para.push(line);
  });
  flush(`${keyBase}fx`);
  return nodes;
}
