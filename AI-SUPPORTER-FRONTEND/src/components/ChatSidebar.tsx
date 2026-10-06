import { C, R, FS, acc, w } from '../lib/theme.js';
import type { ConversationMeta } from '../lib/data.js';

interface Props {
  conversations: ConversationMeta[];
  active: string | null;
  onSelect: (id: string) => void;
  onCreate: () => void;
  onDelete: (id: string) => void;
}

function timeAgo(ts: string): string {
  const diff = Date.now() - new Date(ts).getTime();
  const m = Math.floor(diff / 60000);
  if (m < 1) return 'сейчас';
  if (m < 60) return `${m} мин`;
  const h = Math.floor(m / 60);
  if (h < 24) return `${h} ч`;
  const d = Math.floor(h / 24);
  return `${d} д`;
}

export default function ChatSidebar({ conversations, active, onSelect, onCreate, onDelete }: Props) {
  return (
    <aside style={{
      width: 260,
      flexShrink: 0,
      display: 'flex',
      flexDirection: 'column',
      borderRight: '1px solid rgba(255,255,255,0.06)',
      background: 'rgba(6, 8, 13, 0.65)',
      backdropFilter: 'blur(20px)',
      WebkitBackdropFilter: 'blur(20px)',
    }}>
      {/* Header with New Chat Button */}
      <div style={{
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'space-between',
        padding: '16px 16px 14px',
        borderBottom: '1px solid rgba(255,255,255,0.04)',
      }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: 7 }}>
          <span style={{
            fontFamily: C.mono,
            fontSize: 10,
            color: 'rgba(255,255,255,0.4)',
            letterSpacing: '.18em',
            fontWeight: 700,
          }}>
            ДИАЛОГИ
          </span>
          <span style={{
            fontFamily: C.mono,
            fontSize: 9.5,
            padding: '1px 6px',
            borderRadius: 999,
            background: 'rgba(255,255,255,0.06)',
            color: C.textMid,
          }}>
            {conversations.length}
          </span>
        </div>

        <button
          onClick={onCreate}
          title="Создать новый чат"
          style={{
            display: 'inline-flex',
            alignItems: 'center',
            gap: 6,
            padding: '5px 11px',
            borderRadius: 999,
            border: `1px solid ${acc(0.35)}`,
            background: 'rgba(255,107,91,0.1)',
            color: C.accent,
            cursor: 'pointer',
            fontSize: 11,
            fontWeight: 600,
            fontFamily: "'Plus Jakarta Sans', sans-serif",
            transition: 'all .18s',
            boxShadow: '0 2px 8px rgba(255,107,91,0.15)',
          }}
          onMouseEnter={(e) => {
            e.currentTarget.style.background = 'rgba(255,107,91,0.2)';
            e.currentTarget.style.boxShadow = `0 0 16px ${acc(0.35)}`;
            e.currentTarget.style.transform = 'translateY(-1px)';
          }}
          onMouseLeave={(e) => {
            e.currentTarget.style.background = 'rgba(255,107,91,0.1)';
            e.currentTarget.style.boxShadow = '0 2px 8px rgba(255,107,91,0.15)';
            e.currentTarget.style.transform = 'none';
          }}
        >
          <span style={{ fontSize: 13, lineHeight: 1 }}>+</span>
          <span>Новый</span>
        </button>
      </div>

      {/* Conversations List */}
      <div style={{ flex: 1, overflowY: 'auto', padding: '10px 8px', display: 'flex', flexDirection: 'column', gap: 3 }}>
        {conversations.length === 0 && (
          <div style={{
            textAlign: 'center',
            padding: '30px 10px',
            fontFamily: C.mono,
            fontSize: 11,
            color: C.textDim,
          }}>
            Нет сохраненных диалогов
          </div>
        )}

        {conversations.map((c) => {
          const isActive = c.id === active;
          return (
            <div
              key={c.id}
              onClick={() => onSelect(c.id)}
              style={{
                display: 'flex',
                alignItems: 'center',
                gap: 10,
                padding: '9px 12px',
                borderRadius: 12,
                cursor: 'pointer',
                position: 'relative',
                background: isActive
                  ? 'linear-gradient(90deg, rgba(255,107,91,0.15) 0%, rgba(255,107,91,0.03) 100%)'
                  : 'transparent',
                border: isActive
                  ? '1px solid rgba(255,107,91,0.25)'
                  : '1px solid transparent',
                transition: 'all .18s cubic-bezier(.4,0,.2,1)',
              }}
              onMouseEnter={(e) => {
                if (!isActive) {
                  e.currentTarget.style.background = 'rgba(255,255,255,0.04)';
                  e.currentTarget.style.borderColor = 'rgba(255,255,255,0.08)';
                }
              }}
              onMouseLeave={(e) => {
                if (!isActive) {
                  e.currentTarget.style.background = 'transparent';
                  e.currentTarget.style.borderColor = 'transparent';
                }
              }}
            >
              <div style={{ flex: 1, minWidth: 0 }}>
                <div style={{
                  fontFamily: "'Plus Jakarta Sans', sans-serif",
                  fontSize: 13,
                  fontWeight: isActive ? 600 : 500,
                  color: isActive ? '#f8fafc' : '#94a3b8',
                  whiteSpace: 'nowrap',
                  overflow: 'hidden',
                  textOverflow: 'ellipsis',
                }}>
                  {c.title || 'Новый чат'}
                </div>
                <div style={{
                  fontFamily: C.mono,
                  fontSize: 10,
                  color: 'rgba(126,139,160,0.7)',
                  marginTop: 2,
                  letterSpacing: '.03em',
                }}>
                  {timeAgo(c.lastMessageAt || c.createdAt)}
                </div>
              </div>

              <button
                onClick={(e) => {
                  e.stopPropagation();
                  onDelete(c.id);
                }}
                title="Удалить чат"
                style={{
                  width: 22,
                  height: 22,
                  borderRadius: 6,
                  border: 'none',
                  background: 'transparent',
                  color: '#64748b',
                  cursor: 'pointer',
                  display: 'flex',
                  alignItems: 'center',
                  justifyContent: 'center',
                  opacity: isActive ? 1 : 0.4,
                  transition: 'all .18s',
                  fontSize: 12,
                }}
                onMouseEnter={(e) => {
                  e.currentTarget.style.background = 'rgba(255,84,112,0.15)';
                  e.currentTarget.style.color = '#ff5470';
                  e.currentTarget.style.opacity = '1';
                }}
                onMouseLeave={(e) => {
                  e.currentTarget.style.background = 'transparent';
                  e.currentTarget.style.color = '#64748b';
                  e.currentTarget.style.opacity = isActive ? '1' : '0.4';
                }}
              >
                ✕
              </button>
            </div>
          );
        })}
      </div>
    </aside>
  );
}
