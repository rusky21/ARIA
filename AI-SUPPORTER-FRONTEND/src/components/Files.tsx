import { useEffect } from 'react';
import { C, acc, w } from '../lib/theme.js';
import { Icon } from './Icon.jsx';
import type { AssistantState } from '../hooks/useAssistant.js';

const fmtSize = (n: number): string => {
  if (n < 1024) return `${n} B`;
  if (n < 1024 * 1024) return `${(n / 1024).toFixed(1)} КБ`;
  if (n < 1024 * 1024 * 1024) return `${(n / 1024 / 1024).toFixed(1)} МБ`;
  return `${(n / 1024 / 1024 / 1024).toFixed(2)} ГБ`;
};

export default function Files({ a }: { a: AssistantState }) {
  const { fsEntries, fsPath, fsParent, fsContent, fsReadPath, fsLoading, fsList, fsRead } = a;

  useEffect(() => {
    fsList(undefined);
  }, []);

  return (
    <div style={{ position: 'absolute', inset: 0, display: 'flex', animation: 'ariaFadeFast .22s ease both' }}>
      {/* ─── Left File Explorer Panel ─── */}
      <aside style={{
        width: 320,
        flexShrink: 0,
        borderRight: '1px solid rgba(255,255,255,0.06)',
        background: 'rgba(6, 8, 13, 0.65)',
        backdropFilter: 'blur(20px)',
        overflowY: 'auto',
        padding: '18px 14px',
        display: 'flex',
        flexDirection: 'column',
      }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: 10, marginBottom: 14, padding: '0 6px' }}>
          <span style={{ fontFamily: C.mono, fontSize: 10, letterSpacing: '.18em', color: 'rgba(255,255,255,0.4)', fontWeight: 700 }}>
            ФАЙЛОВАЯ СИСТЕМА
          </span>
          <button
            onClick={() => fsList(fsPath)}
            title="Обновить список"
            style={{
              marginLeft: 'auto',
              display: 'inline-flex',
              alignItems: 'center',
              justifyContent: 'center',
              background: 'rgba(255,255,255,0.04)',
              border: '1px solid rgba(255,255,255,0.08)',
              borderRadius: 6,
              padding: '5px 8px',
              cursor: 'pointer',
              color: '#94a3b8',
              transition: 'all .18s',
            }}
            onMouseEnter={(e) => {
              e.currentTarget.style.background = 'rgba(255,255,255,0.09)';
              e.currentTarget.style.color = '#fff';
            }}
            onMouseLeave={(e) => {
              e.currentTarget.style.background = 'rgba(255,255,255,0.04)';
              e.currentTarget.style.color = '#94a3b8';
            }}
          >
            <Icon name="refresh" size={12} />
          </button>
        </div>

        {fsParent && (
          <div
            onClick={() => fsList(fsParent)}
            style={{
              display: 'flex',
              alignItems: 'center',
              gap: 8,
              padding: '8px 10px',
              borderRadius: 8,
              cursor: 'pointer',
              color: '#38bdf8',
              fontFamily: C.mono,
              fontSize: 12,
              marginBottom: 4,
              transition: 'background .15s',
            }}
            onMouseEnter={(e) => (e.currentTarget.style.background = 'rgba(56,189,248,0.1)')}
            onMouseLeave={(e) => (e.currentTarget.style.background = 'transparent')}
          >
            <span>←</span>
            <span>.. (на уровень вверх)</span>
          </div>
        )}

        <div style={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
          {fsEntries.map((f) => {
            const isSelected = fsReadPath === f.path;
            return (
              <div
                key={f.path}
                onClick={() => (f.isDir ? fsList(f.path) : fsRead(f.path))}
                style={{
                  display: 'flex',
                  alignItems: 'center',
                  gap: 10,
                  padding: '8px 12px',
                  borderRadius: 10,
                  cursor: 'pointer',
                  background: isSelected
                    ? 'linear-gradient(90deg, rgba(255,107,91,0.18) 0%, rgba(255,107,91,0.04) 100%)'
                    : 'transparent',
                  border: isSelected ? '1px solid rgba(255,107,91,0.3)' : '1px solid transparent',
                  transition: 'all .15s',
                }}
                onMouseEnter={(e) => {
                  if (!isSelected) e.currentTarget.style.background = 'rgba(255,255,255,0.04)';
                }}
                onMouseLeave={(e) => {
                  if (!isSelected) e.currentTarget.style.background = 'transparent';
                }}
              >
                <Icon
                  name={f.isDir ? 'folder' : 'file'}
                  color={f.isDir ? '#38bdf8' : isSelected ? '#ff8a7a' : 'rgba(255,255,255,0.4)'}
                  size={16}
                />
                <span style={{
                  fontFamily: C.mono,
                  fontSize: 12,
                  color: f.isDir ? '#38bdf8' : isSelected ? '#fff' : '#cbd5e1',
                  overflow: 'hidden',
                  textOverflow: 'ellipsis',
                  whiteSpace: 'nowrap',
                  flex: 1,
                }}>
                  {f.name}
                </span>
                {!f.isDir && (
                  <span style={{ fontFamily: C.mono, fontSize: 10, color: 'rgba(255,255,255,0.35)', flexShrink: 0 }}>
                    {fmtSize(f.size)}
                  </span>
                )}
              </div>
            );
          })}
        </div>

        {!fsLoading && fsEntries.length === 0 && (
          <div style={{ fontFamily: C.mono, fontSize: 11, color: C.textDim, textAlign: 'center', marginTop: 40 }}>
            Директория пуста
          </div>
        )}
      </aside>

      {/* ─── Right File Preview Pane ─── */}
      <div style={{ flex: 1, display: 'flex', flexDirection: 'column', minWidth: 0, background: 'rgba(5, 8, 14, 0.4)' }}>
        {/* File Header */}
        <div style={{
          padding: '12px 24px',
          borderBottom: '1px solid rgba(255,255,255,0.06)',
          display: 'flex',
          alignItems: 'center',
          gap: 14,
          background: 'rgba(6, 8, 13, 0.45)',
          backdropFilter: 'blur(10px)',
        }}>
          <span style={{
            fontFamily: C.mono,
            fontSize: 12,
            fontWeight: 600,
            color: '#f8fafc',
            overflow: 'hidden',
            textOverflow: 'ellipsis',
            whiteSpace: 'nowrap',
          }}>
            {fsReadPath || fsPath || '/'}
          </span>

          {fsReadPath && fsContent && (
            <span style={{
              fontFamily: C.mono,
              fontSize: 10,
              fontWeight: 600,
              padding: '2px 8px',
              borderRadius: 6,
              background: 'rgba(57,217,138,0.12)',
              color: '#39d98a',
              border: '1px solid rgba(57,217,138,0.25)',
            }}>
              ● {fmtSize(fsContent.size)}
            </span>
          )}

          {fsLoading && (
            <span style={{ fontFamily: C.mono, fontSize: 11, color: '#38bdf8' }}>
              загрузка содержимого…
            </span>
          )}
        </div>

        {/* Content Viewer */}
        <div style={{ flex: 1, overflowY: 'auto', padding: '20px 24px', fontFamily: C.mono, fontSize: 12.5, lineHeight: 1.7 }}>
          {!fsReadPath && !fsLoading && (
            <div style={{ color: 'rgba(126,139,160,0.6)', textAlign: 'center', marginTop: 60 }}>
              {fsEntries.length === 0 ? 'Выберите файл' : 'Выберите файл в левой панели для просмотра содержимого'}
            </div>
          )}

          {fsReadPath && fsContent?.binary && (
            <div style={{ color: 'rgba(126,139,160,0.8)', textAlign: 'center', marginTop: 60 }}>
              Бинарный файл ({fmtSize(fsContent.size)}) — текстовый просмотр недоступен
            </div>
          )}

          {fsReadPath && fsContent && !fsContent.binary && (
            <div style={{
              background: 'rgba(4, 7, 12, 0.85)',
              border: '1px solid rgba(255,255,255,0.06)',
              borderRadius: 12,
              padding: '18px 20px',
              boxShadow: '0 8px 32px rgba(0,0,0,0.4)',
            }}>
              {fsContent.truncated && (
                <div style={{
                  color: '#ffb454',
                  fontFamily: C.mono,
                  fontSize: 11,
                  marginBottom: 12,
                  padding: '6px 12px',
                  borderRadius: 6,
                  background: 'rgba(255,180,84,0.1)',
                  border: '1px solid rgba(255,180,84,0.25)',
                }}>
                  Показан хвост файла ({fmtSize(fsContent.size)} всего).
                </div>
              )}
              <pre style={{ margin: 0, whiteSpace: 'pre-wrap', wordBreak: 'break-word', color: '#cbd5e1' }}>
                {fsContent.content}
              </pre>
            </div>
          )}

          {fsReadPath && fsContent === null && !fsLoading && (
            <div style={{ color: 'rgba(126,139,160,0.6)', textAlign: 'center', marginTop: 60 }}>
              Файл пуст
            </div>
          )}
        </div>
      </div>
    </div>
  );
}
