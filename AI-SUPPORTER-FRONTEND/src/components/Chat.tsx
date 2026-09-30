import { useState, useRef, useEffect } from 'react';
import { useAssistant } from '../hooks/useAssistant.js';
import { C } from '../lib/theme.js';
import { UserMsg, AssistantMsg, ToolMsg, ThinkingRow, GptComposer, useStickToBottom } from './gpt.js';
import BrowserPanel from './BrowserPanel.js';
import ChatSidebar from './ChatSidebar.js';
import ContextMenu from './ContextMenu.js';
import { Icon } from './Icon.jsx';

const CHIPS = [
  'Настрой Art-Net оборудование',
  'Объясни протокол OSC',
  'Проверь сетевое подключение',
  'Покажи системные метрики',
];

export default function Chat({ a }: { a: ReturnType<typeof useAssistant> }) {
  const [browse, setBrowse] = useState<string | null>(null);
  const { ref } = useStickToBottom([a.messages, a.thinking, a.chatThinking]);
  const editRef = useRef<HTMLTextAreaElement>(null);

  useEffect(() => {
    if (a.editingIndex !== null && editRef.current) {
      editRef.current.focus();
      editRef.current.selectionStart = editRef.current.value.length;
    }
  }, [a.editingIndex]);

  const openLink = (url: string) => setBrowse(url);

  const handleContextMenu = (e: React.MouseEvent, i: number, role: 'user' | 'assistant', text: string) => {
    e.preventDefault();
    a.setContextMenu({ x: e.clientX, y: e.clientY, index: i, role, text });
  };

  return (
    <div style={{ position: 'absolute', inset: 0, display: 'flex', animation: 'ariaFadeFast .22s ease both' }}>
      <ChatSidebar
        conversations={a.conversations}
        active={a.activeConversation}
        onSelect={a.switchChat}
        onCreate={a.createChat}
        onDelete={a.deleteChat}
      />

      <div style={{ flex: 1, display: 'flex', flexDirection: 'column', minWidth: 0 }}>
        <div style={{
          display: 'flex', alignItems: 'center', justifyContent: 'space-between',
          padding: '14px 30px',
          borderBottom: '1px solid rgba(255,255,255,.06)',
          background: 'rgba(7,11,18,.4)',
        }}>
          <span style={{
            fontFamily: C.mono, fontSize: 12, color: C.textMid,
            letterSpacing: '.1em',
          }}>ARIA · AI-АССИСТЕНТ</span>
          <button onClick={() => a.createChat()} style={clearBtn} title="Начать новый чат">＋ Новый чат</button>
        </div>

        <div ref={ref} style={{ flex: 1, overflowY: 'auto', padding: '20px 0 10px' }}>
          <div style={{ maxWidth: 'min(820px, 94vw)', margin: '0 auto', padding: '0 24px', display: 'flex', flexDirection: 'column', gap: 26 }}>
            {a.messages.length === 0 && !a.thinking && !a.chatThinking && (
              <div style={{ textAlign: 'center', marginTop: '16vh' }}>
                <div style={{ fontSize: 34, fontWeight: 700, letterSpacing: '.02em', color: C.textHi }}>Чем я могу помочь?</div>
                <div style={{ marginTop: 12, fontFamily: C.mono, fontSize: 12, color: C.textDim, letterSpacing: '.1em' }}>
                  ARIA · ГОТОВА К РАБОТЕ
                </div>
                <div style={{ display: 'flex', flexWrap: 'wrap', justifyContent: 'center', gap: 8, marginTop: 24, maxWidth: 600, margin: '24px auto 0' }}>
                  {CHIPS.map((chip) => (
                    <button
                      key={chip}
                      onClick={() => a.sendMsg(chip)}
                      style={{
                        padding: '9px 16px', borderRadius: 18,
                        border: '1px solid rgba(255,255,255,.1)',
                        background: 'rgba(255,255,255,.04)',
                        color: C.textMid, fontFamily: C.ui, fontSize: 13,
                        cursor: 'pointer', whiteSpace: 'nowrap',
                      }}
                      onMouseEnter={(e) => { e.currentTarget.style.background = 'rgba(255,107,91,.1)'; e.currentTarget.style.borderColor = 'rgba(255,107,91,.3)'; e.currentTarget.style.color = C.textHi; }}
                      onMouseLeave={(e) => { e.currentTarget.style.background = 'rgba(255,255,255,.04)'; e.currentTarget.style.borderColor = 'rgba(255,255,255,.1)'; e.currentTarget.style.color = C.textMid; }}
                    >{chip}</button>
                  ))}
                </div>
              </div>
            )}

            {a.messages.map((m, i) => {
              if (m.role === 'user') {
                if (a.editingIndex === i) {
                  return (
                    <div key={i} style={{ display: 'flex', justifyContent: 'flex-end' }}>
                      <textarea
                        ref={editRef}
                        defaultValue={m.text || ''}
                        onKeyDown={(e) => {
                          if (e.key === 'Enter' && !e.shiftKey) {
                            e.preventDefault();
                            a.setEditingIndex(null);
                          }
                          if (e.key === 'Escape') a.setEditingIndex(null);
                        }}
                        style={{
                          maxWidth: '78%', background: 'rgba(255,107,91,.08)', borderRadius: 20,
                          padding: '11px 17px', fontSize: 15, lineHeight: 1.55, color: C.textHi,
                          border: `1px solid ${C.accentLine}`, outline: 'none', resize: 'none',
                          fontFamily: C.ui, whiteSpace: 'pre-wrap', wordBreak: 'break-word',
                        }}
                      />
                    </div>
                  );
                }
                return (
                  <div key={i} onContextMenu={(e) => handleContextMenu(e, i, 'user', m.text || '')}>
                    <UserMsg text={m.text || ''} image={m.image} />
                  </div>
                );
              }
              if (m.role === 'tool') {
                return <ToolMsg key={i} name={m.name || 'tool'} result={m.result || ''} ms={m.ms ?? 0} />;
              }
              return (
                <div key={i} onContextMenu={(e) => handleContextMenu(e, i, 'assistant', m.text || '')}>
                  <AssistantMsg text={m.text} onLink={openLink} />
                  {i > 0 && m.text && (
                    <div style={{ display: 'flex', gap: 6, paddingLeft: 44, marginTop: 6 }}>
                      <button
                        onClick={() => a.likeMessage(i)}
                        title="Нравится"
                        style={actBtn}
                        onMouseEnter={(e) => { e.currentTarget.style.background = 'rgba(57,217,138,.12)'; e.currentTarget.style.color = C.green; }}
                        onMouseLeave={(e) => { e.currentTarget.style.background = 'transparent'; e.currentTarget.style.color = a.likedMessages.has(i) ? C.green : C.textDim; }}
                      >
                        <Icon name="like" size={14} width={1.8} color={a.likedMessages.has(i) ? C.green : C.textDim} />
                      </button>
                      <button
                        onClick={() => a.dislikeMessage(i)}
                        title="Не нравится"
                        style={actBtn}
                        onMouseEnter={(e) => { e.currentTarget.style.background = 'rgba(255,84,112,.12)'; e.currentTarget.style.color = C.red; }}
                        onMouseLeave={(e) => { e.currentTarget.style.background = 'transparent'; e.currentTarget.style.color = a.dislikedMessages.has(i) ? C.red : C.textDim; }}
                      >
                        <Icon name="dislike" size={14} width={1.8} color={a.dislikedMessages.has(i) ? C.red : C.textDim} />
                      </button>
                    </div>
                  )}
                </div>
              );
            })}

            {(a.thinking || a.chatThinking) && (
              <div style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
                <ThinkingRow />
                {a.chatThinkingText && (
                  <span style={{ fontFamily: C.mono, fontSize: 12, color: C.textDim, paddingLeft: 44 }}>{a.chatThinkingText}</span>
                )}
                {a.chatReasoning && (
                  <div style={{
                    marginLeft: 44, fontFamily: C.mono, fontSize: 11.5, lineHeight: 1.6,
                    color: '#9fb0c3', whiteSpace: 'pre-wrap', maxHeight: 200, overflowY: 'auto',
                    background: 'rgba(8,14,22,.55)', border: '1px solid rgba(255,255,255,.06)',
                    borderRadius: 10, padding: '10px 14px',
                  }}>{a.chatReasoning}</div>
                )}
              </div>
            )}
          </div>
        </div>

        <GptComposer
          value={a.input}
          onChange={a.setInput}
          onSend={a.sendMsg}
          placeholder="Спросите ARIA или дайте команду…"
          onMic={a.enterVoice}
          image={a.chatImage}
          onImageChange={a.setChatImage}
        />
      </div>

      {a.contextMenu && (
        <ContextMenu
          x={a.contextMenu.x}
          y={a.contextMenu.y}
          role={a.contextMenu.role}
          text={a.contextMenu.text}
          index={a.contextMenu.index}
          onCopy={a.copyToClipboard}
          onEdit={a.setEditingIndex}
          onLike={a.likeMessage}
          onDislike={a.dislikeMessage}
          onReadAloud={a.readAloud}
          onShare={a.shareMessage}
          onClose={a.closeContextMenu}
        />
      )}

      <BrowserPanel url={browse} onClose={() => setBrowse(null)} />
    </div>
  );
}

const clearBtn: React.CSSProperties = {
  padding: '6px 14px', borderRadius: 18, border: '1px solid rgba(255,255,255,.09)',
  background: 'rgba(255,255,255,.04)', color: '#9fb0c4', cursor: 'pointer',
  fontFamily: "'Chakra Petch', sans-serif", fontSize: 12,
};
const actBtn: React.CSSProperties = {
  width: 30, height: 30, borderRadius: 9, border: '1px solid transparent',
  background: 'transparent', cursor: 'pointer', display: 'flex',
  alignItems: 'center', justifyContent: 'center',
  transition: 'background .18s, color .18s',
};
