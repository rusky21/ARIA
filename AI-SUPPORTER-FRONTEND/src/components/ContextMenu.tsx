import { useEffect, useRef } from 'react';
import { C, R, FS, w } from '../lib/theme.js';
import { Icon } from './Icon.jsx';

interface Props {
  x: number;
  y: number;
  role: 'user' | 'assistant';
  text: string;
  index: number;
  onCopy: (text: string) => void;
  onEdit?: (index: number) => void;
  onLike?: (index: number) => void;
  onDislike?: (index: number) => void;
  onReadAloud?: (text: string) => void;
  onShare?: (text: string) => void;
  onClose: () => void;
}

interface MenuItem {
  label: string;
  icon: string;
  action: () => void;
}

export default function ContextMenu({ x, y, role, text, index, onCopy, onEdit, onLike, onDislike, onReadAloud, onShare, onClose }: Props) {
  const ref = useRef<HTMLDivElement>(null);

  useEffect(() => {
    const onDown = (e: MouseEvent) => {
      if (ref.current && !ref.current.contains(e.target as Node)) onClose();
    };
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onClose();
    };
    document.addEventListener('mousedown', onDown);
    document.addEventListener('keydown', onKey);
    return () => {
      document.removeEventListener('mousedown', onDown);
      document.removeEventListener('keydown', onKey);
    };
  }, [onClose]);

  const items: MenuItem[] = role === 'assistant'
    ? [
        { label: 'Копировать', icon: 'copy', action: () => onCopy(text) },
        ...(onLike ? [{ label: 'Нравится', icon: 'like', action: () => onLike(index) }] : []),
        ...(onDislike ? [{ label: 'Не нравится', icon: 'dislike', action: () => onDislike(index) }] : []),
        ...(onReadAloud ? [{ label: 'Озвучить', icon: 'play', action: () => onReadAloud(text) }] : []),
        ...(onShare ? [{ label: 'Поделиться', icon: 'share', action: () => onShare(text) }] : []),
      ]
    : [
        { label: 'Копировать', icon: 'copy', action: () => onCopy(text) },
        ...(onEdit ? [{ label: 'Редактировать', icon: 'edit', action: () => onEdit(index) }] : []),
      ];

  const boundedX = Math.min(x, window.innerWidth - 220);
  const boundedY = Math.min(y, window.innerHeight - items.length * 40 - 24);

  return (
    <div
      ref={ref}
      style={{
        position: 'fixed', left: boundedX, top: boundedY, zIndex: 1000, minWidth: 190,
        padding: 6, borderRadius: R.m, background: C.panelHi,
        border: `1px solid ${w(0.12)}`, backdropFilter: 'blur(14px)',
        boxShadow: '0 16px 48px rgba(0,0,0,.55)', animation: 'ariaPop .18s ease both',
      }}
    >
      {items.map((item) => (
        <button
          key={item.label}
          onClick={() => { item.action(); onClose(); }}
          style={{
            width: '100%', padding: '9px 12px', borderRadius: R.s,
            border: 'none', background: 'transparent',
            color: C.textMid, fontFamily: C.ui, fontSize: FS.l,
            textAlign: 'left', cursor: 'pointer', whiteSpace: 'nowrap',
            display: 'flex', alignItems: 'center', gap: 10, transition: 'all .15s',
          }}
          onMouseEnter={(e) => { e.currentTarget.style.background = w(0.07); e.currentTarget.style.color = C.textHi; }}
          onMouseLeave={(e) => { e.currentTarget.style.background = 'transparent'; e.currentTarget.style.color = C.textMid; }}
        >
          <Icon name={item.icon as any} size={15} width={1.8} color="currentColor" />
          {item.label}
        </button>
      ))}
    </div>
  );
}