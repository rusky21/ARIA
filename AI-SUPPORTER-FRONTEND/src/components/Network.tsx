import { useState, useEffect, useRef, useMemo } from 'react'
import { C, protoColor, acc, cyanA, w } from '../lib/theme.js'
import { ui } from '../lib/ui.js'
import { Icon } from './Icon.js'
import type { Packet, InspectorTarget } from '../lib/data.js'
import type { AssistantState } from '../hooks/useAssistant.js'
import ReportInstant from './ReportInstant.js'
import ReportPeriod from './ReportPeriod.js'
import { PacketBreakdown } from './PacketDetail.js'

type SubTab = 'capture' | 'patterns' | 'instant' | 'daily' | 'weekly' | 'test'

const COLS = '54px 92px 1.4fr 1.4fr 78px 64px 2fr'
const ROW_H = 30
const OVERSCAN = 20

interface Pattern {
  id: string; description: string; related_ips: string[]
  suggested_action: string; detected_at: string
}

interface Device { ip: string; name: string }

interface TestEntry {
  id: string; time: string; text: string
  type: 'sent' | 'received' | 'info' | 'error' | 'success'
}

const TEST_LABELS: Record<string, string> = {
  ping: 'Ping', http: 'HTTP', osc: 'OSC', artnet: 'Art-Net',
}

const timeToSec = (t: string): number => {
  const [h, m, s] = t.split(':').map(Number)
  return (h || 0) * 3600 + (m || 0) * 60 + (s || 0)
}

const SORT_ACCESSORS: Record<string, (pk: Packet) => number | string> = {
  no: pk => pk.no,
  time: pk => timeToSec(pk.time),
  src: pk => pk.src,
  dst: pk => pk.dst,
  proto: pk => pk.proto,
  len: pk => pk.len,
  info: pk => pk.info,
}

export default function Network({ a, ws }: { a: AssistantState; ws?: WebSocket | null }) {
  const [sub, setSub] = useState<SubTab>('capture')
  const [patterns, setPatterns] = useState<Pattern[]>([])
  const [patternsLoading, setPatternsLoading] = useState(false)
  const [devices, setDevices] = useState<Device[]>([])
  const [scanning, setScanning] = useState(false)
  const [busyIps, setBusyIps] = useState<Set<string>>(new Set())
  const [manualIp, setManualIp] = useState('')
  const [testLog, setTestLog] = useState<TestEntry[]>([])
  const [testInput, setTestInput] = useState('')
  const testLogRef = useRef<HTMLDivElement>(null)
  const [filter, setFilter] = useState('')
  const [trafficFiltered, setTrafficFiltered] = useState<Packet[]>([])
  const [sortKey, setSortKey] = useState('')
  const [sortDir, setSortDir] = useState<1 | -1>(1)
  const [inspector, setInspector] = useState<InspectorTarget | null>(null)

  const scrollRef = useRef<HTMLDivElement>(null)
  const stickRef = useRef(true)
  const [viewH, setViewH] = useState(0)
  const [scrollTop, setScrollTop] = useState(0)

  const display = useMemo(() => {
    const rows = filter.trim() ? (trafficFiltered.length ? trafficFiltered : a.trafficFiltered) : a.packets
    if (!sortKey) return rows
    const acc = SORT_ACCESSORS[sortKey]
    const dir = sortDir
    return [...rows].sort((x, y) => {
      const ax = acc(x)
      const by = acc(y)
      if (typeof ax === 'number' && typeof by === 'number') return (ax - by) * dir
      return String(ax).localeCompare(String(by)) * dir
    })
  }, [a.trafficFiltered, a.packets, trafficFiltered, filter, sortKey, sortDir])

  const stats = useMemo(() => {
    const counts = new Map<string, number>()
    let bytes = 0
    for (const pk of display) {
      counts.set(pk.proto, (counts.get(pk.proto) ?? 0) + 1)
      bytes += pk.len
    }
    return { entries: [...counts.entries()].sort((x, y) => y[1] - x[1]), bytes, total: display.length }
  }, [display])

  const selIdx = a.selPacket >= 0 ? a.selPacket : 0
  const p = display[selIdx] ?? display[display.length - 1] ?? null

  const winStart = Math.max(0, Math.floor(scrollTop / ROW_H) - OVERSCAN)
  const winEnd = Math.min(display.length, Math.ceil((scrollTop + viewH) / ROW_H) + OVERSCAN)
  const slice = display.slice(winStart, winEnd)

  const toggleSort = (key: string) => {
    if (sortKey === key) setSortDir(d => (d === 1 ? -1 : 1))
    else { setSortKey(key); setSortDir(1) }
  }

  const handleExport = (format: 'csv' | 'pcap') => {
    addLog(`Экспорт ${format.toUpperCase()}: формирую...`, 'info')
    a.trafficExport(format)
    addLog(`Экспорт ${format.toUpperCase()}: завершён`, 'success')
  }

  const filterTimer = useRef<ReturnType<typeof setTimeout> | null>(null)
  const activeFilter = filter.trim()
  useEffect(() => {
    if (filterTimer.current) clearTimeout(filterTimer.current)
    if (!activeFilter) {
      setTrafficFiltered([])
      return
    }
    filterTimer.current = setTimeout(() => {
      a.trafficFilter(activeFilter)
      stickRef.current = true
    }, 300)
    return () => { if (filterTimer.current) clearTimeout(filterTimer.current) }
  }, [activeFilter])

  useEffect(() => {
    if (testLogRef.current) testLogRef.current.scrollTop = testLogRef.current.scrollHeight
  }, [testLog])

  useEffect(() => {
    a.refreshInterfaces()
  }, [a.refreshInterfaces])

  useEffect(() => {
    const el = scrollRef.current
    if (!el) return
    const ro = new ResizeObserver(() => setViewH(el.clientHeight))
    ro.observe(el)
    setViewH(el.clientHeight)
    return () => ro.disconnect()
  }, [sub])

  useEffect(() => {
    if (sub !== 'capture') return
    stickRef.current = true
    setScrollTop(0)
    const el = scrollRef.current
    if (el && !sortKey) el.scrollTop = el.scrollHeight
  }, [sub])

  useEffect(() => {
    const el = scrollRef.current
    if (!el || !stickRef.current || sortKey) return
    el.scrollTop = el.scrollHeight
  }, [display.length, sortKey])

  useEffect(() => {
    if (!ws) return
    const handler = (e: MessageEvent) => {
      try {
        const parsed = JSON.parse(e.data)
        switch (parsed.type) {
          case 'traffic:patterns':
            if (parsed.patterns) setPatterns(parsed.patterns)
            setPatternsLoading(false)
            break
          case 'traffic:patterns_cleared':
            setPatterns([])
            setPatternsLoading(false)
            break
          case 'artnet:poll-result':
            if (parsed.devices) setDevices(parsed.devices)
            break
          case 'traffic:interface:set':
            if (parsed.ok) addLog(`Интерфейс захвата: ${parsed.ip}`, 'success')
            else addLog(`Ошибка переключения интерфейса: ${parsed.error || parsed.ip}`, 'error')
            break
          case 'device:test-result':
            if (parsed.ip && parsed.results) {
              for (const r of parsed.results) {
                addLog(`  ${r.success ? '✓' : '✗'} ${TEST_LABELS[r.test] || r.test} ${formatDetails(r)}`, r.success ? 'success' : 'error')
              }
              setBusyIps(prev => { const n = new Set(prev); n.delete(parsed.ip); return n })
            }
            break
        }
      } catch {}
    }
    ws.addEventListener('message', handler)
    return () => ws.removeEventListener('message', handler)
  }, [ws])

  useEffect(() => {
    if (sortKey) stickRef.current = false
  }, [sortKey])

  useEffect(() => {
    if (sub === 'patterns' && ws?.readyState === WebSocket.OPEN) {
      setPatternsLoading(true)
      ws.send(JSON.stringify({ type: 'traffic:patterns' }))
    }
  }, [sub, ws])

  const addLog = (text: string, type: TestEntry['type']) => {
    setTestLog(prev => [...prev, {
      id: crypto.randomUUID(),
      time: new Date().toLocaleTimeString('ru-RU', { hour: '2-digit', minute: '2-digit', second: '2-digit' }),
      text, type,
    }])
  }

  const formatDetails = (r: any): string => {
    const d = r.details
    if (r.test === 'ping') return d.ms !== undefined ? `${d.ms}ms` : d.error || ''
    if (r.test === 'http') return d.statusCode ? `статус ${d.statusCode}` : d.error || ''
    if (r.test === 'osc') return r.success ? (d.detail || '/ping') : d.error || ''
    if (r.test === 'artnet') return r.success ? 'ch1=255 (сброс через 1с)' : d.error || ''
    return ''
  }

  const handleScroll = () => {
    const el = scrollRef.current
    if (!el) return
    setScrollTop(el.scrollTop)
    stickRef.current = el.scrollTop + el.clientHeight >= el.scrollHeight - ROW_H * 2
  }

  const sendCmd = (data: any) => {
    if (ws?.readyState === WebSocket.OPEN) {
      ws.send(JSON.stringify(data))
    } else {
      addLog('Нет соединения с сервером', 'error')
    }
  }

  const toggleCapture = () => {
    const t = a.captureRunning ? 'traffic:capture_stop' : 'traffic:capture_start'
    sendCmd({ type: t })
    addLog(a.captureRunning ? 'Останавливаю анализ трафика…' : 'Возобновляю анализ трафика…', 'info')
  }

  const handlePoll = () => {
    setScanning(true)
    addLog('ArtPoll: сканирование...', 'info')
    sendCmd({ type: 'artnet:poll' })
    setTimeout(() => setScanning(false), 3000)
  }

  const runTests = (ip: string, tests: string[]) => {
    setBusyIps(prev => new Set(prev).add(ip))
    const label = tests.length === 4 ? 'Все' : tests.map(t => TEST_LABELS[t]).join('+')
    addLog(`► ${ip}: ${label}...`, 'info')
    sendCmd({ type: 'device:test', ip, tests })
  }

  const sendTestCommand = () => {
    const text = testInput.trim()
    if (!text) return
    addLog(text, 'sent')
    setTestInput('')
  }

  return (
    <div style={{ position: 'absolute', inset: 0, display: 'flex', flexDirection: 'column', animation: 'ariaFadeFast .22s ease both' }}>
      <div style={{ display: 'flex', flexWrap: 'wrap', alignItems: 'center', gap: 14, padding: '10px 24px', borderBottom: `1px solid rgba(255,255,255,.06)` }}>
        <div style={{ display: 'flex', gap: 4 }}>
          {(['capture', 'patterns', 'instant', 'daily', 'weekly', 'test'] as SubTab[]).map(t => (
            <button key={t} onClick={() => setSub(t)} style={{
              padding: '6px 14px', borderRadius: 6, border: 'none', cursor: 'pointer',
              fontSize: 11, fontFamily: C.mono, letterSpacing: '.08em',
              background: sub === t ? 'rgba(255,107,91,.2)' : 'transparent',
              color: sub === t ? C.accent : C.textDim,
              borderBottom: sub === t ? `2px solid ${C.accent}` : '2px solid transparent',
            }}>
              {{ capture: 'Захват', patterns: 'Паттерны', instant: 'Сейчас', daily: 'Суточный', weekly: 'Недельный', test: 'Тест' }[t]}
            </button>
          ))}
        </div>
        {sub === 'capture' && (
          <>
            <div style={{
              display: 'flex', alignItems: 'center', gap: 8, padding: '6px 13px', borderRadius: 8,
              background: a.captureRunning ? 'rgba(57,217,138,.1)' : 'rgba(255,84,112,.1)',
              border: a.captureRunning ? '1px solid rgba(57,217,138,.25)' : '1px solid rgba(255,84,112,.25)',
            }}>
              <span style={{
                width: 7, height: 7, borderRadius: '50%',
                background: a.captureRunning ? C.green : C.red,
                boxShadow: `0 0 8px ${a.captureRunning ? C.green : C.red}`,
                animation: a.captureRunning ? 'ariaDot 1.4s infinite' : 'none',
              }} />
              <span style={{ fontFamily: C.mono, fontSize: 11, color: a.captureRunning ? C.green : C.red, letterSpacing: '.1em' }}>
                {a.captureRunning ? 'ЗАХВАТ' : 'ОСТАНОВЛЕН'}
              </span>
            </div>
            <button onClick={toggleCapture} style={{ ...btnStyle, minWidth: 128, display: 'inline-flex', alignItems: 'center', justifyContent: 'center', gap: 7 }}>
              <Icon name={a.captureRunning ? 'stop' : 'play'} size={11} />
              {a.captureRunning ? 'Стоп анализа' : 'Возобновить'}
            </button>
            <div style={{ display: 'flex', alignItems: 'center', gap: 8, background: C.panel, border: `1px solid ${C.line}`, borderRadius: 8, padding: '6px 10px' }}>
              <span style={{ color: C.textDim, fontFamily: C.mono, fontSize: 11 }}>⇄</span>
              <select
                value={a.currentInterface || ''}
                onChange={e => {
                  const ip = e.target.value
                  if (ip && ip !== a.currentInterface) {
                    addLog(`Интерфейс: ${ip}…`, 'info')
                    a.selectInterface(ip)
                  }
                }}
                title="Интерфейс захвата"
                style={{
                  background: 'transparent', border: 'none', outline: 'none',
                  fontFamily: C.mono, fontSize: 12, color: C.textMid, cursor: 'pointer', maxWidth: 240,
                }}>
                <option value="" disabled>
                  {a.interfaces.length === 0 ? 'Интерфейсы…' : 'Выберите интерфейс'}
                </option>
                {a.interfaces.map(nic => (
                  <option key={nic.id} value={nic.ip} disabled={!nic.up || nic.loopback}>
                    {nic.name} · {nic.ip}{!nic.up ? ' (выкл)' : nic.loopback ? ' (loopback)' : nic.speed > 0 ? ` · ${nic.speed} Мбит/с` : ''}
                  </option>
                ))}
              </select>
            </div>
            <div style={{ flex: 1, display: 'flex', alignItems: 'center', gap: 8, background: C.panel, border: `1px solid ${C.line}`, borderRadius: 8, padding: '7px 13px', minWidth: 160 }}>
              <span style={{ color: C.textDim }}>⌕</span>
              <input value={filter} onChange={e => setFilter(e.target.value)} placeholder='proto == artnet && port == 6454 || src == 192.168.1.50'
                style={{ flex: 1, background: 'transparent', border: 'none', outline: 'none', fontFamily: C.mono, fontSize: 12, color: C.textMid }} />
            </div>
            <div style={{ display: 'flex', gap: 6 }}>
              <button onClick={() => handleExport('csv')} style={btnStyle}>CSV</button>
              <button onClick={() => handleExport('pcap')} style={btnStyle}>PCAP</button>
            </div>
            <div style={{ fontFamily: C.mono, fontSize: 11, color: C.textDim }}>{stats.total} пакетов · {stats.bytes.toLocaleString('ru-RU')} B</div>
          </>
        )}
        {sub === 'patterns' && (
          <div style={{ display: 'flex', gap: 8, marginLeft: 'auto' }}>
            <button onClick={() => { setPatternsLoading(true); sendCmd({ type: 'traffic:patterns' }) }} style={btnStyle}>
              {patternsLoading ? 'Загрузка…' : 'Обновить'}
            </button>
            <button onClick={() => sendCmd({ type: 'traffic:patterns_clear' })} style={{ ...btnStyle, background: 'rgba(255,84,112,.15)', color: C.red }}>Очистить</button>
          </div>
        )}
        {sub === 'test' && (
          <button onClick={handlePoll} disabled={scanning} style={{ ...btnStyle, marginLeft: 'auto' }}>
            {scanning ? 'Сканирование...' : 'ArtPoll'}
          </button>
        )}
        {sub === 'instant' && (
          <button onClick={a.requestInstantAnalysis} style={{ ...btnStyle, marginLeft: 'auto' }}>
            {a.trafficReport?.reportType === 'instant' ? 'Обновить' : 'Анализ'}
          </button>
        )}
        {sub === 'daily' && (
          <button onClick={a.requestDailyAnalysis} style={{ ...btnStyle, marginLeft: 'auto' }}>
            {a.trafficReport?.reportType === 'daily' ? 'Обновить' : 'Анализ'}
          </button>
        )}
        {sub === 'weekly' && (
          <button onClick={a.requestWeeklyAnalysis} style={{ ...btnStyle, marginLeft: 'auto' }}>
            {a.trafficReport?.reportType === 'weekly' ? 'Обновить' : 'Анализ'}
          </button>
        )}
      </div>

      {sub === 'capture' && (
        <div style={{ flex: 1, display: 'flex', flexDirection: 'column', minHeight: 0 }}>
          {stats.entries.length > 0 && (
            <div style={{ display: 'flex', gap: 16, alignItems: 'center', padding: '8px 18px', borderBottom: `1px solid ${C.line}`, background: 'rgba(8,12,20,.4)' }}>
              {stats.entries.map(([proto, n]) => {
                const max = stats.entries[0][1]
                return (
                  <div key={proto} style={{ display: 'flex', flexDirection: 'column', gap: 3, minWidth: 64 }}>
                    <div style={{ display: 'flex', justifyContent: 'space-between', fontFamily: C.mono, fontSize: 10, color: C.textDim }}>
                      <span style={{ color: protoColor(proto), fontWeight: 600 }}>{proto}</span>
                      <span>{n}</span>
                    </div>
                    <div style={{ height: 4, borderRadius: 2, background: 'rgba(255,255,255,.06)', overflow: 'hidden' }}>
                      <div style={{ width: `${Math.max(8, (n / max) * 100)}%`, height: '100%', background: protoColor(proto), borderRadius: 2 }} />
                    </div>
                  </div>
                )
              })}
            </div>
          )}
          <div style={{ flex: 1, display: 'flex', minHeight: 0 }}>
            <div style={{ flex: 1, display: 'flex', flexDirection: 'column', minWidth: 0 }}>
              <div style={{ display: 'grid', gridTemplateColumns: COLS, fontFamily: C.mono, fontSize: 11, background: C.headerSolid, borderBottom: `1px solid ${C.line}`, color: C.textDim, letterSpacing: '.08em', flexShrink: 0 }}>
                {(['no', 'time', 'src', 'dst', 'proto', 'len', 'info'] as const).map(h => (
                  <div key={h} onClick={() => toggleSort(h)} style={{ padding: '8px 10px', cursor: 'pointer', userSelect: 'none', display: 'flex', alignItems: 'center', gap: 4 }}>
                    {{ no: '№', time: 'ВРЕМЯ', src: 'ИСТОЧНИК', dst: 'НАЗНАЧЕНИЕ', proto: 'ПРОТО', len: 'ДЛИНА', info: 'ИНФО' }[h]}
                    {sortKey === h && <span style={{ fontSize: 9, color: C.accent }}>{sortDir === 1 ? '▲' : '▼'}</span>}
                  </div>
                ))}
              </div>
              <div ref={scrollRef} onScroll={handleScroll} style={{ flex: 1, overflowY: 'auto', minHeight: 0 }}>
                {display.length === 0 && (
                  <div style={{ fontFamily: C.mono, fontSize: 12, color: C.textDim, textAlign: 'center', marginTop: 60 }}>
                    {filter.trim() ? 'Нет пакетов по фильтру' : 'Нет захваченных пакетов'}
                  </div>
                )}
                <div style={{ height: display.length * ROW_H, position: 'relative' }}>
                  {slice.map((pk, idx) => {
                    const i = winStart + idx
                    const isSel = a.selPacket === i
                    const hl = a.highlightIds.has(`${pk.time}|${pk.src}|${pk.dst}|${pk.proto}|${pk.len}`)
                    return (
                      <div key={pk.no} onClick={() => { stickRef.current = false; a.setSelPacket(i) }} style={{
                        position: 'absolute', top: i * ROW_H, left: 0, right: 0, height: ROW_H,
                        display: 'grid', gridTemplateColumns: COLS, alignItems: 'center', cursor: 'pointer',
                        borderBottom: '1px solid rgba(255,255,255,.03)',
                        background: isSel ? acc(0.1) : hl ? cyanA(0.1) : i % 2 ? w(0.012) : 'transparent',
                        borderLeft: isSel ? `2px solid ${C.accent}` : '2px solid transparent',
                      }}>
                        <Cell color={C.textDim}>{pk.no}</Cell>
                        <Cell color={C.tableDim}>{pk.time}</Cell>
                        <Cell color={C.textMid}>{pk.src}{pk.sport ? `:${pk.sport}` : ''}</Cell>
                        <Cell color={C.textMid}>{pk.dst}{pk.dport ? `:${pk.dport}` : ''}</Cell>
                        <Cell color={protoColor(pk.proto)} bold>{pk.proto}</Cell>
                        <Cell color={C.tableDim}>{pk.len}</Cell>
                        <Cell color={C.tableText} ellipsis>{pk.info}</Cell>
                      </div>
                    )
                  })}
                </div>
              </div>
            </div>
            <div style={{ width: 340, flexShrink: 0, borderLeft: `1px solid rgba(255,255,255,.06)`, background: 'rgba(8,12,20,.5)', overflowY: 'auto', padding: 20 }}>
              <div style={{ fontFamily: C.mono, fontSize: 10, letterSpacing: '.18em', color: C.textDim, marginBottom: 14 }}>РАЗБОР ПАКЕТА</div>
              <PacketBreakdown p={p} />
            </div>
          </div>
        </div>
      )}

      {sub === 'patterns' && (
        <div style={{ flex: 1, display: 'flex', minHeight: 0 }}>
          <div style={{ flex: 1, overflowY: 'auto', padding: 20 }}>
            {patternsLoading && patterns.length === 0 ? (
              <div style={{ fontFamily: C.mono, fontSize: 12, color: C.textDim, textAlign: 'center', marginTop: 60, display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 10 }}>
                <div style={{ width: 18, height: 18, border: `2px solid ${C.line}`, borderTopColor: C.accent, borderRadius: '50%', animation: 'ariaSpin 1s linear infinite' }} />
                Загрузка паттернов…
              </div>
            ) : patterns.length === 0 ? (
              <div style={{ fontFamily: C.mono, fontSize: 12, color: C.textDim, textAlign: 'center', marginTop: 60, lineHeight: 2.2 }}>
                Паттернов пока нет
                <br />Запустите захват трафика и дождитесь анализа ИИ
              </div>
            ) : (
              <div style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
                {patterns.map(p => (
                  <div key={p.id} onClick={() => setInspector({ title: p.description, ips: [...p.related_ips] })} style={{
                    padding: 14, borderRadius: 10, cursor: 'pointer',
                    background: C.panelAlt, border: `1px solid ${C.line}`,
                    borderLeft: `3px solid ${p.suggested_action === 'alert' ? C.red : p.suggested_action === 'warn' ? '#ffb84d' : C.green}`,
                  }}>
                    <div style={{ display: 'flex', alignItems: 'center', gap: 10, marginBottom: 6 }}>
                      <span style={{
                        padding: '2px 8px', borderRadius: 4, fontFamily: C.mono, fontSize: 10,
                        background: p.suggested_action === 'alert' ? 'rgba(255,84,112,.2)' : p.suggested_action === 'warn' ? 'rgba(255,184,77,.2)' : 'rgba(57,217,138,.2)',
                        color: p.suggested_action === 'alert' ? C.red : p.suggested_action === 'warn' ? '#ffb84d' : C.green,
                      }}>
                        {p.suggested_action.toUpperCase()}
                      </span>
                      <span style={{ fontFamily: C.mono, fontSize: 10, color: C.textDim }}>
                        {new Date(p.detected_at).toLocaleTimeString('ru-RU')}
                      </span>
                      <button onClick={e => { e.stopPropagation(); a.explainTrafficItem(`Паттерн: ${p.description}. Действие: ${p.suggested_action}. IP: ${p.related_ips.join(', ')}`, p.related_ips[0], 'pattern') }}
                        style={{ marginLeft: 'auto', padding: '2px 8px', borderRadius: 4, border: 'none', cursor: 'pointer',
                          fontFamily: C.mono, fontSize: 9, background: 'rgba(63,214,255,.15)', color: C.cyan }}>
                        Спросить ИИ
                      </button>
                    </div>
                    <div style={{ fontFamily: C.mono, fontSize: 12, color: C.textMid, marginBottom: 4 }}>{p.description}</div>
                    {p.related_ips.length > 0 && (
                      <div style={{ fontFamily: C.mono, fontSize: 11, color: C.tableDim }}>
                        IP: {p.related_ips.join(', ')}
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

      {sub === 'test' && (
        <div style={{ flex: 1, display: 'flex', flexDirection: 'column', minHeight: 0 }}>
          <div ref={testLogRef} style={{ flex: 1, overflowY: 'auto', padding: 20 }}>
            {devices.length > 0 && (
              <div style={{ display: 'flex', flexDirection: 'column', gap: 10, marginBottom: 20 }}>
                {devices.map(d => (
                  <div key={d.ip} style={{
                    padding: 14, borderRadius: 10,
                    background: C.panelAlt, border: `1px solid ${C.line}`,
                  }}>
                    <div style={{ display: 'flex', alignItems: 'center', gap: 12, marginBottom: 10 }}>
                      <span style={{ fontFamily: C.mono, fontSize: 12, color: C.green }}>{d.ip}</span>
                      <span style={{ fontFamily: C.mono, fontSize: 11, color: C.textDim }}>{d.name}</span>
                    </div>
                    <div style={{ display: 'flex', gap: 6, flexWrap: 'wrap' }}>
                      {(['ping', 'http', 'osc', 'artnet'] as const).map(t => (
                        <button key={t} onClick={() => runTests(d.ip, [t])} disabled={busyIps.has(d.ip)}
                          style={testBtnStyle(t)}>{TEST_LABELS[t]}</button>
                      ))}
                      <button onClick={() => runTests(d.ip, ['ping', 'http', 'osc', 'artnet'])} disabled={busyIps.has(d.ip)}
                        style={{ ...testBtnStyle('all'), color: C.green }}>Все</button>
                    </div>
                  </div>
                ))}
              </div>
            )}
            <div style={{ display: 'flex', gap: 8, marginBottom: 20, alignItems: 'center' }}>
              <input value={manualIp} onChange={e => setManualIp(e.target.value)} placeholder="IP для теста..."
                style={{ flex: 1, padding: '7px 11px', borderRadius: 6, border: `1px solid ${C.line}`, background: C.panel, fontFamily: C.mono, fontSize: 12, color: C.textMid, outline: 'none' }} />
              {(['ping', 'http', 'osc', 'artnet'] as const).map(t => (
                <button key={t} onClick={() => manualIp && runTests(manualIp, [t])} disabled={!manualIp || busyIps.has(manualIp)}
                  style={testBtnStyle(t)}>{TEST_LABELS[t]}</button>
              ))}
              <button onClick={() => manualIp && runTests(manualIp, ['ping', 'http', 'osc', 'artnet'])} disabled={!manualIp || busyIps.has(manualIp)}
                style={{ ...testBtnStyle('all'), color: C.green }}>Все</button>
            </div>
            <div style={{ fontFamily: C.mono, fontSize: 11, display: 'flex', flexDirection: 'column', gap: 3 }}>
              {testLog.length === 0 && <div style={{ color: C.textDim }}>Лог тестирования</div>}
              {testLog.map(e => (
                <div key={e.id} style={{ color: e.type === 'error' ? C.red : e.type === 'success' ? C.green : e.type === 'sent' ? C.tableDim : C.textMid }}>
                  <span style={{ color: C.textDim }}>{e.time}</span> {e.text}
                </div>
              ))}
            </div>
          </div>
          <div style={{ display: 'flex', gap: 8, padding: '10px 20px', borderTop: `1px solid ${C.line}` }}>
            <input value={testInput} onChange={e => setTestInput(e.target.value)}
              onKeyDown={e => { if (e.key === 'Enter') sendTestCommand() }}
              placeholder="Введите команду..."
              style={{ flex: 1, padding: '7px 11px', borderRadius: 6, border: `1px solid ${C.line}`, background: C.panel, fontFamily: C.mono, fontSize: 12, color: C.textMid, outline: 'none' }} />
            <button onClick={sendTestCommand} style={btnStyle}>Отправить</button>
          </div>
        </div>
      )}

      {sub === 'instant' && (
        <div style={{ flex: 1, display: 'flex', minHeight: 0 }}>
          <ReportInstant report={a.trafficReport} onExplain={a.explainTrafficItem} onInspect={setInspector} />
          {inspector ? (
            <InspectorPanel target={inspector} explanation={a.trafficExplanation} packets={a.packets} onClose={() => setInspector(null)} />
          ) : (
            <ExplanationPanel explanation={a.trafficExplanation} />
          )}
        </div>
      )}

      {sub === 'daily' && (
        <div style={{ flex: 1, display: 'flex', minHeight: 0 }}>
          <ReportPeriod report={a.trafficReport} onExplain={a.explainTrafficItem} onInspect={setInspector} />
          {inspector ? (
            <InspectorPanel target={inspector} explanation={a.trafficExplanation} packets={a.packets} onClose={() => setInspector(null)} />
          ) : (
            <ExplanationPanel explanation={a.trafficExplanation} />
          )}
        </div>
      )}

      {sub === 'weekly' && (
        <div style={{ flex: 1, display: 'flex', minHeight: 0 }}>
          <ReportPeriod report={a.trafficReport} onExplain={a.explainTrafficItem} onInspect={setInspector} />
          {inspector ? (
            <InspectorPanel target={inspector} explanation={a.trafficExplanation} packets={a.packets} onClose={() => setInspector(null)} />
          ) : (
            <ExplanationPanel explanation={a.trafficExplanation} />
          )}
        </div>
      )}
    </div>
  )
}

function InspectorPanel({ target, explanation, packets, onClose }: { target: InspectorTarget; explanation: string | null; packets: Packet[]; onClose: () => void }) {
  const [sel, setSel] = useState(-1)
  useEffect(() => { setSel(-1) }, [target])
  const matched = useMemo(() => {
    const ips = target.ips.map(x => x.toLowerCase()).filter(Boolean)
    if (ips.length === 0) return []
    return packets
      .filter(pk => ips.includes(pk.src.toLowerCase()) || ips.includes(pk.dst.toLowerCase()))
      .slice(-400)
      .reverse()
  }, [target, packets])
  const selected = sel >= 0 && sel < matched.length ? matched[sel] : null
  return (
    <div style={{ width: 420, flexShrink: 0, borderLeft: '1px solid rgba(255,255,255,.06)', background: 'rgba(8,12,20,.5)', overflowY: 'auto', padding: 20 }}>
      <div style={{ display: 'flex', alignItems: 'flex-start', gap: 10, marginBottom: 4 }}>
        <div style={{ fontFamily: C.mono, fontSize: 11, letterSpacing: '.08em', color: C.textHi, flex: 1, minWidth: 0, wordBreak: 'break-word' }}>{target.title}</div>
        <button onClick={onClose} title="Закрыть" style={{
          flexShrink: 0, width: 22, height: 22, borderRadius: 6, border: 'none', cursor: 'pointer',
          background: 'rgba(255,255,255,.07)', color: C.textMid, fontFamily: C.mono, fontSize: 11,
        }}>✕</button>
      </div>
      <div style={{ fontFamily: C.mono, fontSize: 10, color: C.textDim, marginBottom: 12 }}>
        {target.ips.length === 0
          ? 'IP не указаны — совпадения не ищутся'
          : `Найдено пакетов: ${matched.length}${matched.length >= 400 ? ' (последние 400)' : ''}`}
      </div>
      <div style={{ maxHeight: '38vh', overflowY: 'auto', marginBottom: 16, borderRadius: 8, border: `1px solid ${C.lineSoft}` }}>
        {matched.length === 0 ? (
          <div style={{ fontFamily: C.mono, fontSize: 11, color: C.textDim, padding: '14px 10px', textAlign: 'center' }}>Совпадений нет</div>
        ) : matched.map((pk, i) => {
          const isSel = sel === i
          return (
            <div key={`${pk.no}-${i}`} onClick={() => setSel(i)} style={{
              padding: '6px 10px', cursor: 'pointer',
              borderBottom: '1px solid rgba(255,255,255,.03)',
              background: isSel ? 'rgba(255,107,91,.12)' : i % 2 ? 'rgba(255,255,255,.02)' : 'transparent',
              borderLeft: isSel ? `2px solid ${C.accent}` : '2px solid transparent',
            }}>
              <div style={{ display: 'flex', alignItems: 'baseline', gap: 8, fontFamily: C.mono, fontSize: 10.5 }}>
                <span style={{ color: C.tableDim, flexShrink: 0 }}>{pk.time}</span>
                <span style={{ flex: 1, minWidth: 0, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap', color: C.textMid }}>
                  {pk.src}{pk.sport ? `:${pk.sport}` : ''} → {pk.dst}{pk.dport ? `:${pk.dport}` : ''}
                </span>
                <span style={{ color: protoColor(pk.proto), fontWeight: 600, flexShrink: 0 }}>{pk.proto}</span>
                <span style={{ color: C.tableDim, flexShrink: 0 }}>{pk.len}</span>
              </div>
              <div style={{ fontFamily: C.mono, fontSize: 10, color: C.textDim, marginTop: 2, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>{pk.info}</div>
            </div>
          )
        })}
      </div>
      <div style={{ fontFamily: C.mono, fontSize: 10, letterSpacing: '.18em', color: C.textDim, marginBottom: 14 }}>РАЗБОР ПАКЕТА</div>
      <PacketBreakdown p={selected} />
      {explanation && (
        <>
          <div style={{ fontFamily: C.mono, fontSize: 10, letterSpacing: '.18em', color: C.textDim, margin: '16px 0 10px' }}>ОТВЕТ ИИ</div>
          <div style={{ fontFamily: C.mono, fontSize: 12, color: C.textMid, lineHeight: 1.7, whiteSpace: 'pre-wrap' }}>{explanation}</div>
        </>
      )}
    </div>
  )
}

function ExplanationPanel({ explanation }: { explanation: string | null }) {
  return (
    <div style={{ width: 340, flexShrink: 0, borderLeft: '1px solid rgba(255,255,255,.06)', background: 'rgba(8,12,20,.5)', overflowY: 'auto', padding: 20 }}>
      <div style={{ fontFamily: C.mono, fontSize: 10, letterSpacing: '.18em', color: C.textDim, marginBottom: 14 }}>ОТВЕТ ИИ</div>
      {explanation ? (
        <div style={{ fontFamily: C.mono, fontSize: 12, color: C.textMid, lineHeight: 1.7, whiteSpace: 'pre-wrap' }}>{explanation}</div>
      ) : (
        <div style={{ fontFamily: C.mono, fontSize: 12, color: C.textDim }}>Нажмите «Спросить ИИ» на паттерне или аномалии</div>
      )}
    </div>
  )
}

function Cell({ children, color, bold, ellipsis }: { children: React.ReactNode; color: string; bold?: boolean; ellipsis?: boolean }) {
  return (
    <div style={{
      padding: '0 10px', color, fontWeight: bold ? 600 : 400, lineHeight: `${ROW_H}px`,
      ...(ellipsis ? { overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' as const } : null),
    }}>{children}</div>
  )
}

const btnStyle: React.CSSProperties = ui.s.btn

const testBtnStyle = (t: string): React.CSSProperties => ({
  padding: '4px 10px', borderRadius: 5, border: 'none', cursor: 'pointer',
  fontFamily: "'Chakra Petch', sans-serif", fontSize: 10.5, transition: 'all .2s',
  background: t === 'ping' ? 'rgba(57,217,138,.15)' : t === 'http' ? 'rgba(63,214,255,.15)' : t === 'osc' ? 'rgba(168,130,255,.15)' : t === 'artnet' ? 'rgba(255,107,91,.15)' : 'rgba(255,255,255,.08)',
  color: t === 'ping' ? C.green : t === 'http' ? C.cyan : t === 'osc' ? '#a882ff' : t === 'artnet' ? '#ff6b5b' : '#ccc',
})
