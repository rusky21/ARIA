export const w = (a: number): string => `rgba(255,255,255,${a})`;
export const acc = (a: number): string => `rgba(255,107,91,${a})`;
export const cyanA = (a: number): string => `rgba(63,214,255,${a})`;

export const R = {
  xs: 6, s: 8, m: 10, lg: 12, xl: 16, xxl: 20, pill: 9999,
} as const;

export const FS = { x: 10, s: 11, m: 12, l: 13, xl: 15, xxl: 20 } as const;

export const C = {
  bg0: '#06080d',
  bgVoid: '#030508',
  bgCrust: '#080c14',
  panel: 'rgba(13,19,30,0.65)',
  panelSolid: '#0a0f1a',
  panelAlt: 'rgba(12,18,28,.85)',
  panelHi: 'rgba(18,26,40,.9)',
  headerSolid: '#0c121e',
  inputBg: 'rgba(0,0,0,.35)',
  glassCard: 'rgba(14,20,32,0.6)',
  glassBorder: 'rgba(255,255,255,0.08)',
  glassBorderHover: 'rgba(255,255,255,0.22)',
  line: 'rgba(255,255,255,0.08)',
  lineSoft: 'rgba(255,255,255,0.04)',
  rowHover: 'rgba(255,255,255,.035)',

  accent: '#ff6b5b',
  accentHot: '#ff3d68',
  accentLine: 'rgba(255,107,91,0.28)',
  onAccent: '#1a0a07',
  accentText: '#ffd9d2',
  gradientFire: 'linear-gradient(135deg, #ff6b5b 0%, #ff3d68 100%)',
  gradientPill: 'linear-gradient(180deg, rgba(255,255,255,0.07) 0%, rgba(255,255,255,0.02) 100%)',

  cyan: '#3fd6ff',
  green: '#39d98a',
  amber: '#ffb454',
  red: '#ff5470',
  purple: '#b46bff',
  blue: '#5b8cff',
  pink: '#ff7ac2',

  textHi: '#f2f5fa',
  textMid: '#8e9eaf',
  textDim: '#586477',
  textFaint: '#434e5e',
  tableText: '#dbe3ee',
  tableDim: '#8fa3b8',

  display: "'Syne', sans-serif",
  mono: "'JetBrains Mono', monospace",
  ui: "'Plus Jakarta Sans', 'Chakra Petch', sans-serif",
} as const;

export const protoColor = (p: string): string =>
  ({
    TCP: C.cyan,
    'TLSv1.3': C.accent,
    'TLSv1.2': C.accent,
    HTTP: C.green,
    DNS: C.amber,
    QUIC: C.blue,
    UDP: C.purple,
    ARP: '#7a8699',
    ICMP: C.pink,
  })[p] || '#8a97ab';

export const levelColor = (l: string): string =>
  ({
    INFO: C.blue,
    OK: C.green,
    WARN: C.amber,
    ERROR: C.red,
    SEC: C.accent,
    DEBUG: '#7a8699',
  })[l] || '#8a97ab';