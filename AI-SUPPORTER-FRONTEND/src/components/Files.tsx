import { useEffect } from 'react'
import { C, acc, w } from '../lib/theme.js'
import { Icon } from './Icon.jsx'
import type { AssistantState } from '../hooks/useAssistant.js'

const fmtSize = (n: number): string => {
  if (n < 1024) return `${n} B`
  if (n < 1024 * 1024) return `${(n / 1024).toFixed(1)} КБ`
  if (n < 1024 * 1024 * 1024) return `${(n / 1024 / 1024).toFixed(1)} МБ`
  return `${(n / 1024 / 1024 / 1024).toFixed(2)} ГБ`
}

export default function Files({ a }: { a: AssistantState }) {
  const { fsEntries, fsPath, fsParent, fsContent, fsReadPath, fsLoading, fsList, fsRead } = a

  useEffect(() => { fsList(undefined) }, [])

  return (
    <div style={{ position: 'absolute', inset: 0, display: 'flex', animation: 'ariaFadeFast .22s ease both' }}>
      <div style={{ width: 300, flexShrink: 0, borderRight: `1px solid rgba(255,255,255,.06)`, overflowY: 'auto', padding: 18 }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: 10, marginBottom: 12 }}>
          <span style={{ fontFamily: C.mono, fontSize: 10, letterSpacing: '.18em', color: C.textDim }}>ФАЙЛОВАЯ СИСТЕМА</span>
          <button onClick={() => fsList(fsPath)} title="Обновить" style={{ marginLeft: 'auto', display: 'inline-flex', alignItems: 'center', justifyContent: 'center', background: w(0.08), border: 'none', borderRadius: 6, padding: '4px 8px', cursor: 'pointer', color: C.textMid }}>
            <Icon name="refresh" size={12} />
          </button>
        </div>
        {fsParent && (
          <div onClick={() => fsList(fsParent)} style={{ display: 'flex', alignItems: 'center', gap: 8, padding: '7px 10px', borderRadius: 8, cursor: 'pointer', color: C.textDim, fontFamily: C.mono, fontSize: 12 }}>
            <span>←</span><span>.. (вверх)</span>
          </div>
        )}
        {fsEntries.map((f) => (
          <div key={f.path} onClick={() => f.isDir ? fsList(f.path) : fsRead(f.path)} style={{
            display: 'flex', alignItems: 'center', gap: 10, padding: '7px 10px', borderRadius: 8, cursor: 'pointer',
            background: fsReadPath === f.path ? acc(0.08) : 'transparent',
          }}>
            <Icon name={f.isDir ? 'folder' : 'file'} color={f.isDir ? C.cyan : fsReadPath === f.path ? C.accent : C.textFaint} size={17} />
            <span style={{ fontFamily: C.mono, fontSize: 12, color: f.isDir ? C.cyan : C.textMid, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>{f.name}</span>
            <span style={{ marginLeft: 'auto', fontFamily: C.mono, fontSize: 10, color: C.tableDim, flexShrink: 0 }}>
              {f.isDir ? '' : fmtSize(f.size)}
            </span>
          </div>
        ))}
        {!fsLoading && fsEntries.length === 0 && (
          <div style={{ fontFamily: C.mono, fontSize: 11, color: C.textDim, textAlign: 'center', marginTop: 30 }}>Пусто</div>
        )}
      </div>

      <div style={{ flex: 1, display: 'flex', flexDirection: 'column', minWidth: 0 }}>
        <div style={{ padding: '13px 20px', borderBottom: `1px solid rgba(255,255,255,.06)`, display: 'flex', alignItems: 'center', gap: 14 }}>
          <span style={{ fontFamily: C.mono, fontSize: 12, color: C.textMid, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
            {fsReadPath || fsPath || '/'}
          </span>
          {fsReadPath && fsContent && (
            <span style={{ fontFamily: C.mono, fontSize: 10, color: C.green }}>● {fmtSize(fsContent.size)}</span>
          )}
          {fsLoading && <span style={{ fontFamily: C.mono, fontSize: 10, color: C.textDim }}>загрузка…</span>}
        </div>
        <div style={{ flex: 1, overflowY: 'auto', padding: '14px 20px', fontFamily: C.mono, fontSize: 12 }}>
          {!fsReadPath && !fsLoading && (
            <div style={{ color: C.textDim, textAlign: 'center', marginTop: 40 }}>
              {fsEntries.length === 0 ? 'Выберите файл' : 'Выберите файл, чтобы посмотреть содержимое'}
            </div>
          )}
          {fsReadPath && fsContent?.binary && (
            <div style={{ color: C.textDim, textAlign: 'center', marginTop: 40 }}>
              Бинарный файл, {fmtSize(fsContent.size)} — просмотр содержимого недоступен
            </div>
          )}
          {fsReadPath && fsContent && !fsContent.binary && (
            <pre style={{ margin: 0, whiteSpace: 'pre-wrap', wordBreak: 'break-word', color: C.textMid, lineHeight: 1.55 }}>
              {fsContent.truncated && <div style={{ color: C.amber, marginBottom: 10 }}>Показан хвост файла ({fmtSize(fsContent.size)} всего).</div>}
              {fsContent.content}
            </pre>
          )}
          {fsReadPath && fsContent === null && (
            <div style={{ color: C.textDim, textAlign: 'center', marginTop: 40 }}>Файл пуст</div>
          )}
        </div>
      </div>
    </div>
  )
}
