import { useState, useEffect } from 'react';
import { C, w, R, acc } from '../lib/theme.js';
import { Icon } from './Icon.jsx';
import type { AssistantState } from '../hooks/useAssistant.js';
import type { DevHunk } from '../lib/data.js';
import { UserMsg, AssistantMsg, ThinkingRow, GptComposer, useStickToBottom } from './gpt.js';
import BrowserPanel from './BrowserPanel.js';
import ChatSidebar from './ChatSidebar.js';

export default function Dev({ a }: { a: AssistantState }) {
  const [pw, setPw] = useState('');
  const [showChange, setShowChange] = useState(false);
  const [curPw, setCurPw] = useState('');
  const [newPw, setNewPw] = useState('');
  const [expanded, setExpanded] = useState<Set<string>>(new Set());
  const [browse, setBrowse] = useState<string | null>(null);
  const [rightTab, setRightTab] = useState<'changes' | 'reasoning'>('changes');
  const { ref } = useStickToBottom([a.devMessages, a.devThinking]);

  useEffect(() => {
    if (a.devThinking) setRightTab('reasoning');
  }, [a.devThinking]);

  if (!a.devAuthed) {
    return (
      <div style={{ position: 'absolute', inset: 0, display: 'flex', alignItems: 'center', justifyContent: 'center', animation: 'ariaFadeFast .22s ease both' }}>
        <div style={{ width: 380, background: C.panelAlt, border: `1px solid ${w(0.1)}`, borderRadius: R.xl, padding: 36, backdropFilter: 'blur(14px)' }}>
          <div style={{ display: 'flex', alignItems: 'center', gap: 14, marginBottom: 22 }}>
            <div style={{ width: 46, height: 46, borderRadius: R.xl, border: `1px solid ${acc(0.35)}`, background: acc(0.12), display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
              <Icon name="shield" color={C.accent} size={24} />
            </div>
            <div>
              <div style={{ fontSize: 19, fontWeight: 600, letterSpacing: '.04em' }}>Режим разработчика</div>
              <div style={{ fontFamily: C.mono, fontSize: 11, color: C.textDim, marginTop: 3 }}>DEV SHELL · PROTECTED</div>
            </div>
          </div>

          <div style={{ fontFamily: C.mono, fontSize: 11, color: C.textMid, letterSpacing: '.08em', marginBottom: 10 }}>ПАРОЛЬ ДОСТУПА</div>
          <input
            type="password"
            value={pw}
            onChange={(e) => setPw(e.target.value)}
            onKeyDown={(e) => e.key === 'Enter' && a.devAuth(pw)}
            placeholder="••••••••••"
            autoFocus
            style={inputStyle}
          />
          <button onClick={() => a.devAuth(pw)} style={accentBtn} onMouseEnter={(e) => (e.currentTarget.style.filter = 'brightness(1.1)')} onMouseLeave={(e) => (e.currentTarget.style.filter = 'none')}>
            Войти в Dev Shell
          </button>

          {a.devAuthError && <div style={{ marginTop: 12, fontFamily: C.mono, fontSize: 12, color: C.accent }}>{a.devAuthError}</div>}
        </div>
      </div>
    );
  }

  if (showChange) {
    return (
      <div style={{ position: 'absolute', inset: 0, display: 'flex', alignItems: 'center', justifyContent: 'center', animation: 'ariaFadeFast .22s ease both' }}>
        <div style={{ width: 400, background: C.panelAlt, border: `1px solid ${w(0.1)}`, borderRadius: R.xl, padding: 36, backdropFilter: 'blur(14px)' }}>
          <div style={{ fontSize: 19, fontWeight: 600, letterSpacing: '.04em', marginBottom: 6 }}>Смена пароля</div>
          <div style={{ fontFamily: C.mono, fontSize: 11, color: C.textDim, marginBottom: 22 }}>Вы используете пароль по умолчанию — задайте свой.</div>
          <div style={{ fontFamily: C.mono, fontSize: 11, color: C.textMid, letterSpacing: '.08em', marginBottom: 8 }}>ТЕКУЩИЙ ПАРОЛЬ</div>
          <input type="password" value={curPw} onChange={(e) => setCurPw(e.target.value)} placeholder="Текущий пароль" style={inputStyle} />
          <div style={{ fontFamily: C.mono, fontSize: 11, color: C.textMid, letterSpacing: '.08em', margin: '18px 0 8px' }}>НОВЫЙ ПАРОЛЬ</div>
          <input type="password" value={newPw} onChange={(e) => setNewPw(e.target.value)} onKeyDown={(e) => e.key === 'Enter' && newPw.trim() && (a.devChangePassword(curPw, newPw), setShowChange(false))} placeholder="Минимум 4 символа" style={inputStyle} />
          <div style={{ display: 'flex', gap: 10, marginTop: 22 }}>
            <button onClick={() => { a.devChangePassword(curPw, newPw); setShowChange(false); }} style={{ ...accentBtn, flex: 1 }}>Сохранить</button>
            <button onClick={() => setShowChange(false)} style={ghostBtn}>Позже</button>
          </div>
        </div>
      </div>
    );
  }

  const toggleFile = (path: string) => {
    setExpanded((prev) => {
      const next = new Set(prev);
      if (next.has(path)) next.delete(path);
      else next.add(path);
      return next;
    });
  };

  return (
    <div style={{ position: 'absolute', inset: 0, display: 'flex', animation: 'ariaFadeFast .22s ease both' }}>
      {/* СЛЕВА: ChatSidebar 260px */}
      <ChatSidebar conversations={a.conversations} active={a.activeConversation}
        onSelect={a.switchChat} onCreate={a.createChat} onDelete={a.deleteChat} />

      {/* ЦЕНТР: DevMessages + Composer */}
      <div style={{ flex: 1, display: 'flex', flexDirection: 'column', minWidth: 0 }}>
        <div style={devBar}>
          <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
            <Icon name="shield" color={C.accent} size={16} />
            <span style={{ fontFamily: C.mono, fontSize: 11, color: C.textMid, letterSpacing: '.12em' }}>DEV SHELL</span>
          </div>
          <div style={{ display: 'flex', gap: 10, alignItems: 'center' }}>
            <label style={{ display: 'flex', alignItems: 'center', gap: 7, fontFamily: C.mono, fontSize: 11, color: C.textDim }}>
              АГЕНТ
              <select value={a.devAgent} onChange={(e) => a.setDevAgent(e.target.value)} style={selStyle}>
                {(a.devAgents.length > 0 ? a.devAgents : [{ id: 'build', type: 'primary' }]).map((ag) => (
                  <option key={ag.id} value={ag.id}>{ag.id}</option>
                ))}
              </select>
            </label>
            <label style={{ display: 'flex', alignItems: 'center', gap: 7, fontFamily: C.mono, fontSize: 11, color: C.textDim }}>
              МОДЕЛЬ
              <select value={a.devModel} onChange={(e) => a.setDevModel(e.target.value)} style={selStyle}>
                <option value="">по умолчанию</option>
                {a.devModels.map((m) => (
                  <option key={m} value={m}>{m.split('/').pop()}</option>
                ))}
              </select>
            </label>
            <button onClick={() => setShowChange(true)} style={smallBtn} title="Сменить пароль"><Icon name="settings" color={C.tableDim} size={15} /></button>
            <button onClick={a.devNewChat} style={smallBtn}>Новый чат</button>
          </div>
        </div>

        <div ref={ref} style={{ flex: 1, overflowY: 'auto', padding: '26px 0 10px' }}>
          <div style={{ maxWidth: 820, margin: '0 auto', padding: '0 24px', display: 'flex', flexDirection: 'column', gap: 26 }}>
            {a.devMessages.length === 0 && !a.devThinking && (
              <div style={{ textAlign: 'center', marginTop: '15vh' }}>
                <div style={{ fontSize: 30, fontWeight: 700, color: C.textHi }}>Опишите задачу</div>
                <div style={{ marginTop: 12, fontFamily: C.mono, fontSize: 12, color: C.textDim, lineHeight: 2 }}>
                  opencode проанализирует проект и предложит изменения.
                  <br />Применяются только после подтверждения.
                </div>
              </div>
            )}
            {a.devMessages.map((m, i) =>
              m.role === 'user' ? (
                <UserMsg key={i} text={m.text} />
              ) : (
                <AssistantMsg key={i} text={m.text} onLink={(url) => setBrowse(url)} />
              ),
            )}
            {a.devThinking && <ThinkingRow label={`opencode работает… ${a.devThinkingText || ''}`} />}
          </div>
        </div>

        <GptComposer
          value={a.devInput}
          onChange={a.setDevInput}
          onSend={a.devSendMsg}
          placeholder="Что нужно сделать с кодом?"
        />
      </div>

      {/* СПРАВА: колонка 420px */}
      <div style={{ width: 420, flexShrink: 0, display: 'flex', flexDirection: 'column',
        borderLeft: `1px solid ${w(0.06)}`, background: C.bgCrust }}>

        {/* Браузер — верх 45% */}
        <div style={{ flex: '0 0 45%', borderBottom: `1px solid ${w(0.06)}`,
          display: 'flex', flexDirection: 'column' }}>
          <BrowserPanel url={browse} />
        </div>

        {/* DevPanel — низ 55% */}
        <div style={{ flex: '0 0 55%', display: 'flex', flexDirection: 'column', overflow: 'hidden' }}>
          {/* Табы ИЗМЕНЕНИЯ/РЕАЛИЗАЦИЯ */}
          <div style={{ display: 'flex', borderBottom: `1px solid ${w(0.06)}` }}>
            <button onClick={() => setRightTab('changes')} style={tabBtn(rightTab === 'changes')}>
              ИЗМЕНЕНИЯ {a.devChanges ? `(${a.devChanges.length})` : ''}
            </button>
            <button onClick={() => setRightTab('reasoning')} style={tabBtn(rightTab === 'reasoning')}>
              РЕАЛИЗАЦИЯ
            </button>
          </div>

          {/* Контент вкладок — как сейчас */}
          {rightTab === 'changes' && (
            <>
              <div style={{ flex: 1, overflowY: 'auto', padding: 12 }}>
                {!a.devChanges || a.devChanges.length === 0 ? (
                  <div style={{ fontFamily: C.mono, fontSize: 11, color: C.textDim, textAlign: 'center', marginTop: 60, lineHeight: 2 }}>
                    Нет предложенных изменений.
                    <br />Опишите задачу в чате.
                  </div>
                ) : (
                  a.devChanges.map((f) => {
                    const open = expanded.has(f.path);
                    return (
                      <div key={f.path} style={{ border: '1px solid rgba(63,214,255,.16)', borderRadius: 12, marginBottom: 10, overflow: 'hidden', background: 'rgba(8,14,22,.6)' }}>
                        <button onClick={() => toggleFile(f.path)} style={{ width: '100%', display: 'flex', alignItems: 'center', gap: 9, padding: '10px 12px', border: 'none', background: 'rgba(63,214,255,.07)', cursor: 'pointer', color: C.textHi, fontFamily: C.mono, fontSize: 12 }}>
                          <span style={{ color: C.cyan, transform: open ? 'rotate(90deg)' : 'none', transition: 'transform .15s' }}>▶</span>
                          <span style={{ flex: 1, textAlign: 'left', wordBreak: 'break-all' }}>{f.path}</span>
                          <span style={{ fontFamily: C.mono, fontSize: 10, color: C.textDim }}>{f.hunks.length} hunk{f.hunks.length === 1 ? '' : 's'}</span>
                        </button>
                        {open && (
                          <div style={{ padding: '10px 12px', fontFamily: C.mono, fontSize: 11.5, lineHeight: 1.55 }}>
                            {f.hunks.map((h, hi) => <HunkView key={hi} hunk={h} />)}
                          </div>
                        )}
                      </div>
                    );
                  })
                )}
              </div>

              {a.devChanges && a.devChanges.length > 0 && (
                <div style={{ padding: 14, borderTop: `1px solid ${w(0.06)}` }}>
                  <button onClick={a.devApplyChanges} style={applyBtn} onMouseEnter={(e) => (e.currentTarget.style.filter = 'brightness(1.1)')} onMouseLeave={(e) => (e.currentTarget.style.filter = 'none')}>
                    Применить изменения
                  </button>
                  <button onClick={a.devRejectChanges} style={rejectBtn}>Отклонить</button>
                </div>
              )}
            </>
          )}

          {rightTab === 'reasoning' && (
            <div style={{ flex: 1, overflowY: 'auto', padding: 14 }}>
              {a.devThinking && <ThinkingRow label={`opencode работает… ${a.devThinkingText || ''}`} />}
              {a.devReasoning && (
                <div style={{
                  fontFamily: C.mono, fontSize: 11.5, lineHeight: 1.6,
                  color: C.textMid, whiteSpace: 'pre-wrap', wordBreak: 'break-word',
                }}>{a.devReasoning}</div>
              )}
              {!a.devReasoning && !a.devThinking && (
                <div style={{ fontFamily: C.mono, fontSize: 11, color: C.textDim, textAlign: 'center', marginTop: 60, lineHeight: 2 }}>
                  Нет данных о реализации.
                </div>
              )}
            </div>
          )}
        </div>
      </div>
    </div>
  );
}

function HunkView({ hunk }: { hunk: DevHunk }) {
  return (
    <div style={{ border: `1px solid ${w(0.06)}`, borderRadius: 8, marginBottom: 8, overflow: 'hidden' }}>
      {hunk.oldLines.map((line, i) => (
        <div key={`o${i}`} style={{ display: 'flex', background: 'rgba(255,107,91,.07)' }}>
          <span style={{ width: 34, flexShrink: 0, textAlign: 'right', paddingRight: 8, color: C.textFaint, userSelect: 'none' }}>{hunk.oldStart + i}</span>
          <span style={{ width: 18, flexShrink: 0, color: C.accent, userSelect: 'none' }}>−</span>
          <span style={{ color: '#f0b9b2', whiteSpace: 'pre-wrap', wordBreak: 'break-word' }}>{line}</span>
        </div>
      ))}
      {hunk.newLines.map((line, i) => (
        <div key={`n${i}`} style={{ display: 'flex', background: 'rgba(80,220,150,.06)' }}>
          <span style={{ width: 34, flexShrink: 0, textAlign: 'right', paddingRight: 8, color: C.textFaint, userSelect: 'none' }}>{hunk.newStart + i}</span>
          <span style={{ width: 18, flexShrink: 0, color: C.green, userSelect: 'none' }}>+</span>
          <span style={{ color: '#b7e6c8', whiteSpace: 'pre-wrap', wordBreak: 'break-word' }}>{line}</span>
        </div>
      ))}
    </div>
  );
}

const inputStyle: React.CSSProperties = {
  width: '100%', boxSizing: 'border-box', padding: '12px 16px', borderRadius: 10,
  background: 'rgba(255,255,255,0.03)', border: '1px solid rgba(255,255,255,0.08)', outline: 'none',
  color: '#f8fafc', fontFamily: C.mono, fontSize: 13,
};
const accentBtn: React.CSSProperties = {
  width: '100%', marginTop: 16, padding: '12px 0', borderRadius: 10, border: 'none',
  background: 'linear-gradient(135deg, #ff6b5b 0%, #ff8b7b 100%)', color: '#ffffff',
  fontFamily: "'Plus Jakarta Sans', sans-serif", fontSize: 13.5, fontWeight: 700,
  cursor: 'pointer', letterSpacing: '.04em', boxShadow: '0 4px 16px rgba(255,107,91,0.35)',
  transition: 'all .18s',
};
const ghostBtn: React.CSSProperties = {
  marginTop: 16, padding: '11px 18px', borderRadius: 10, border: '1px solid rgba(255,255,255,0.1)',
  background: 'rgba(255,255,255,0.03)', color: '#cbd5e1', fontFamily: "'Plus Jakarta Sans', sans-serif",
  fontSize: 13, cursor: 'pointer', transition: 'all .18s',
};
const devBar: React.CSSProperties = {
  display: 'flex', alignItems: 'center', justifyContent: 'space-between', padding: '12px 24px',
  borderBottom: '1px solid rgba(255,255,255,0.06)', background: 'rgba(6, 8, 13, 0.55)',
  backdropFilter: 'blur(16px)',
};
const smallBtn: React.CSSProperties = {
  display: 'flex', alignItems: 'center', gap: 6, padding: '6px 12px', borderRadius: 8,
  border: '1px solid rgba(255,255,255,0.08)', background: 'rgba(255,255,255,0.03)', color: '#94a3b8',
  fontFamily: C.mono, fontSize: 11, cursor: 'pointer', transition: 'all .18s',
};
const selStyle: React.CSSProperties = {
  background: 'rgba(10, 15, 24, 0.9)', border: '1px solid rgba(255,255,255,0.1)', borderRadius: 8,
  color: '#cbd5e1', fontFamily: C.mono, fontSize: 11, padding: '6px 10px', outline: 'none', cursor: 'pointer',
};
const applyBtn: React.CSSProperties = {
  width: '100%', padding: '12px 0', borderRadius: 10, border: 'none',
  background: 'linear-gradient(135deg, #39d98a 0%, #4ae396 100%)', color: '#06130b',
  fontFamily: "'Plus Jakarta Sans', sans-serif", fontSize: 13.5, fontWeight: 700, cursor: 'pointer',
  letterSpacing: '.04em', boxShadow: '0 4px 16px rgba(57,217,138,0.3)', transition: 'all .18s',
};
const rejectBtn: React.CSSProperties = {
  width: '100%', marginTop: 8, padding: '10px 0', borderRadius: 10,
  border: '1px solid rgba(255,255,255,0.1)', background: 'rgba(255,255,255,0.03)', color: '#94a3b8',
  fontFamily: "'Plus Jakarta Sans', sans-serif", fontSize: 13, fontWeight: 600, cursor: 'pointer',
  transition: 'all .18s',
};
const tabBtn = (active: boolean): React.CSSProperties => ({
  flex: 1, padding: '12px 0', border: 'none',
  borderBottom: active ? '2px solid #ff6b5b' : '2px solid transparent',
  background: active ? 'rgba(255,107,91,0.08)' : 'transparent',
  color: active ? '#ff8a7a' : 'rgba(255,255,255,0.45)',
  fontFamily: C.mono, fontSize: 11, fontWeight: active ? 700 : 500, letterSpacing: '.08em',
  cursor: 'pointer', transition: 'all .15s',
});
