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
    <div style={{
      width: 250, flexShrink: 0, display: 'flex', flexDirection: 'column',
      borderRight: `1px solid ${w(0.06)}`, background: 'rgba(7,11,18,.5)',
    }}>
      <div style={{
        display: 'flex', alignItems: 'center', justifyContent: 'space-between',
        padding: '16px 14px 12px',
      }}>
        <span style={{ fontFamily: C.mono, fontSize: FS.x, color: C.textDim, letterSpacing: '.16em' }}>ЧАТЫ</span>
        <button onClick={onCreate} title="Новый чат" style={{
          width: 28, height: 28, borderRadius: R.xs, border: `1px solid ${acc(0.3)}`,
          background: acc(0.1), color: C.accent, cursor: 'pointer',
          display: 'flex', alignItems: 'center', justifyContent: 'center',
          fontSize: 17, fontFamily: C.ui, transition: 'all .18s', lineHeight: 1,
        }}
          onMouseEnter={(e) => { e.currentTarget.style.background = acc(0.2); e.currentTarget.style.boxShadow = `0 0 14px ${acc(0.3)}`; }}
          onMouseLeave={(e) => { e.currentTarget.style.background = acc(0.1); e.currentTarget.style.boxShadow = 'none'; }}>+</button>
      </div>

      <div style={{ flex: 1, overflowY: 'auto', padding: '0 8px' }}>
        {conversations.map((c) => {
          const isActive = c.id === active;
          return (
            <div
              key={c.id}
              onClick={() => onSelect(c.id)}
              style={{
                display: 'flex', alignItems: 'center', gap: 8,
                padding: '9px 10px', borderRadius: R.m, cursor: 'pointer',
                marginBottom: 2,
                borderLeft: isActive ? `2px solid ${C.accent}` : '2px solid transparent',
                background: isActive ? acc(0.1) : 'transparent',
                transition: 'background .15s',
              }}
              onMouseEnter={(e) => { if (!isActive) e.currentTarget.style.background = w(0.04); }}
              onMouseLeave={(e) => { if (!isActive) e.currentTarget.style.background = 'transparent'; }}
            >
              <div style={{ flex: 1, minWidth: 0 }}>
                <div style={{
                  fontFamily: C.ui, fontSize: FS.l, color: isActive ? C.textHi : C.textMid,
                  whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis',
                }}>{c.title || 'Новый чат'}</div>
                <div style={{ fontFamily: C.mono, fontSize: FS.x, color: C.textDim, marginTop: 2 }}>
                  {timeAgo(c.lastMessageAt || c.createdAt)}
                </div>
              </div>
              <button
                onClick={(e) => { e.stopPropagation(); onDelete(c.id); }}
                title="Удалить"
                style={{
                  width: 22, height: 22, borderRadius: R.xs, border: 'none',
                  background: 'transparent', color: C.textDim, cursor: 'pointer',
                  display: 'flex', alignItems: 'center', justifyContent: 'center',
                  opacity: isActive ? 1 : 0, transition: 'opacity .18s, background .18s', fontSize: 13,
                }}
                onMouseEnter={(e) => { e.currentTarget.style.background = w(0.07); e.currentTarget.style.color = C.red; }}
                onMouseLeave={(e) => { e.currentTarget.style.background = 'transparent'; e.currentTarget.style.color = C.textDim; }}
              >✕</button>
            </div>
          );
        })}
      </div>
    </div>
  );
}