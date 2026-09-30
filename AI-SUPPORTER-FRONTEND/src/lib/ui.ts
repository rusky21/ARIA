import type { CSSProperties } from 'react';
import { C, R, FS, acc } from './theme.js';

const TR = 'all .18s cubic-bezier(.4,0,.2,1)';

export const ui = {
  s: {
    btn: {
      padding: '8px 16px', borderRadius: R.s, border: `1px solid ${C.accentLine}`,
      background: acc(0.1), color: C.accent, fontFamily: C.ui, fontSize: FS.l,
      fontWeight: 600, cursor: 'pointer', letterSpacing: '.06em',
      transition: TR,
    } as CSSProperties,

    btnActive: {
      padding: '8px 18px', borderRadius: R.s, border: `1px solid ${acc(0.4)}`,
      background: acc(0.18), color: C.textHi, fontFamily: C.ui, fontSize: FS.l,
      fontWeight: 600, cursor: 'pointer', letterSpacing: '.06em',
      boxShadow: `0 0 18px ${acc(0.2)}`, transition: TR,
    } as CSSProperties,

    input: {
      padding: '8px 12px', borderRadius: R.s, border: `1px solid ${C.line}`,
      background: 'rgba(15,22,35,.6)', color: C.textMid, fontFamily: C.mono, fontSize: FS.l,
      outline: 'none', transition: TR,
    } as CSSProperties,
  },
} as const;