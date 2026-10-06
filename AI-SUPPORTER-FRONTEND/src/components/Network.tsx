import { useState, useEffect, useRef, useMemo } from 'react';
import { C, protoColor, acc, cyanA, w } from '../lib/theme.js';
import { Icon } from './Icon.js';
import type { Packet, InspectorTarget } from '../lib/data.js';
import type { AssistantState } from '../hooks/useAssistant.js';
import ReportInstant from './ReportInstant.js';
import ReportPeriod from './ReportPeriod.js';
import { PacketBreakdown } from './PacketDetail.js';

type SubTab = 'capture' | 'patterns' | 'instant' | 'daily' | 'weekly' | 'test';

const COLS = '58px 96px 1.3fr 1.3fr 82px 68px 2fr';
const ROW_H = 32;
const OVERSCAN = 20;

interface Pattern {
  id: string;
  description: string;
  related_ips: string[];
  suggested_action: string;
  detected_at: string;
}

interface Device {
  ip: string;
  name: string;
}

interface TestEntry {
  id: string;
  time: string;
  text: string;
  type: 'sent' | 'received' | 'info' | 'error' | 'success';
}

const TEST_LABELS: Record<string, string> = {
  ping: 'Ping',
  http: 'HTTP',
  osc: 'OSC',
  artnet: 'Art-Net',
};

const timeToSec = (t: string): number => {
  const [h, m, s] = t.split(':').map(Number);
  return (h || 0) * 3600 + (m || 0) * 60 + (s || 0);
};

const SORT_ACCESSORS: Record<string, (pk: Packet) => number | string> = {
  no: (pk) => pk.no,
  time: (pk) => timeToSec(pk.time),
  src: (pk) => pk.src,
  dst: (pk) => pk.dst,
  proto: (pk) => pk.proto,
  len: (pk) => pk.len,
  info: (pk) => pk.info,
};

export default function Network({ a, ws }: { a: AssistantState; ws?: WebSocket | null }) {
  const [sub, setSub] = useState<SubTab>('capture');
  const [patterns, setPatterns] = useState<Pattern[]>([]);
  const [patternsLoading, setPatternsLoading] = useState(false);
  const [devices, setDevices] = useState<Device[]>([]);
  const [scanning, setScanning] = useState(false);
  const [busyIps, setBusyIps] = useState<Set<string>>(new Set());
  const [manualIp, setManualIp] = useState('');
  const [testLog, setTestLog] = useState<TestEntry[]>([]);
  const [testInput, setTestInput] = useState('');
  const testLogRef = useRef<HTMLDivElement>(null);
  const [filter, setFilter] = useState('');
  const [trafficFiltered, setTrafficFiltered] = useState<Packet[]>([]);
  const [sortKey, setSortKey] = useState('');
  const [sortDir, setSortDir] = useState<1 | -1>(1);
  const [inspector, setInspector] = useState<InspectorTarget | null>(null);

  const scrollRef = useRef<HTMLDivElement>(null);
  const stickRef = useRef(true);
  const [viewH, setViewH] = useState(0);
  const [scrollTop, setScrollTop] = useState(0);

  const display = useMemo(() => {
    const rows = filter.trim() ? (trafficFiltered.length ? trafficFiltered : a.trafficFiltered) : a.packets;
    if (!sortKey) return rows;
    const accessor = SORT_ACCESSORS[sortKey];
    const dir = sortDir;
    return [...rows].sort((x, y) => {
      const ax = accessor(x);
      const by = accessor(y);
      if (typeof ax === 'number' && typeof by === 'number') return (ax - by) * dir;
      return String(ax).localeCompare(String(by)) * dir;
    });
  }, [a.trafficFiltered, a.packets, trafficFiltered, filter, sortKey, sortDir]);

  const stats = useMemo(() => {
    const counts = new Map<string, number>();
    let bytes = 0;
    for (const pk of display) {
      counts.set(pk.proto, (counts.get(pk.proto) ?? 0) + 1);
      bytes += pk.len;
    }
    return { entries: [...counts.entries()].sort((x, y) => y[1] - x[1]), bytes, total: display.length };
  }, [display]);

  const selIdx = a.selPacket >= 0 ? a.selPacket : 0;
  const p = display[selIdx] ?? display[display.length - 1] ?? null;

  const winStart = Math.max(0, Math.floor(scrollTop / ROW_H) - OVERSCAN);
  const winEnd = Math.min(display.length, Math.ceil((scrollTop + viewH) / ROW_H) + OVERSCAN);
  const slice = display.slice(winStart, winEnd);

  const toggleSort = (key: string) => {
    if (sortKey === key) setSortDir((d) => (d === 1 ? -1 : 1));
    else {
      setSortKey(key);
      setSortDir(1);
    }
  };

  const handleExport = (format: 'csv' | 'pcap') => {
    addLog(`Экспорт ${format.toUpperCase()}: формирую...`, 'info');
    a.trafficExport(format);
    addLog(`Экспорт ${format.toUpperCase()}: завершён`, 'success');
  };

  const filterTimer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const activeFilter = filter.trim();
  useEffect(() => {
    if (filterTimer.current) clearTimeout(filterTimer.current);
    if (!activeFilter) {
      setTrafficFiltered([]);
      return;
    }
    filterTimer.current = setTimeout(() => {
      a.trafficFilter(activeFilter);
      stickRef.current = true;
    }, 300);
    return () => {
      if (filterTimer.current) clearTimeout(filterTimer.current);
    };
  }, [activeFilter]);

  useEffect(() => {
    if (testLogRef.current) testLogRef.current.scrollTop = testLogRef.current.scrollHeight;
  }, [testLog]);

  useEffect(() => {
    a.refreshInterfaces();
  }, [a.refreshInterfaces]);

  useEffect(() => {
    const el = scrollRef.current;
    if (!el) return;
    const ro = new ResizeObserver(() => setViewH(el.clientHeight));
    ro.observe(el);
    setViewH(el.clientHeight);
    return () => ro.disconnect();
  }, [sub]);

  useEffect(() => {
    if (sub !== 'capture') return;
    stickRef.current = true;
    setScrollTop(0);
    const el = scrollRef.current;
    if (el && !sortKey) el.scrollTop = el.scrollHeight;
  }, [sub]);

  useEffect(() => {
    const el = scrollRef.current;
    if (!el || !stickRef.current || sortKey) return;
    el.scrollTop = el.scrollHeight;
  }, [display.length, sortKey]);

  useEffect(() => {
    if (!ws) return;
    const handler = (e: MessageEvent) => {
      try {
        const parsed = JSON.parse(e.data);
        switch (parsed.type) {
          case 'traffic:patterns':
            if (parsed.patterns) setPatterns(parsed.patterns);
            setPatternsLoading(false);
            break;
          case 'traffic:patterns_cleared':
            setPatterns([]);
            setPatternsLoading(false);
            break;
          case 'artnet:poll-result':
            if (parsed.devices) setDevices(parsed.devices);
            break;
          case 'traffic:interface:set':
            if (parsed.ok) addLog(`Интерфейс захвата: ${parsed.ip}`, 'success');
            else addLog(`Ошибка переключения интерфейса: ${parsed.error || parsed.ip}`, 'error');
            break;
          case 'device:test-result':
            if (parsed.ip && parsed.results) {
              for (const r of parsed.results) {
                addLog(`  ${r.success ? '✓' : '✗'} ${TEST_LABELS[r.test] || r.test} ${formatDetails(r)}`, r.success ? 'success' : 'error');
              }
              setBusyIps((prev) => {
                const n = new Set(prev);
                n.delete(parsed.ip);
                return n;
              });
            }
            break;
        }
      } catch {}
    };
    ws.addEventListener('message', handler)
    return () => ws.removeEventListener('message', handler)
  }, [ws]);

  useEffect(() => {
    if (sortKey) stickRef.current = false;
  }, [sortKey]);

  useEffect(() => {
    if (sub === 'patterns' && ws?.readyState === WebSocket.OPEN) {
      setPatternsLoading(true);
      ws.send(JSON.stringify({ type: 'traffic:patterns' }));
    }
  }, [sub, ws]);

  const addLog = (text: string, type: TestEntry['type']) => {
    setTestLog((prev) => [
      ...prev,
      {
        id: crypto.randomUUID(),
        time: new Date().toLocaleTimeString('ru-RU', { hour: '2-digit', minute: '2-digit', second: '2-digit' }),
        text,
        type,
      },
    ]);
  };

  const formatDetails = (r: any): string => {
    const d = r.details;
    if (r.test === 'ping') return d.ms !== undefined ? `${d.ms}ms` : d.error || '';
    if (r.test === 'http') return d.statusCode ? `статус ${d.statusCode}` : d.error || '';
    if (r.test === 'osc') return r.success ? d.detail || '/ping' : d.error || '';
    if (r.test === 'artnet') return r.success ? 'ch1=255 (сброс через 1с)' : d.error || '';
    return '';
  };

  const handleScroll = () => {
    const el = scrollRef.current;
    if (!el) return;
    setScrollTop(el.scrollTop);
    stickRef.current = el.scrollTop + el.clientHeight >= el.scrollHeight - ROW_H * 2;
  };

  const sendCmd = (data: any) => {
    if (ws?.readyState === WebSocket.OPEN) {
      ws.send(JSON.stringify(data));
    } else {
      addLog('Нет соединения с сервером', 'error');
    }
  };

  const toggleCapture = () => {
    const t = a.captureRunning ? 'traffic:capture_stop' : 'traffic:capture_start';
    sendCmd({ type: t });
    addLog(a.captureRunning ? 'Останавливаю анализ трафика…' : 'Возобновляю анализ трафика…', 'info');
  };

  const handlePoll = () => {
    setScanning(true);
    sendCmd({ type: 'artnet:poll' });
    addLog('Отправлен ArtPoll запрос (broadcast 255.255.255.255:6454)…', 'info');
    setTimeout(() => setScanning(false), 3000);
  };

  const runTests = (ip: string, tests: string[]) => {
    setBusyIps((prev) => new Set(prev).add(ip));
    sendCmd({ type: 'device:test', ip, tests });
    addLog(`Запуск тестов для ${ip}: ${tests.join(', ')}…`, 'info');
  };

  const sendTestCommand = () => {
    const text = testInput.trim();
    if (!text) return;
    addLog(text, 'sent');
    setTestInput('');
  };

  return (
    <div style={{ position: 'absolute', inset: 0, display: 'flex', flexDirection: 'column', animation: 'ariaFadeFast .22s ease both' }}>
      {/* ─── High-Tech Subtab Navigation & Controls Bar ─── */}
      <div style={{
        display: 'flex',
        flexWrap: 'wrap',
        alignItems: 'center',
        gap: 12,
        padding: '10px 24px',
        borderBottom: '1px solid rgba(255,255,255,0.06)',
        background: 'rgba(6, 8, 13, 0.55)',
        backdropFilter: 'blur(16px)',
      }}>
        {/* Navigation Tabs */}
        <div style={{
          display: 'flex',
          gap: 4,
          background: 'rgba(255,255,255,0.03)',
          padding: 3,
          borderRadius: 10,
          border: '1px solid rgba(255,255,255,0.05)',
        }}>
          {(['capture', 'patterns', 'instant', 'daily', 'weekly', 'test'] as SubTab[]).map((t) => {
            const active = sub === t;
            return (
              <button
                key={t}
                onClick={() => setSub(t)}
                style={{
                  padding: '5px 13px',
                  borderRadius: 7,
                  border: 'none',
                  cursor: 'pointer',
                  fontSize: 11,
                  fontFamily: C.mono,
                  letterSpacing: '.06em',
                  fontWeight: active ? 700 : 500,
                  background: active ? 'rgba(255,107,91,0.18)' : 'transparent',
                  color: active ? '#ff8a7a' : 'rgba(255,255,255,0.5)',
                  boxShadow: active ? '0 2px 10px rgba(255,107,91,0.15)' : 'none',
                  transition: 'all .18s',
                }}
              >
                {{ capture: 'Захват', patterns: 'Паттерны', instant: 'Сейчас', daily: 'Суточный', weekly: 'Недельный', test: 'Тест' }[t]}
              </button>
            );
          })}
        </div>

        {sub === 'capture' && (
          <>
            {/* Live Capture Status Indicator */}
            <div style={{
              display: 'flex',
              alignItems: 'center',
              gap: 8,
              padding: '6px 12px',
              borderRadius: 999,
              background: a.captureRunning ? 'rgba(57,217,138,0.1)' : 'rgba(255,84,112,0.1)',
              border: a.captureRunning ? '1px solid rgba(57,217,138,0.3)' : '1px solid rgba(255,84,112,0.3)',
            }}>
              <span style={{
                width: 7,
                height: 7,
                borderRadius: '50%',
                background: a.captureRunning ? '#39d98a' : '#ff5470',
                boxShadow: a.captureRunning ? '0 0 10px #39d98a' : '0 0 10px #ff5470',
              }} />
              <span style={{
                fontFamily: C.mono,
                fontSize: 10.5,
                fontWeight: 700,
                color: a.captureRunning ? '#39d98a' : '#ff5470',
                letterSpacing: '.1em',
              }}>
                {a.captureRunning ? 'АКТИВЕН' : 'ПАУЗА'}
              </span>
            </div>

            {/* Toggle Capture Button */}
            <button
              onClick={toggleCapture}
              style={{
                ...ctrlBtn,
                background: a.captureRunning ? 'rgba(255,84,112,0.12)' : 'rgba(57,217,138,0.14)',
                borderColor: a.captureRunning ? 'rgba(255,84,112,0.35)' : 'rgba(57,217,138,0.35)',
                color: a.captureRunning ? '#ff8a9e' : '#39d98a',
              }}
            >
              <Icon name={a.captureRunning ? 'stop' : 'play'} size={12} />
              <span>{a.captureRunning ? 'Остановить' : 'Возобновить'}</span>
            </button>

            {/* NIC Selector */}
            <div style={{
              display: 'flex',
              alignItems: 'center',
              gap: 8,
              background: 'rgba(255,255,255,0.03)',
              border: '1px solid rgba(255,255,255,0.08)',
              borderRadius: 8,
              padding: '5px 10px',
            }}>
              <span style={{ color: 'rgba(255,255,255,0.4)', fontFamily: C.mono, fontSize: 11 }}>⇄</span>
              <select
                value={a.currentInterface || ''}
                onChange={(e) => {
                  const ip = e.target.value;
                  if (ip && ip !== a.currentInterface) {
                    addLog(`Интерфейс: ${ip}…`, 'info');
                    a.selectInterface(ip);
                  }
                }}
                title="Интерфейс захвата"
                style={{
                  background: 'transparent',
                  border: 'none',
                  outline: 'none',
                  fontFamily: C.mono,
                  fontSize: 11.5,
                  color: '#cbd5e1',
                  cursor: 'pointer',
                  maxWidth: 220,
                }}
              >
                <option value="" disabled style={{ background: '#090d15' }}>
                  {a.interfaces.length === 0 ? 'Интерфейсы…' : 'Выберите сетевой адаптер'}
                </option>
                {a.interfaces.map((nic) => (
                  <option key={nic.id} value={nic.ip} disabled={!nic.up || nic.loopback} style={{ background: '#090d15' }}>
                    {nic.name} · {nic.ip}{!nic.up ? ' (выкл)' : nic.loopback ? ' (loopback)' : nic.speed > 0 ? ` · ${nic.speed} Мбит` : ''}
                  </option>
                ))}
              </select>
            </div>

            {/* Packet Filter Input */}
            <div style={{
              flex: 1,
              display: 'flex',
              alignItems: 'center',
              gap: 8,
              background: 'rgba(255,255,255,0.03)',
              border: '1px solid rgba(255,255,255,0.08)',
              borderRadius: 8,
              padding: '6px 12px',
              minWidth: 180,
            }}>
              <span style={{ color: 'rgba(255,255,255,0.3)', fontSize: 12 }}>⌕</span>
              <input
                value={filter}
                onChange={(e) => setFilter(e.target.value)}
                placeholder="Фильтр: proto == artnet || port == 6454…"
                style={{
                  flex: 1,
                  background: 'transparent',
                  border: 'none',
                  outline: 'none',
                  fontFamily: C.mono,
                  fontSize: 12,
                  color: '#f1f5f9',
                }}
              />
            </div>

            {/* Export Buttons */}
            <div style={{ display: 'flex', gap: 6 }}>
              <button onClick={() => handleExport('csv')} style={ctrlBtn}>CSV</button>
              <button onClick={() => handleExport('pcap')} style={ctrlBtn}>PCAP</button>
            </div>

            {/* Packet Stats Badge */}
            <div style={{
              fontFamily: C.mono,
              fontSize: 11,
              color: 'rgba(126,139,160,0.85)',
              padding: '4px 10px',
              borderRadius: 6,
              background: 'rgba(255,255,255,0.02)',
              border: '1px solid rgba(255,255,255,0.05)',
            }}>
              {stats.total.toLocaleString('ru-RU')} пакетов · {(stats.bytes / 1024).toFixed(1)} КБ
            </div>
          </>
        )}

        {sub === 'patterns' && (
          <div style={{ display: 'flex', gap: 8, marginLeft: 'auto' }}>
            <button
              onClick={() => {
                setPatternsLoading(true);
                sendCmd({ type: 'traffic:patterns' });
              }}
              style={ctrlBtn}
            >
              {patternsLoading ? 'Загрузка…' : 'Обновить'}
            </button>
            <button
              onClick={() => sendCmd({ type: 'traffic:patterns_clear' })}
              style={{ ...ctrlBtn, background: 'rgba(255,84,112,0.12)', borderColor: 'rgba(255,84,112,0.3)', color: '#ff5470' }}
            >
              Очистить
            </button>
          </div>
        )}

        {sub === 'test' && (
          <button onClick={handlePoll} disabled={scanning} style={{ ...ctrlBtn, marginLeft: 'auto' }}>
            {scanning ? 'Сканирование...' : 'ArtPoll'}
          </button>
        )}

        {(sub === 'instant' || sub === 'daily' || sub === 'weekly') && (
          <button
            onClick={() => {
              if (sub === 'instant') a.requestInstantAnalysis();
              else if (sub === 'daily') a.requestDailyAnalysis();
              else a.requestWeeklyAnalysis();
            }}
            style={{ ...ctrlBtn, marginLeft: 'auto' }}
          >
            Обновить анализ
          </button>
        )}
      </div>

      {/* ─── Main View Content ─── */}
      {sub === 'capture' && (
        <div style={{ flex: 1, display: 'flex', flexDirection: 'column', minHeight: 0 }}>
          {/* Protocol Distribution Bar */}
          {stats.entries.length > 0 && (
            <div style={{
              display: 'flex',
              gap: 16,
              alignItems: 'center',
              padding: '6px 24px',
              borderBottom: '1px solid rgba(255,255,255,0.05)',
              background: 'rgba(6, 9, 15, 0.4)',
              overflowX: 'auto',
            }}>
              {stats.entries.map(([proto, n]) => {
                const max = stats.entries[0][1];
                const pct = Math.max(8, Math.round((n / max) * 100));
                return (
                  <div key={proto} style={{ display: 'flex', flexDirection: 'column', gap: 3, minWidth: 68 }}>
                    <div style={{ display: 'flex', justifyContent: 'space-between', fontFamily: C.mono, fontSize: 10 }}>
                      <span style={{ color: protoColor(proto), fontWeight: 700 }}>{proto}</span>
                      <span style={{ color: 'rgba(255,255,255,0.45)' }}>{n}</span>
                    </div>
                    <div style={{ height: 3, borderRadius: 2, background: 'rgba(255,255,255,0.06)', overflow: 'hidden' }}>
                      <div style={{ width: `${pct}%`, height: '100%', background: protoColor(proto), borderRadius: 2 }} />
                    </div>
                  </div>
                );
              })}
            </div>
          )}

          {/* Packet Table + Side Inspector Split Pane */}
          <div style={{ flex: 1, display: 'flex', minHeight: 0 }}>
            {/* Virtualized Table Container */}
            <div style={{ flex: 1, display: 'flex', flexDirection: 'column', minWidth: 0 }}>
              {/* Sticky Table Header */}
              <div style={{
                display: 'grid',
                gridTemplateColumns: COLS,
                alignItems: 'center',
                padding: '0',
                height: 32,
                borderBottom: '1px solid rgba(255,255,255,0.08)',
                background: 'rgba(10, 15, 24, 0.9)',
                fontFamily: C.mono,
                fontSize: 10.5,
                fontWeight: 700,
                color: 'rgba(255,255,255,0.5)',
                letterSpacing: '.06em',
                userSelect: 'none',
              }}>
                <HeaderCell onClick={() => toggleSort('no')} active={sortKey === 'no'}>#</HeaderCell>
                <HeaderCell onClick={() => toggleSort('time')} active={sortKey === 'time'}>ВРЕМЯ</HeaderCell>
                <HeaderCell onClick={() => toggleSort('src')} active={sortKey === 'src'}>ИСТОЧНИК</HeaderCell>
                <HeaderCell onClick={() => toggleSort('dst')} active={sortKey === 'dst'}>НАЗНАЧЕНИЕ</HeaderCell>
                <HeaderCell onClick={() => toggleSort('proto')} active={sortKey === 'proto'}>ПРОТОКОЛ</HeaderCell>
                <HeaderCell onClick={() => toggleSort('len')} active={sortKey === 'len'}>БАЙТ</HeaderCell>
                <HeaderCell onClick={() => toggleSort('info')} active={sortKey === 'info'}>ИНФОРМАЦИЯ</HeaderCell>
              </div>

              {/* Scrollable Virtualized Body */}
              <div ref={scrollRef} onScroll={handleScroll} style={{ flex: 1, overflowY: 'auto', position: 'relative' }}>
                {display.length === 0 && (
                  <div style={{
                    fontFamily: C.mono,
                    fontSize: 12,
                    color: C.textDim,
                    textAlign: 'center',
                    marginTop: 80,
                  }}>
                    {filter.trim() ? 'Нет пакетов, соответствующих фильтру' : 'Нет захваченных пакетов'}
                  </div>
                )}

                <div style={{ height: display.length * ROW_H, position: 'relative' }}>
                  {slice.map((pk, idx) => {
                    const i = winStart + idx;
                    const isSel = a.selPacket === i;
                    const hl = a.highlightIds.has(`${pk.time}|${pk.src}|${pk.dst}|${pk.proto}|${pk.len}`);
                    return (
                      <div
                        key={pk.no}
                        onClick={() => {
                          stickRef.current = false;
                          a.setSelPacket(i);
                        }}
                        style={{
                          position: 'absolute',
                          top: i * ROW_H,
                          left: 0,
                          right: 0,
                          height: ROW_H,
                          display: 'grid',
                          gridTemplateColumns: COLS,
                          alignItems: 'center',
                          cursor: 'pointer',
                          borderBottom: '1px solid rgba(255,255,255,0.025)',
                          background: isSel
                            ? 'linear-gradient(90deg, rgba(255,107,91,0.18) 0%, rgba(255,107,91,0.04) 100%)'
                            : hl
                            ? 'rgba(56,189,248,0.12)'
                            : i % 2
                            ? 'rgba(255,255,255,0.015)'
                            : 'transparent',
                          borderLeft: isSel ? '3px solid #ff6b5b' : '3px solid transparent',
                          transition: 'background .12s',
                        }}
                      >
                        <Cell color="rgba(255,255,255,0.35)">{pk.no}</Cell>
                        <Cell color="#94a3b8">{pk.time}</Cell>
                        <Cell color="#e2e8f0">{pk.src}{pk.sport ? `:${pk.sport}` : ''}</Cell>
                        <Cell color="#e2e8f0">{pk.dst}{pk.dport ? `:${pk.dport}` : ''}</Cell>
                        <div style={{ padding: '0 10px' }}>
                          <span style={{
                            padding: '2px 8px',
                            borderRadius: 6,
                            fontSize: 10,
                            fontWeight: 700,
                            fontFamily: C.mono,
                            color: protoColor(pk.proto),
                            background: 'rgba(255,255,255,0.04)',
                            border: `1px solid ${protoColor(pk.proto)}33`,
                          }}>
                            {pk.proto}
                          </span>
                        </div>
                        <Cell color="rgba(255,255,255,0.4)">{pk.len}</Cell>
                        <Cell color="#cbd5e1" ellipsis>{pk.info}</Cell>
                      </div>
                    );
                  })}
                </div>
              </div>
            </div>

            {/* Right Packet Inspector Drawer */}
            <aside style={{
              width: 360,
              flexShrink: 0,
              borderLeft: '1px solid rgba(255,255,255,0.06)',
              background: 'rgba(6, 8, 13, 0.65)',
              backdropFilter: 'blur(20px)',
              overflowY: 'auto',
              padding: 20,
            }}>
              <div style={{
                fontFamily: C.mono,
                fontSize: 10,
                letterSpacing: '.18em',
                color: 'rgba(255,255,255,0.45)',
                marginBottom: 16,
                fontWeight: 700,
              }}>
                РАЗБОР ПАКЕТА
              </div>
              <PacketBreakdown p={p} />
            </aside>
          </div>
        </div>
      )}

      {/* Patterns Tab */}
      {sub === 'patterns' && (
        <div style={{ flex: 1, display: 'flex', minHeight: 0 }}>
          <div style={{ flex: 1, overflowY: 'auto', padding: 24 }}>
            {patternsLoading && patterns.length === 0 ? (
              <div style={{
                fontFamily: C.mono,
                fontSize: 12,
                color: C.textDim,
                textAlign: 'center',
                marginTop: 60,
                display: 'flex',
                flexDirection: 'column',
                alignItems: 'center',
                gap: 12,
              }}>
                <div style={{
                  width: 20,
                  height: 20,
                  border: '2px solid rgba(255,255,255,0.1)',
                  borderTopColor: '#ff6b5b',
                  borderRadius: '50%',
                  animation: 'ariaSpin 1s linear infinite',
                }} />
                Поиск паттернов в сетевом трафике…
              </div>
            ) : patterns.length === 0 ? (
              <div style={{
                fontFamily: C.mono,
                fontSize: 12,
                color: C.textDim,
                textAlign: 'center',
                marginTop: 60,
                lineHeight: 2,
              }}>
                Паттернов и аномалий пока не обнаружено
                <br />
                Запустите захват пакетов для автоматического анализа ARIA
              </div>
            ) : (
              <div style={{ display: 'flex', flexDirection: 'column', gap: 12, maxWidth: 900 }}>
                {patterns.map((p) => (
                  <div
                    key={p.id}
                    onClick={() => setInspector({ title: p.description, ips: [...p.related_ips] })}
                    style={{
                      padding: 16,
                      borderRadius: 14,
                      cursor: 'pointer',
                      background: 'rgba(255,255,255,0.02)',
                      border: '1px solid rgba(255,255,255,0.07)',
                      borderLeft: `4px solid ${p.suggested_action === 'alert' ? '#ff5470' : p.suggested_action === 'warn' ? '#ffb84d' : '#39d98a'}`,
                      boxShadow: '0 4px 16px rgba(0,0,0,0.2)',
                      transition: 'all .18s',
                    }}
                    onMouseEnter={(e) => {
                      e.currentTarget.style.background = 'rgba(255,255,255,0.04)';
                      e.currentTarget.style.borderColor = 'rgba(255,255,255,0.12)';
                    }}
                    onMouseLeave={(e) => {
                      e.currentTarget.style.background = 'rgba(255,255,255,0.02)';
                      e.currentTarget.style.borderColor = 'rgba(255,255,255,0.07)';
                    }}
                  >
                    <div style={{ display: 'flex', alignItems: 'center', gap: 10, marginBottom: 8 }}>
                      <span style={{
                        padding: '3px 9px',
                        borderRadius: 6,
                        fontFamily: C.mono,
                        fontSize: 10,
                        fontWeight: 700,
                        background: p.suggested_action === 'alert' ? 'rgba(255,84,112,0.2)' : p.suggested_action === 'warn' ? 'rgba(255,184,77,0.2)' : 'rgba(57,217,138,0.2)',
                        color: p.suggested_action === 'alert' ? '#ff5470' : p.suggested_action === 'warn' ? '#ffb84d' : '#39d98a',
                      }}>
                        {p.suggested_action.toUpperCase()}
                      </span>
                      <span style={{ fontFamily: C.mono, fontSize: 11, color: 'rgba(255,255,255,0.4)' }}>
                        {new Date(p.detected_at).toLocaleTimeString('ru-RU')}
                      </span>
                      <button
                        onClick={(e) => {
                          e.stopPropagation();
                          a.explainTrafficItem(`Паттерн: ${p.description}. Действие: ${p.suggested_action}. IP: ${p.related_ips.join(', ')}`, p.related_ips[0], 'pattern');
                        }}
                        style={{
                          marginLeft: 'auto',
                          padding: '4px 10px',
                          borderRadius: 6,
                          border: '1px solid rgba(56,189,248,0.3)',
                          cursor: 'pointer',
                          fontFamily: C.mono,
                          fontSize: 10,
                          fontWeight: 600,
                          background: 'rgba(56,189,248,0.12)',
                          color: '#38bdf8',
                        }}
                      >
                        Спросить ИИ
                      </button>
                    </div>
                    <div style={{ fontFamily: "'Plus Jakarta Sans', sans-serif", fontSize: 13.5, color: '#f1f5f9', marginBottom: 6, lineHeight: 1.5 }}>
                      {p.description}
                    </div>
                    {p.related_ips.length > 0 && (
                      <div style={{ fontFamily: C.mono, fontSize: 11, color: '#94a3b8' }}>
                        IP узлы: {p.related_ips.join(', ')}
                      </div>
                    )}
                  </div>
                ))}
              </div>
            )}
          </div>

          {inspector ? (
            <InspectorPanel target={inspector} explanation={a.trafficExplanation} packets={a.packets} onClose={() => setInspector(null)} />
          ) : (
            <ExplanationPanel explanation={a.trafficExplanation} />
          )}
        </div>
      )}

      {/* Tests Tab */}
      {sub === 'test' && (
        <div style={{ flex: 1, display: 'flex', flexDirection: 'column', minHeight: 0 }}>
          <div ref={testLogRef} style={{ flex: 1, overflowY: 'auto', padding: 24 }}>
            {devices.length > 0 && (
              <div style={{ display: 'flex', flexDirection: 'column', gap: 12, marginBottom: 24, maxWidth: 900 }}>
                {devices.map((d) => (
                  <div key={d.ip} style={{
                    padding: 16,
                    borderRadius: 14,
                    background: 'rgba(255,255,255,0.02)',
                    border: '1px solid rgba(255,255,255,0.07)',
                  }}>
                    <div style={{ display: 'flex', alignItems: 'center', gap: 12, marginBottom: 12 }}>
                      <span style={{ fontFamily: C.mono, fontSize: 13, fontWeight: 700, color: '#39d98a' }}>{d.ip}</span>
                      <span style={{ fontFamily: "'Plus Jakarta Sans', sans-serif", fontSize: 13, color: '#94a3b8' }}>{d.name}</span>
                    </div>
                    <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }}>
                      {(['ping', 'http', 'osc', 'artnet'] as const).map((t) => (
                        <button key={t} onClick={() => runTests(d.ip, [t])} disabled={busyIps.has(d.ip)} style={testBtnStyle(t)}>
                          {TEST_LABELS[t]}
                        </button>
                      ))}
                      <button onClick={() => runTests(d.ip, ['ping', 'http', 'osc', 'artnet'])} disabled={busyIps.has(d.ip)} style={{ ...testBtnStyle('all'), color: '#39d98a' }}>
                        Все тесты
                      </button>
                    </div>
                  </div>
                ))}
              </div>
            )}

            {/* Test Log Terminal */}
            <div style={{
              background: 'rgba(4, 7, 12, 0.9)',
              border: '1px solid rgba(255,255,255,0.07)',
              borderRadius: 12,
              padding: '16px 20px',
              fontFamily: C.mono,
              fontSize: 12,
              lineHeight: 1.7,
              minHeight: 240,
            }}>
              {testLog.length === 0 && (
                <div style={{ color: 'rgba(255,255,255,0.3)', textAlign: 'center', padding: '40px 0' }}>
                  Лог тестирования чист. Нажмите ArtPoll или отправьте команду.
                </div>
              )}
              {testLog.map((log) => (
                <div key={log.id} style={{
                  color: log.type === 'success' ? '#39d98a' : log.type === 'error' ? '#ff5470' : log.type === 'sent' ? '#38bdf8' : '#94a3b8',
                }}>
                  <span style={{ color: 'rgba(255,255,255,0.3)', marginRight: 10 }}>[{log.time}]</span>
                  {log.text}
                </div>
              ))}
            </div>
          </div>

          <div style={{
            display: 'flex',
            gap: 10,
            padding: '12px 24px',
            borderTop: '1px solid rgba(255,255,255,0.06)',
            background: 'rgba(6, 8, 13, 0.6)',
          }}>
            <input
              value={testInput}
              onChange={(e) => setTestInput(e.target.value)}
              onKeyDown={(e) => {
                if (e.key === 'Enter') sendTestCommand();
              }}
              placeholder="Введите ручную команду или эхо..."
              style={{
                flex: 1,
                padding: '8px 14px',
                borderRadius: 8,
                border: '1px solid rgba(255,255,255,0.08)',
                background: 'rgba(255,255,255,0.03)',
                fontFamily: C.mono,
                fontSize: 12,
                color: '#f8fafc',
                outline: 'none',
              }}
            />
            <button onClick={sendTestCommand} style={ctrlBtn}>
              Отправить
            </button>
          </div>
        </div>
      )}

      {/* Reports Subtabs */}
      {(sub === 'instant' || sub === 'daily' || sub === 'weekly') && (
        <div style={{ flex: 1, display: 'flex', minHeight: 0 }}>
          {sub === 'instant' && <ReportInstant report={a.trafficReport} onExplain={a.explainTrafficItem} onInspect={setInspector} />}
          {(sub === 'daily' || sub === 'weekly') && <ReportPeriod report={a.trafficReport} onExplain={a.explainTrafficItem} onInspect={setInspector} />}
          {inspector ? (
            <InspectorPanel target={inspector} explanation={a.trafficExplanation} packets={a.packets} onClose={() => setInspector(null)} />
          ) : (
            <ExplanationPanel explanation={a.trafficExplanation} />
          )}
        </div>
      )}
    </div>
  );
}

function HeaderCell({ children, onClick, active }: { children: React.ReactNode; onClick?: () => void; active?: boolean }) {
  return (
    <div
      onClick={onClick}
      style={{
        padding: '0 10px',
        cursor: 'pointer',
        color: active ? '#ff8a7a' : 'rgba(255,255,255,0.5)',
        display: 'flex',
        alignItems: 'center',
        gap: 4,
        transition: 'color .15s',
      }}
    >
      <span>{children}</span>
      {active && <span style={{ fontSize: 9 }}>▼</span>}
    </div>
  );
}

function Cell({ children, color, bold, ellipsis }: { children: React.ReactNode; color: string; bold?: boolean; ellipsis?: boolean }) {
  return (
    <div
      style={{
        padding: '0 10px',
        color,
        fontWeight: bold ? 700 : 400,
        fontFamily: C.mono,
        fontSize: 11,
        lineHeight: `${ROW_H}px`,
        ...(ellipsis ? { overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' as const } : null),
      }}
    >
      {children}
    </div>
  );
}

function InspectorPanel({ target, explanation, packets, onClose }: { target: InspectorTarget; explanation: string | null; packets: Packet[]; onClose: () => void }) {
  const [sel, setSel] = useState(-1);
  useEffect(() => {
    setSel(-1);
  }, [target]);

  const matched = useMemo(() => {
    const ips = target.ips.map((x) => x.toLowerCase()).filter(Boolean);
    if (ips.length === 0) return [];
    return packets
      .filter((pk) => ips.includes(pk.src.toLowerCase()) || ips.includes(pk.dst.toLowerCase()))
      .slice(-400)
      .reverse();
  }, [target, packets]);

  const selected = sel >= 0 && sel < matched.length ? matched[sel] : null;

  return (
    <div style={{
      width: 420,
      flexShrink: 0,
      borderLeft: '1px solid rgba(255,255,255,0.06)',
      background: 'rgba(6, 8, 13, 0.7)',
      backdropFilter: 'blur(20px)',
      overflowY: 'auto',
      padding: 20,
    }}>
      <div style={{ display: 'flex', alignItems: 'flex-start', gap: 10, marginBottom: 4 }}>
        <div style={{ fontFamily: "'Plus Jakarta Sans', sans-serif", fontSize: 13, fontWeight: 700, color: '#f8fafc', flex: 1, minWidth: 0, wordBreak: 'break-word' }}>
          {target.title}
        </div>
        <button
          onClick={onClose}
          title="Закрыть"
          style={{
            flexShrink: 0,
            width: 24,
            height: 24,
            borderRadius: 6,
            border: 'none',
            cursor: 'pointer',
            background: 'rgba(255,255,255,0.07)',
            color: '#94a3b8',
            fontFamily: C.mono,
            fontSize: 12,
          }}
        >
          ✕
        </button>
      </div>

      <div style={{ fontFamily: C.mono, fontSize: 10, color: 'rgba(255,255,255,0.4)', marginBottom: 14 }}>
        {target.ips.length === 0
          ? 'IP не указаны — совпадения не ищутся'
          : `Найдено пакетов: ${matched.length}${matched.length >= 400 ? ' (последние 400)' : ''}`}
      </div>

      <div style={{ maxHeight: '36vh', overflowY: 'auto', marginBottom: 16, borderRadius: 10, border: '1px solid rgba(255,255,255,0.06)', background: 'rgba(0,0,0,0.3)' }}>
        {matched.length === 0 ? (
          <div style={{ fontFamily: C.mono, fontSize: 11, color: C.textDim, padding: '16px 10px', textAlign: 'center' }}>Совпадений нет</div>
        ) : (
          matched.map((pk, i) => {
            const isSel = sel === i;
            return (
              <div
                key={`${pk.no}-${i}`}
                onClick={() => setSel(i)}
                style={{
                  padding: '7px 10px',
                  cursor: 'pointer',
                  borderBottom: '1px solid rgba(255,255,255,0.03)',
                  background: isSel ? 'rgba(255,107,91,0.14)' : i % 2 ? 'rgba(255,255,255,0.02)' : 'transparent',
                  borderLeft: isSel ? '2px solid #ff6b5b' : '2px solid transparent',
                }}
              >
                <div style={{ display: 'flex', alignItems: 'baseline', gap: 8, fontFamily: C.mono, fontSize: 10.5 }}>
                  <span style={{ color: 'rgba(255,255,255,0.35)', flexShrink: 0 }}>{pk.time}</span>
                  <span style={{ flex: 1, minWidth: 0, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap', color: '#cbd5e1' }}>
                    {pk.src}{pk.sport ? `:${pk.sport}` : ''} → {pk.dst}{pk.dport ? `:${pk.dport}` : ''}
                  </span>
                  <span style={{ color: protoColor(pk.proto), fontWeight: 700, flexShrink: 0 }}>{pk.proto}</span>
                  <span style={{ color: 'rgba(255,255,255,0.35)', flexShrink: 0 }}>{pk.len}</span>
                </div>
                <div style={{ fontFamily: C.mono, fontSize: 10, color: 'rgba(255,255,255,0.4)', marginTop: 2, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                  {pk.info}
                </div>
              </div>
            );
          })
        )}
      </div>

      <div style={{ fontFamily: C.mono, fontSize: 10, letterSpacing: '.18em', color: 'rgba(255,255,255,0.4)', marginBottom: 12, fontWeight: 700 }}>
        РАЗБОР ПАКЕТА
      </div>
      <PacketBreakdown p={selected} />

      {explanation && (
        <>
          <div style={{ fontFamily: C.mono, fontSize: 10, letterSpacing: '.18em', color: 'rgba(255,255,255,0.4)', margin: '18px 0 10px', fontWeight: 700 }}>
            ОТВЕТ ИИ
          </div>
          <div style={{ fontFamily: "'Plus Jakarta Sans', sans-serif", fontSize: 12.5, color: '#cbd5e1', lineHeight: 1.7, whiteSpace: 'pre-wrap' }}>
            {explanation}
          </div>
        </>
      )}
    </div>
  );
}

function ExplanationPanel({ explanation }: { explanation: string | null }) {
  return (
    <aside style={{
      width: 360,
      flexShrink: 0,
      borderLeft: '1px solid rgba(255,255,255,0.06)',
      background: 'rgba(6, 8, 13, 0.65)',
      backdropFilter: 'blur(20px)',
      overflowY: 'auto',
      padding: 24,
    }}>
      <div style={{ fontFamily: C.mono, fontSize: 10, letterSpacing: '.18em', color: 'rgba(255,255,255,0.4)', marginBottom: 14, fontWeight: 700 }}>
        ИНТЕЛЛЕКТУАЛЬНЫЙ АНАЛИЗ
      </div>
      {explanation ? (
        <div style={{ fontFamily: "'Plus Jakarta Sans', sans-serif", fontSize: 13, color: '#e2e8f0', lineHeight: 1.7, whiteSpace: 'pre-wrap' }}>
          {explanation}
        </div>
      ) : (
        <div style={{ fontFamily: C.mono, fontSize: 11.5, color: 'rgba(126,139,160,0.7)', lineHeight: 1.8 }}>
          Нажмите «Спросить ИИ» на любом обнаруженном паттерне или аномалии для получения детального разбора.
        </div>
      )}
    </aside>
  );
}

const ctrlBtn: React.CSSProperties = {
  padding: '6px 14px',
  borderRadius: 8,
  border: '1px solid rgba(255,255,255,0.08)',
  background: 'rgba(255,255,255,0.03)',
  color: '#cbd5e1',
  cursor: 'pointer',
  fontFamily: C.mono,
  fontSize: 11,
  fontWeight: 600,
  display: 'inline-flex',
  alignItems: 'center',
  gap: 6,
  transition: 'all .18s',
};

const testBtnStyle = (t: string): React.CSSProperties => ({
  padding: '5px 12px',
  borderRadius: 6,
  border: '1px solid rgba(255,255,255,0.07)',
  cursor: 'pointer',
  fontFamily: C.mono,
  fontSize: 11,
  fontWeight: 600,
  transition: 'all .18s',
  background:
    t === 'ping'
      ? 'rgba(57,217,138,0.15)'
      : t === 'http'
      ? 'rgba(56,189,248,0.15)'
      : t === 'osc'
      ? 'rgba(168,130,255,0.15)'
      : t === 'artnet'
      ? 'rgba(255,107,91,0.15)'
      : 'rgba(255,255,255,0.05)',
  color:
    t === 'ping'
      ? '#39d98a'
      : t === 'http'
      ? '#38bdf8'
      : t === 'osc'
      ? '#a882ff'
      : t === 'artnet'
      ? '#ff6b5b'
      : '#cbd5e1',
});
