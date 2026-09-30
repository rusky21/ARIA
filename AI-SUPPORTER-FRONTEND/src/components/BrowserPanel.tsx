import { useEffect, useState } from 'react';
import { C } from '../lib/theme.js';

interface Props {
  url: string | null;
  onClose?: () => void;
}

export default function BrowserPanel({ url, onClose }: Props) {
  const [addr, setAddr] = useState(url ?? '');
  const [src, setSrc] = useState(url);
  const [nonce, setNonce] = useState(0);

  useEffect(() => {
    setAddr(url ?? '');
    setSrc(url);
    if (url) setNonce((n) => n + 1);
  }, [url]);

  const go = (raw: string) => {
    let u = raw.trim();
    if (!u) return;
    if (!/^https?:\/\//i.test(u)) u = 'https://' + u;
    setAddr(u);
    setSrc(u);
    setNonce((n) => n + 1);
  };

  return (
    <div style={{
      width: 400, flexShrink: 0, display: 'flex', flexDirection: 'column', minWidth: 0,
      borderLeft: `1px solid ${C.line}`, background: '#0b101b',
    }}>
      <div style={{ display: 'flex', alignItems: 'center', gap: 8, padding: '10px 12px', borderBottom: `1px solid ${C.line}` }}>
        {onClose && <button onClick={onClose} title="Закрыть" style={tbBtn}>✕</button>}
        <button onClick={() => src && setNonce((n) => n + 1)} title="Обновить" style={tbBtn}>⟳</button>
        <div style={{ flex: 1, display: 'flex', alignItems: 'center', background: 'rgba(0,0,0,.4)', border: `1px solid ${C.line}`, borderRadius: 16, padding: '5px 12px' }}>
          <span style={{ color: C.green, fontSize: 11, marginRight: 7 }}>🔒</span>
          <input
            value={addr}
            onChange={(e) => setAddr(e.target.value)}
            onKeyDown={(e) => e.key === 'Enter' && go(addr)}
            style={{ flex: 1, background: 'transparent', border: 'none', outline: 'none', color: C.textMid, fontFamily: C.mono, fontSize: 12 }}
          />
        </div>
        {src && <a href={src} target="_blank" rel="noopener noreferrer" title="Открыть в новой вкладке" style={{ ...tbBtn, textDecoration: 'none', display: 'flex' }}>↗</a>}
      </div>
      {src ? (
        <iframe
          key={nonce}
          src={src}
          title="browser"
          style={{ flex: 1, width: '100%', border: 'none', background: '#fff' }}
          referrerPolicy="no-referrer"
        />
      ) : (
        <div style={{ flex: 1, display: 'flex', alignItems: 'center', justifyContent: 'center', color: C.textDim, fontFamily: C.mono, fontSize: 12 }}>
          Введите URL или откройте ссылку из чата
        </div>
      )}
      <div style={{ padding: '6px 12px', borderTop: `1px solid ${C.line}`, fontFamily: C.mono, fontSize: 10, color: C.textDim }}>
        Если сайт не отображается — он запрещает встраивание. Нажмите ↗ для открытия в новой вкладке.
      </div>
    </div>
  );
}

const tbBtn: React.CSSProperties = {
  width: 28, height: 28, borderRadius: 8, border: 'none', cursor: 'pointer',
  background: 'rgba(255,255,255,.05)', color: C.textMid, fontSize: 13,
  display: 'inline-flex', alignItems: 'center', justifyContent: 'center',
};
