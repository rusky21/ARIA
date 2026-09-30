type IconName = 'chat' | 'network' | 'system' | 'terminal' | 'files' | 'tasks' | 'settings' | 'folder' | 'file' | 'clock' | 'shield' | 'refresh' | 'tool' | 'send' | 'close' | 'audio' | 'wifi' | 'camera' | 'like' | 'dislike' | 'copy' | 'edit' | 'share' | 'play' | 'check' | 'stop';

const PATHS: Record<string, JSX.Element> = {
  chat: <path d="M4 5h16v11H9l-4 4z" />,
  network: (
    <>
      <circle cx="6" cy="6" r="2.4" /><circle cx="18" cy="7" r="2.4" /><circle cx="12" cy="18" r="2.4" />
      <path d="M7.6 7.4 10.6 16M16.6 8.6 13.2 16M8 6.4h8" />
    </>
  ),
  system: (
    <>
      <rect x="3" y="4" width="18" height="14" rx="2" />
      <path d="M6 12l2-3 2.5 5L13 8l2 4h3" />
    </>
  ),
  terminal: (
    <>
      <rect x="3" y="4" width="18" height="16" rx="2" />
      <path d="M7 9l3 3-3 3M13 15h4" />
    </>
  ),
  files: <path d="M3 7a2 2 0 0 1 2-2h4l2 2h8a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z" />,
  tasks: <path d="M13 2 4 14h7l-1 8 9-12h-7z" />,
  settings: (
    <>
      <path d="M4 7h10M18 7h2M4 17h2M10 17h10" />
      <circle cx="16" cy="7" r="2.4" /><circle cx="8" cy="17" r="2.4" />
    </>
  ),
  folder: <path d="M3 7a2 2 0 0 1 2-2h3l2 2h9a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z" />,
  file: (
    <>
      <path d="M7 3h7l4 4v14a1 1 0 0 1-1 1H7a1 1 0 0 1-1-1V4a1 1 0 0 1 1-1z" />
      <path d="M14 3v4h4" />
    </>
  ),
  clock: (
    <>
      <circle cx="12" cy="12" r="9" /><path d="M12 7v5l3 2" />
    </>
  ),
  shield: <path d="M12 3l8 3v6c0 5-3.5 8-8 9-4.5-1-8-4-8-9V6z" />,
  refresh: <path d="M4 12a8 8 0 0 1 14-5l2 2M20 12a8 8 0 0 1-14 5l-2-2M18 4v5h-5M6 20v-5h5" />,
  tool: <path d="M14 7l-1.5-1.5a3.5 3.5 0 0 0-5 5L9 12M10 17l1.5 1.5a3.5 3.5 0 0 0 5-5L15 12" />,
  send: <path d="M5 12h14M13 6l6 6-6 6" />,
  close: <path d="M6 6l12 12M18 6L6 18" />,
  audio: (
    <>
      <path d="M4 10v4h4l5 4V6L8 10z" />
      <path d="M16 9a4 4 0 0 1 0 6M18.5 6.5a8 8 0 0 1 0 11" />
    </>
  ),
  wifi: (
    <>
      <path d="M2 9a14 14 0 0 1 20 0M5.5 12.5a9 9 0 0 1 13 0M9 16a4.5 4.5 0 0 1 6 0" />
      <circle cx="12" cy="19.5" r="1.2" />
    </>
  ),
  camera: (
    <>
      <path d="M23 19a2 2 0 0 1-2 2H3a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h4l2-3h6l2 3h4a2 2 0 0 1 2 2z" />
      <circle cx="12" cy="13" r="4" />
    </>
  ),
  like: <path d="M7 22V11M3 13v8a1 1 0 0 0 1 1h3M7 11l4-8a2.5 2.5 0 0 1 4 2l-.8 3h5a2 2 0 0 1 2 2.2l-1.4 8A2 2 0 0 1 17.9 22H9l-2-2z" />,
  dislike: <path d="M17 2v11M21 11v8a1 1 0 0 1-1 1h-3M17 13l-4 8a2.5 2.5 0 0 1-4-2l.8-3H4.8a2 2 0 0 1-2-2.2l1.4-8A2 2 0 0 1 6.1 2H15l2 2z" />,
  copy: (
    <>
      <rect x="9" y="9" width="12" height="12" rx="2" />
      <path d="M5 15V5a2 2 0 0 1 2-2h10" />
    </>
  ),
  edit: <path d="M4 20h4L19.5 8.5a2.1 2.1 0 0 0-3-3L5 17zM13.5 6.5l3 3" />,
  share: <path d="M4 12v8a1 1 0 0 0 1 1h14a1 1 0 0 0 1-1v-8M16 6l-4-4-4 4M12 2v13" />,
  play: <path d="M7 4l13 8-13 8z" />,
  check: <path d="M4 12l5 5L20 6" />,
  stop: <rect x="6" y="6" width="12" height="12" rx="2" />,
};

interface IconProps {
  name: IconName;
  color?: string;
  size?: number;
  width?: number;
}

export function Icon({ name, color = 'currentColor', size = 20, width = 1.6 }: IconProps) {
  return (
    <svg width={size} height={size} viewBox="0 0 24 24" fill="none" stroke={color} strokeWidth={width} strokeLinecap="round" strokeLinejoin="round">
      {PATHS[name] || null}
    </svg>
  );
}

export function MicIcon({ color = 'currentColor', size = 20 }: { color?: string; size?: number }) {
  return (
    <svg width={size} height={size} viewBox="0 0 24 24" fill="none" stroke={color} strokeWidth={1.7} strokeLinecap="round">
      <rect x="9" y="3" width="6" height="11" rx="3" />
      <path d="M5 11a7 7 0 0 0 14 0M12 18v3" />
    </svg>
  );
}
