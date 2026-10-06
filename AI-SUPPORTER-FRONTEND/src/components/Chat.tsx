import { useState, useRef, useEffect } from 'react';
import { useAssistant } from '../hooks/useAssistant.js';
import { C } from '../lib/theme.js';
import { UserMsg, AssistantMsg, ToolMsg, ThinkingRow, GptComposer, useStickToBottom } from './gpt.js';
import BrowserPanel from './BrowserPanel.js';
import ChatSidebar from './ChatSidebar.js';
import ContextMenu from './ContextMenu.js';
import { Icon } from './Icon.jsx';

const CHIPS = [
  '⚡ Настрой Art-Net оборудование',
  '📡 Объясни протокол OSC',
  '🔍 Проверь сетевое подключение',
  '📊 Покажи системные метрики',
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

      <div style={{ flex: 1, display: 'flex', flexDirection: 'column', minWidth: 0, position: 'relative' }}>
        {/* Subtle Chat Sub-Header */}
        <div style={{
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'space-between',
          padding: '12px 28px',
          borderBottom: '1px solid rgba(255,255,255,.05)',
          background: 'rgba(6, 8, 13, 0.4)',
          backdropFilter: 'blur(10px)',
        }}>
          <div style={{ display: 'flex', alignItems: 'center', gap: 9 }}>
            <span style={{
              width: 6,
              height: 6,
              borderRadius: '50%',
              background: '#38bdf8',
              boxShadow: '0 0 8px #38bdf8',
            }} />
            <span style={{
              fontFamily: C.mono,
              fontSize: 11,
              color: 'rgba(255,255,255,0.6)',
              letterSpacing: '.12em',
              fontWeight: 600,
            }}>
              ARIA · НЕЙРОСЕТЕВОЙ ДИАЛОГ
            </span>
          </div>

          <button
            onClick={() => a.createChat()}
            style={clearBtn}
            title="Начать новый диалог"
            onMouseEnter={(e) => {
              e.currentTarget.style.background = 'rgba(255,255,255,0.08)';
              e.currentTarget.style.borderColor = 'rgba(255,255,255,0.18)';
              e.currentTarget.style.color = '#fff';
            }}
            onMouseLeave={(e) => {
              e.currentTarget.style.background = 'rgba(255,255,255,0.03)';
              e.currentTarget.style.borderColor = 'rgba(255,255,255,0.08)';
              e.currentTarget.style.color = '#94a3b8';
            }}
          >
            + Новый диалог
          </button>
        </div>

        {/* Message Stream */}
        <div ref={ref} style={{ flex: 1, overflowY: 'auto', padding: '24px 0 12px' }}>
          <div style={{ maxWidth: 'min(860px, 94vw)', margin: '0 auto', padding: '0 24px', display: 'flex', flexDirection: 'column', gap: 24 }}>
            {a.messages.length === 0 && !a.thinking && !a.chatThinking && (
              <div style={{ textAlign: 'center', marginTop: '14vh' }}>
                <div style={{
                  fontFamily: "'Syne', sans-serif",
                  fontSize: 'clamp(28px, 4vw, 42px)',
                  fontWeight: 800,
                  letterSpacing: '-0.02em',
                  color: '#f8fafc',
                  background: 'linear-gradient(180deg, #ffffff 40%, rgba(255,255,255,0.6) 100%)',
                  WebkitBackgroundClip: 'text',
                  WebkitTextFillColor: 'transparent',
                }}>
                  Чем я могу помочь?
                </div>

                <div style={{
                  marginTop: 10,
                  fontFamily: C.mono,
                  fontSize: 12,
                  color: 'rgba(126,139,160,0.85)',
                  letterSpacing: '.14em',
                }}>
                  ARIA · ИНТЕЛЛЕКТУАЛЬНЫЙ АГЕНТ ГОТОВ К РАБОТЕ
                </div>

                {/* Prompt Suggestion Chips */}
                <div style={{
                  display: 'flex',
                  flexWrap: 'wrap',
                  justifyContent: 'center',
                  gap: 10,
                  marginTop: 28,
                  maxWidth: 640,
                  margin: '28px auto 0',
                }}>
                  {CHIPS.map((chip) => (
                    <button
                      key={chip}
                      onClick={() => a.sendMsg(chip)}
                      style={{
                        padding: '10px 18px',
                        borderRadius: 999,
                        border: '1px solid rgba(255,255,255,0.09)',
                        background: 'rgba(255,255,255,0.03)',
                        color: '#cbd5e1',
                        fontFamily: "'Plus Jakarta Sans', sans-serif",
                        fontSize: 13,
                        fontWeight: 500,
                        cursor: 'pointer',
                        whiteSpace: 'nowrap',
                        boxShadow: '0 2px 10px rgba(0,0,0,0.2)',
                        transition: 'all .2s cubic-bezier(.4,0,.2,1)',
                      }}
                      onMouseEnter={(e) => {
                        e.currentTarget.style.background = 'rgba(255,107,91,0.12)';
                        e.currentTarget.style.borderColor = 'rgba(255,107,91,0.35)';
                        e.currentTarget.style.color = '#fff';
                        e.currentTarget.style.transform = 'translateY(-2px)';
                        e.currentTarget.style.boxShadow = '0 6px 20px rgba(255,107,91,0.15)';
                      }}
                      onMouseLeave={(e) => {
                        e.currentTarget.style.background = 'rgba(255,255,255,0.03)';
                        e.currentTarget.style.borderColor = 'rgba(255,255,255,0.09)';
                        e.currentTarget.style.color = '#cbd5e1';
                        e.currentTarget.style.transform = 'none';
                        e.currentTarget.style.boxShadow = '0 2px 10px rgba(0,0,0,0.2)';
                      }}
                    >
                      {chip}
                    </button>
                  ))}
                </div>
              </div>
            )}

            {a.messages.map((m, i) => {
              if (m.role === 'user') {
                if (a.editingIndex === i) {
                  return (
                    <div key={i} style={{ display: 'flex', justifyContent: 'flex-end', width: '100%' }}>
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
                          maxWidth: '82%',
                          background: 'rgba(255,107,91,0.12)',
                          borderRadius: 20,
                          padding: '12px 18px',
                          fontSize: 14.5,
                          lineHeight: 1.55,
                          color: '#fff',
                          border: '1px solid rgba(255,107,91,0.4)',
                          outline: 'none',
                          resize: 'none',
                          fontFamily: "'Plus Jakarta Sans', sans-serif",
                          whiteSpace: 'pre-wrap',
                          wordBreak: 'break-word',
                          boxShadow: '0 0 20px rgba(255,107,91,0.2)',
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
                    <div style={{ display: 'flex', gap: 8, paddingLeft: 46, marginTop: 8 }}>
                      <button
                        onClick={() => a.likeMessage(i)}
                        title="Полезно"
                        style={actBtn}
                        onMouseEnter={(e) => {
                          e.currentTarget.style.background = 'rgba(57,217,138,0.15)';
                          e.currentTarget.style.color = '#39d98a';
                        }}
                        onMouseLeave={(e) => {
                          e.currentTarget.style.background = 'transparent';
                          e.currentTarget.style.color = a.likedMessages.has(i) ? '#39d98a' : 'rgba(255,255,255,0.3)';
                        }}
                      >
                        <Icon name="like" size={14} width={1.8} color={a.likedMessages.has(i) ? '#39d98a' : 'rgba(255,255,255,0.35)'} />
                      </button>
                      <button
                        onClick={() => a.dislikeMessage(i)}
                        title="Не полезно"
                        style={actBtn}
                        onMouseEnter={(e) => {
                          e.currentTarget.style.background = 'rgba(255,84,112,0.15)';
                          e.currentTarget.style.color = '#ff5470';
                        }}
                        onMouseLeave={(e) => {
                          e.currentTarget.style.background = 'transparent';
                          e.currentTarget.style.color = a.dislikedMessages.has(i) ? '#ff5470' : 'rgba(255,255,255,0.3)';
                        }}
                      >
                        <Icon name="dislike" size={14} width={1.8} color={a.dislikedMessages.has(i) ? '#ff5470' : 'rgba(255,255,255,0.35)'} />
                      </button>
                    </div>
                  )}
                </div>
              );
            })}

            {(a.thinking || a.chatThinking) && (
              <div style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
                <ThinkingRow />
                {a.chatThinkingText && (
                  <span style={{ fontFamily: C.mono, fontSize: 12, color: 'rgba(126,139,160,0.85)', paddingLeft: 46 }}>
                    {a.chatThinkingText}
                  </span>
                )}
                {a.chatReasoning && (
                  <div style={{
                    marginLeft: 46,
                    fontFamily: C.mono,
                    fontSize: 11.5,
                    lineHeight: 1.6,
                    color: '#94a3b8',
                    whiteSpace: 'pre-wrap',
                    maxHeight: 220,
                    overflowY: 'auto',
                    background: 'rgba(6,9,15,0.7)',
                    border: '1px solid rgba(255,255,255,0.06)',
                    borderRadius: 12,
                    padding: '12px 16px',
                  }}>
                    {a.chatReasoning}
                  </div>
                )}
              </div>
            )}
          </div>
        </div>

        {/* Composer */}
        <GptComposer
          value={a.input}
          onChange={a.setInput}
          onSend={a.sendMsg}
          placeholder="Спросите ARIA или дайте команду для выполнения…"
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
  padding: '6px 14px',
  borderRadius: 999,
  border: '1px solid rgba(255,255,255,.08)',
  background: 'rgba(255,255,255,.03)',
  color: '#94a3b8',
  cursor: 'pointer',
  fontFamily: "'Plus Jakarta Sans', sans-serif",
  fontSize: 12,
  fontWeight: 600,
  transition: 'all .18s',
};

const actBtn: React.CSSProperties = {
  width: 28,
  height: 28,
  borderRadius: 8,
  border: '1px solid transparent',
  background: 'transparent',
  cursor: 'pointer',
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'center',
  transition: 'all .18s',
};
