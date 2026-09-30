import { describe, expect, it } from 'vitest';
import { C, levelColor, protoColor } from './theme.js';

describe('protoColor', () => {
  it('возвращает цвет из палитры для известных протоколов', () => {
    expect(protoColor('TCP')).toBe(C.cyan);
    expect(protoColor('TLSv1.3')).toBe(C.accent);
    expect(protoColor('TLSv1.2')).toBe(C.accent);
    expect(protoColor('HTTP')).toBe(C.green);
    expect(protoColor('DNS')).toBe(C.amber);
    expect(protoColor('QUIC')).toBe(C.blue);
    expect(protoColor('UDP')).toBe(C.purple);
    expect(protoColor('ARP')).toBe('#7a8699');
    expect(protoColor('ICMP')).toBe(C.pink);
  });

  it('возвращает нейтральный цвет для неизвестного протокола', () => {
    expect(protoColor('SMB')).toBe('#8a97ab');
    expect(protoColor('')).toBe('#8a97ab');
  });
});

describe('levelColor', () => {
  it('возвращает цвет из палитры для известных уровней', () => {
    expect(levelColor('INFO')).toBe(C.blue);
    expect(levelColor('OK')).toBe(C.green);
    expect(levelColor('WARN')).toBe(C.amber);
    expect(levelColor('ERROR')).toBe(C.red);
    expect(levelColor('SEC')).toBe(C.accent);
    expect(levelColor('DEBUG')).toBe('#7a8699');
  });

  it('возвращает нейтральный цвет для неизвестного уровня', () => {
    expect(levelColor('TRACE')).toBe('#8a97ab');
    expect(levelColor('')).toBe('#8a97ab');
  });
});
