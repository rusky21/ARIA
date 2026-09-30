export const w = (a: number): string => `rgba(255,255,255,${a})`;
export const acc = (a: number): string => `rgba(255,107,91,${a})`;
export const cyanA = (a: number): string => `rgba(63,214,255,${a})`;

export const R = {
  xs: 6, s: 8, m: 10, lg: 12, xl: 16, xxl: 20, pill: 999,
} as const;

export const FS = { x: 10, s: 11, m: 12, l: 13, xl: 15, xxl: 20 } as const;

export const C = {
  bg0: '#05080f',
  bgCrust: '#070b12',
  panel: 'rgba(15,22,35,0.6)',
  panelSolid: '#0a0f1a',
  panelAlt: 'rgba(12,18,28,.9)',
  panelHi: 'rgba(18,26,40,.9)',
  headerSolid: '#101724',
  inputBg: 'rgba(0,0,0,.3)',
  line: 'rgba(255,255,255,0.07)',
  lineSoft: 'rgba(255,255,255,0.04)',
  rowHover: 'rgba(255,255,255,.035)',

  accent: '#ff6b5b',
  accentLine: 'rgba(255,107,91,0.25)',
  onAccent: '#1a0a07',
  accentText: '#ffd9d2',

  cyan: '#3fd6ff',
  green: '#39d98a',
  amber: '#ffb454',
  red: '#ff5470',
  purple: '#b46bff',
  blue: '#5b8cff',
  pink: '#ff7ac2',

  textHi: '#e8edf5',
  textMid: '#9fb0c4',
  textDim: '#5b6678',
  textFaint: '#586477',
  tableText: '#dbe3ee',
  tableDim: '#8fa3b8',

  mono: "'JetBrains Mono', monospace",
  ui: "'Chakra Petch', sans-serif",
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