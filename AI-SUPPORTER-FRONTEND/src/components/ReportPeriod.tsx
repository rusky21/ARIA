import { C, cyanA, w } from '../lib/theme.js'
import type { TrafficReport, InspectorTarget } from '../lib/data.js'

interface Props {
  report: TrafficReport | null;
  onExplain: (context: string, relatedIp?: string, explainType?: string) => void;
  onInspect?: (t: InspectorTarget) => void;
}

const fmtBytes = (b: number) => {
  if (b < 1024) return `${b} B`
  if (b < 1048576) return `${(b / 1024).toFixed(1)} KB`
  return `${(b / 1048576).toFixed(1)} MB`
}

const fmtTime = (iso: string) => {
  try { return new Date(iso).toLocaleString('ru-RU', { day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit' }) }
  catch { return iso }
}

const changeStr = (cur: number, prev: number): { text: string; color: string } => {
  if (prev === 0) return { text: 'нет данных', color: C.textDim }
  const pct = ((cur - prev) / prev * 100)
  const sign = pct >= 0 ? '+' : ''
  return { text: `${sign}${pct.toFixed(0)}%`, color: pct > 10 ? C.red : pct < -10 ? C.green : C.textDim }
}

export default function ReportPeriod({ report, onExplain, onInspect }: Props) {
  if (!report) {
    return (
      <div style={{ flex: 1, display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
        <div style={{ fontFamily: C.mono, fontSize: 12, color: C.textDim, textAlign: 'center' }}>
          <div style={{ marginBottom: 12, fontSize: 16, opacity: .4 }}>&#x25C8;</div>
          Нажмите «Анализ» для формирования отчёта
        </div>
      </div>
    )
  }

  const label = report.reportType === 'daily' ? 'СУТОЧНЫЙ' : 'НЕДЕЛЬНЫЙ'
  const maxPkt = Math.max(...report.protocols.map(p => p.count), 1)
  const maxTsPkts = Math.max(...report.timeSeries.map(t => t.packetCount), 1)
  const cmp = report.comparison

  return (
    <div style={{ flex: 1, overflowY: 'auto', padding: 20 }}>
      <div style={{ fontFamily: C.mono, fontSize: 10, letterSpacing: '.15em', color: C.textDim, marginBottom: 14 }}>
        {label} ОТЧЁТ · {fmtTime(report.periodStart)} — {fmtTime(report.periodEnd)} · {report.totalPackets} пакетов · {fmtBytes(report.totalBytes)}
      </div>

      {/* Comparison */}
      {cmp && (
        <div style={{ display: 'flex', gap: 16, marginBottom: 18 }}>
          <CompCard label="Пакеты" cur={cmp.currentPackets} prev={cmp.previousPackets} />
          <CompCard label="Соединения" cur={cmp.currentFlows} prev={cmp.previousFlows} />
        </div>
      )}

      {/* LLM Summary */}
      {report.llmSummary && (
        <div style={{ padding: 16, borderRadius: 10, background: cyanA(0.06), border: `1px solid ${cyanA(0.2)}`, marginBottom: 18 }}>
          <div style={{ fontFamily: C.mono, fontSize: 10, letterSpacing: '.12em', color: C.cyan, marginBottom: 8 }}>ИИ-АНАЛИЗ</div>
          <div style={{ fontFamily: C.mono, fontSize: 12, color: C.textMid, lineHeight: 1.7, whiteSpace: 'pre-wrap' }}>{report.llmSummary}</div>
        </div>
      )}

      {/* Protocol breakdown */}
      <Section title="Протоколы">
        {report.protocols.length === 0 && <Empty />}
        {report.protocols.map(p => (
          <div key={p.protocol} style={{ display: 'flex', alignItems: 'center', gap: 10, marginBottom: 6 }}>
            <span style={{ width: 80, fontFamily: C.mono, fontSize: 11, color: C.textMid, textAlign: 'right' }}>{p.protocol}</span>
            <div style={{ flex: 1, height: 6, borderRadius: 3, background: w(0.06), overflow: 'hidden' }}>
              <div style={{ width: `${Math.max(4, (p.count / maxPkt) * 100)}%`, height: '100%', background: C.accent, borderRadius: 3 }} />
            </div>
            <span style={{ width: 50, fontFamily: C.mono, fontSize: 10, color: C.textDim }}>{p.count} <span style={{ opacity: .5 }}>({p.percentage.toFixed(0)}%)</span></span>
          </div>
        ))}
      </Section>

      {/* Time series */}
      {report.timeSeries.length > 0 && (
        <Section title="Активность по времени">
          <div style={{ display: 'flex', alignItems: 'flex-end', gap: 2, height: 60, padding: '0 4px' }}>
            {report.timeSeries.map((t, i) => (
              <div key={i} title={`${fmtTime(t.timestamp)}: ${t.packetCount} пк / ${fmtBytes(t.totalBytes)}`}
                style={{ flex: 1, minWidth: 4, height: `${Math.max(4, (t.packetCount / maxTsPkts) * 100)}%`,
                  background: C.accent, borderRadius: '2px 2px 0 0', opacity: .7 }} />
            ))}
          </div>
        </Section>
      )}

      {/* Top flows */}
      <Section title="Топ соединения">
        {report.topFlows.length === 0 && <Empty />}
        <TableHead cols={['Источник', 'Назначение', 'Proto', 'Пакеты', 'Байты']} widths={['1.4fr', '1.4fr', '70px', '70px', '80px']} />
        {report.topFlows.slice(0, 10).map((f, i) => (
          <div key={i} onClick={() => onInspect?.({ title: `${f.srcIp}:${f.srcPort} ⇄ ${f.dstIp}:${f.dstPort} · ${f.protocol}`, ips: [f.srcIp, f.dstIp] })}
            style={{ display: 'grid', gridTemplateColumns: '1.4fr 1.4fr 70px 70px 80px', padding: '5px 10px', fontFamily: C.mono, fontSize: 11, color: C.textMid, background: i % 2 ? w(0.015) : 'transparent', cursor: onInspect ? 'pointer' : 'default', borderRadius: 4 }}
            onMouseEnter={e => { if (onInspect) e.currentTarget.style.background = cyanA(0.08) }}
            onMouseLeave={e => { e.currentTarget.style.background = i % 2 ? w(0.015) : 'transparent' }}>
            <span>{f.srcIp}:{f.srcPort}</span>
            <span>{f.dstIp}:{f.dstPort}</span>
            <span style={{ color: C.accent }}>{f.protocol}</span>
            <span>{f.packetCount}</span>
            <span style={{ color: C.textDim }}>{fmtBytes(f.totalBytes)}</span>
          </div>
        ))}
      </Section>

      {/* Top hosts */}
      <Section title="Топ хосты">
        {report.topHosts.length === 0 && <Empty />}
        <TableHead cols={['IP', 'Имя', 'Отправлено', 'Получено', 'Уник. назн.']} widths={['1.2fr', '1fr', '80px', '80px', '80px']} />
        {report.topHosts.slice(0, 10).map((h, i) => (
          <div key={i} style={{ display: 'grid', gridTemplateColumns: '1.2fr 1fr 80px 80px 80px', padding: '5px 10px', fontFamily: C.mono, fontSize: 11, color: C.textMid, background: i % 2 ? w(0.015) : 'transparent' }}>
            <span>{h.ip}</span>
            <span style={{ color: C.textDim }}>{h.name || '—'}</span>
            <span>{h.sentPackets}</span>
            <span>{h.receivedPackets}</span>
            <span>{h.uniqueDestinations}</span>
          </div>
        ))}
      </Section>

      {/* Anomalies */}
      {report.anomalies.length > 0 && (
        <Section title="Аномалии">
          {report.anomalies.map(a => (
            <Card key={a.id} accent={sevColor(a.severity)} onClick={() => onInspect?.({ title: a.description, ips: a.relatedIp ? [a.relatedIp] : [] })}>
              <div style={{ display: 'flex', alignItems: 'center', gap: 8, marginBottom: 4 }}>
                <span style={{ padding: '2px 7px', borderRadius: 4, fontFamily: C.mono, fontSize: 9, background: sevBg(a.severity), color: sevColor(a.severity) }}>
                  {a.type.toUpperCase()} · SEV {a.severity}
                </span>
                <span style={{ fontFamily: C.mono, fontSize: 10, color: C.textDim }}>{fmtTime(a.timestamp)}</span>
                <AskAiBtn onClick={() => onExplain(`Аномалия: ${a.description}. Тип: ${a.type}. Severity: ${a.severity}`, a.relatedIp ?? undefined, 'anomaly')} />
              </div>
              <div style={{ fontFamily: C.mono, fontSize: 11, color: C.textMid }}>{a.description}</div>
              {a.relatedIp && <div style={{ fontFamily: C.mono, fontSize: 10, color: C.textDim, marginTop: 3 }}>IP: {a.relatedIp}</div>}
            </Card>
          ))}
        </Section>
      )}

      {/* Command patterns */}
      {report.commandPatterns.length > 0 && (
        <Section title="Паттерны команд">
          {report.commandPatterns.map((cp, i) => (
            <Card key={i} accent="#a882ff" onClick={() => onInspect?.({ title: cp.description, ips: cp.relatedIps })}>
              <div style={{ display: 'flex', alignItems: 'center', gap: 8, marginBottom: 4 }}>
                <span style={{ padding: '2px 7px', borderRadius: 4, fontFamily: C.mono, fontSize: 9, background: 'rgba(168,130,255,.15)', color: C.purple }}>
                  {cp.occurrences}× · {cp.intervalMs > 0 ? `${(cp.intervalMs / 1000).toFixed(1)}с` : 'без интервала'}
                </span>
                <AskAiBtn onClick={() => onExplain(`Паттерн команд: ${cp.signature}. Повторений: ${cp.occurrences}. Интервал: ${cp.intervalMs}мс. ${cp.description}`, undefined, 'command_pattern')} />
              </div>
              <div style={{ fontFamily: C.mono, fontSize: 11, color: C.textMid, marginBottom: 3 }}>{cp.description}</div>
              <div style={{ fontFamily: C.mono, fontSize: 10, color: C.textDim, wordBreak: 'break-all' }}>{cp.signature}</div>
              {cp.relatedIps.length > 0 && (
                <div style={{ fontFamily: C.mono, fontSize: 10, color: C.textDim, marginTop: 3 }}>IP: {cp.relatedIps.join(', ')}</div>
              )}
            </Card>
          ))}
        </Section>
      )}
    </div>
  )
}

function CompCard({ label, cur, prev }: { label: string; cur: number; prev: number }) {
  const ch = changeStr(cur, prev)
  return (
    <div style={{ flex: 1, padding: 12, borderRadius: 8, background: C.panelAlt, border: `1px solid ${C.line}` }}>
      <div style={{ fontFamily: C.mono, fontSize: 9, letterSpacing: '.12em', color: C.textDim, marginBottom: 6 }}>{label}</div>
      <div style={{ display: 'flex', alignItems: 'baseline', gap: 8 }}>
        <span style={{ fontFamily: C.mono, fontSize: 18, color: C.textMid }}>{cur.toLocaleString('ru-RU')}</span>
        <span style={{ fontFamily: C.mono, fontSize: 11, color: ch.color }}>{ch.text}</span>
      </div>
    </div>
  )
}

const sevColor = (s: number) => s >= 3 ? C.red : s >= 2 ? C.amber : C.green
const sevBg = (s: number) => s >= 3 ? 'rgba(255,84,112,.15)' : s >= 2 ? 'rgba(255,184,77,.15)' : 'rgba(57,217,138,.15)'

function Section({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <div style={{ marginBottom: 18 }}>
      <div style={{ fontFamily: C.mono, fontSize: 10, letterSpacing: '.12em', color: C.textDim, marginBottom: 8 }}>{title}</div>
      {children}
    </div>
  )
}

function Card({ accent, onClick, children }: { accent: string; onClick?: () => void; children: React.ReactNode }) {
  return (
    <div onClick={onClick} style={{ padding: 12, borderRadius: 8, background: C.panelAlt, border: `1px solid ${C.line}`, borderLeft: `3px solid ${accent}`, marginBottom: 8, cursor: onClick ? 'pointer' : 'default' }}>
      {children}
    </div>
  )
}

function TableHead({ cols, widths }: { cols: string[]; widths: string[] }) {
  return (
    <div style={{ display: 'grid', gridTemplateColumns: widths.join(' '), padding: '4px 10px', fontFamily: C.mono, fontSize: 9, letterSpacing: '.1em', color: C.textDim, borderBottom: `1px solid ${C.line}`, marginBottom: 4 }}>
      {cols.map(c => <span key={c}>{c}</span>)}
    </div>
  )
}

function AskAiBtn({ onClick }: { onClick: () => void }) {
  return (
    <button onClick={e => { e.stopPropagation(); onClick(); }}
      style={{ marginLeft: 'auto', padding: '2px 8px', borderRadius: 4, border: 'none', cursor: 'pointer',
        fontFamily: C.mono, fontSize: 9, background: cyanA(0.15), color: C.cyan }}>
      Спросить ИИ
    </button>
  )
}

function Empty() {
  return <div style={{ fontFamily: C.mono, fontSize: 11, color: C.textDim }}>Нет данных</div>
}
