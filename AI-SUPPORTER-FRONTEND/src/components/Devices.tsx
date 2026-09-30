import { useEffect, useRef, useState } from 'react';
import { C, acc } from '../lib/theme.js';
import { ui } from '../lib/ui.js';
import { TablePanel, TableHead, TableRow, Td } from '../lib/Table.js';
import { Icon } from './Icon.jsx';
import type { AssistantState } from '../hooks/useAssistant.js';
import type { Device } from '../lib/data.js';

const TYPE_COLOR: Record<string, string> = {
  camera: C.accent,
  web: C.cyan,
  osc: C.purple,
  artnet: C.green,
  switch: C.amber,
  computer: C.blue,
  printer: C.pink,
};

const TYPE_LABEL: Record<string, string> = {
  camera: 'Камера',
  web: 'Веб',
  osc: 'OSC',
  artnet: 'Art-Net',
  switch: 'Коммутатор',
  computer: 'Компьютер',
  printer: 'Принтер',
  unknown: 'Неизвестно',
};

type Tab = 'online' | 'all';

const COLS = '140px 1.7fr 100px 90px 1.8fr 90px';

const COLS_DEF = [
  { key: '140px', label: 'IP' },
  { key: '1.7fr', label: 'Имя' },
  { key: '100px', label: 'Тип' },
  { key: '90px', label: 'Статус' },
  { key: '1.8fr', label: 'Сервисы' },
  { key: '90px', label: 'Ответ' },
];

export default function Devices({ a, ws }: { a: AssistantState; ws?: WebSocket | null }) {
  const [menu, setMenu] = useState<{ x: number; y: number; device: Device } | null>(null);
  const [tab, setTab] = useState<Tab>('online');
  const [renaming, setRenaming] = useState<Device | null>(null);
  const [newName, setNewName] = useState('');
  const [snapshot, setSnapshot] = useState<{ device: Device; data: string; message: string } | null>(null);
  const [analyzing, setAnalyzing] = useState<string | null>(null);
  const [analyzeResult, setAnalyzeResult] = useState<string | null>(null);
  const snapReqDevice = useRef<Device | null>(null);

  useEffect(() => {
    if (a.camSnapshot && snapReqDevice.current && snapReqDevice.current.ip === a.camSnapshot.ip) {
      setSnapshot({ device: snapReqDevice.current, data: a.camSnapshot.data, message: a.camSnapshot.message });
    }
  }, [a.camSnapshot]);

  useEffect(() => {
    if (a.camAnalyze) {
      setAnalyzing(null);
      setAnalyzeResult(a.camAnalyze.ok ? a.camAnalyze.result : 'Не удалось распознать кадр');
    }
  }, [a.camAnalyze]);

  const onlineCount = a.devices.filter((d) => d.online).length;
  const list = tab === 'online' ? a.devices.filter((d) => d.online) : a.devices;

  const scan = () => {
    if (ws?.readyState === WebSocket.OPEN) {
      ws.send(JSON.stringify({ type: 'device:scan' }));
    }
  };

  const openCamera = (d: Device) => {
    if (ws?.readyState === WebSocket.OPEN) {
      ws.send(JSON.stringify({ type: 'camera:open', ip: d.ip }));
    } else if (d.mjpegUrl) {
      a.openFloatingWindow('camera', d.mjpegUrl, d.name || d.ip);
    }
    setMenu(null);
  };

  const openWeb = (d: Device) => {
    const url = d.services.includes('http-alt') ? `http://${d.ip}:8080` : `http://${d.ip}`;
    a.openFloatingWindow('browser', url, d.name || d.ip);
    setMenu(null);
  };

  const onRowContext = (e: React.MouseEvent, d: Device) => {
    e.preventDefault();
    setMenu({ x: e.clientX, y: e.clientY, device: d });
  };

  const startRename = (d: Device) => {
    setRenaming(d);
    setNewName(d.name || '');
    setMenu(null);
  };

  const saveName = () => {
    if (renaming) a.registerIp(renaming.ip, newName.trim());
    setRenaming(null);
  };

  const retryWithCreds = (d: Device) => {
    const user = window.prompt(`Логин для камеры ${d.ip}:`, 'root');
    if (user === null) return;
    const pass = window.prompt(`Пароль для камеры ${d.ip}:`, '') || '';
    snapReqDevice.current = d;
    setSnapshot(null);
    a.sendCameraSnapshot(d.ip, user, pass);
  };

  return (
    <div style={{ position: 'absolute', inset: 0, display: 'flex', flexDirection: 'column', animation: 'ariaFadeFast .22s ease both' }}>
      <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', padding: '16px 30px', borderBottom: `1px solid ${C.lineSoft}` }}>
        <div style={{ display: 'flex', gap: 4 }}>
          {(['online', 'all'] as Tab[]).map((t) => (
            <button key={t} onClick={() => setTab(t)} style={{
              padding: '6px 14px', borderRadius: 6, border: 'none', cursor: 'pointer',
              fontSize: 11, fontFamily: C.mono, letterSpacing: '.08em',
              background: tab === t ? acc(0.2) : 'transparent',
              color: tab === t ? C.accent : C.textDim,
              borderBottom: tab === t ? `2px solid ${C.accent}` : '2px solid transparent',
            }}>
              {{ online: `Онлайн (${onlineCount})`, all: `Все (${a.devices.length})` }[t]}
            </button>
          ))}
        </div>
        <button onClick={scan} disabled={a.devicesScanning} style={{ ...ui.s.btn, ...(a.devicesScanning ? { opacity: .5, cursor: 'default' } : null) }}>
          {a.devicesScanning ? 'Сканирование…' : 'Сканировать'}
        </button>
      </div>

      {a.devicesScanning && (
        <div style={{ display: 'flex', alignItems: 'center', gap: 12, padding: '12px 30px', borderBottom: `1px solid ${C.lineSoft}`, fontFamily: C.mono, fontSize: 12, color: C.cyan }}>
          <span style={spinner} />
          Сканирование сети…
        </div>
      )}

      {analyzing && (
        <div style={{ display: 'flex', alignItems: 'center', gap: 12, padding: '12px 30px', borderBottom: `1px solid ${C.lineSoft}`, fontFamily: C.mono, fontSize: 12, color: C.cyan }}>
          <span style={spinner} />
          Распознавание кадра…
        </div>
      )}

      <div style={{ flex: 1, overflow: 'auto', padding: '18px 30px' }}>
        {list.length === 0 && !a.devicesScanning ? (
          <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center', height: '60%', color: C.textDim, fontFamily: C.mono, fontSize: 13, textAlign: 'center', gap: 10 }}>
            <Icon name="camera" color={C.textFaint} size={40} />
            <span>{tab === 'online' ? 'Нет онлайн устройств' : 'Нет известных устройств — Сканировать'}</span>
          </div>
        ) : (
          <TablePanel style={{ minWidth: 880 }}>
            <TableHead cols={COLS_DEF} sortKey="" sortDir={1} onSort={() => {}} />
            {list.map(d => (
              <TableRow key={d.ip} cols={COLS} onContextMenu={(e) => onRowContext(e, d)}>
                <Td mono color={C.cyan}>{d.ip}</Td>
                <Td style={{ whiteSpace: 'normal' }}>
                  <span style={{ color: C.textHi }}>{d.name || '—'}</span>
                  {d.vendor && (
                    <span style={{ display: 'block', fontFamily: C.mono, fontSize: 10, color: C.textDim }}>{d.vendor}</span>
                  )}
                </Td>
                <Td mono color={TYPE_COLOR[d.type] || '#8a97ab'}>{TYPE_LABEL[d.type] || d.type}</Td>
                <Td mono color={d.online ? C.green : C.textFaint} style={{ letterSpacing: '.1em' }}>
                  {d.online ? '●' : '○'}
                </Td>
                <Td style={{ whiteSpace: 'normal' }}>
                  <div style={{ display: 'flex', flexWrap: 'wrap', gap: 4 }}>
                    {d.services.map(s => (
                      <span key={s} style={{ padding: '1px 7px', borderRadius: 6, background: 'rgba(255,255,255,.06)', color: C.textMid, fontFamily: C.mono, fontSize: 10 }}>{s}</span>
                    ))}
                  </div>
                </Td>
                <Td mono color={d.responseMs > 150 ? C.amber : C.textMid}>{d.responseMs > 0 ? `${d.responseMs} мс` : '—'}</Td>
              </TableRow>
            ))}
          </TablePanel>
        )}
      </div>

      {menu && (
        <div style={{ position: 'fixed', left: menu.x, top: menu.y, zIndex: 51, background: '#0d1320', border: `1px solid ${C.line}`, borderRadius: 10, padding: 6, minWidth: 200, boxShadow: '0 12px 40px rgba(0,0,0,.6)' }}>
          {(menu.device.mjpegUrl || menu.device.type === 'camera') && (
            <MenuItem onClick={() => openCamera(menu.device)}>Открыть камеру</MenuItem>
          )}
          {menu.device.type === 'camera' && (
            <>
              <MenuItem onClick={() => { snapReqDevice.current = menu.device; a.sendCameraSnapshot(menu.device.ip); setMenu(null); }}>Снимок</MenuItem>
              <MenuItem onClick={() => { setAnalyzing(menu.device.ip); a.sendCameraAnalyze(menu.device.ip); setMenu(null); }}>Распознать кадр</MenuItem>
            </>
          )}
          <MenuItem onClick={() => openWeb(menu.device)}>Открыть веб-интерфейс</MenuItem>
          <MenuItem onClick={() => startRename(menu.device)}>Задать имя</MenuItem>
        </div>
      )}

      {menu && (
        <div style={{ position: 'fixed', inset: 0, zIndex: 50 }} onClick={() => setMenu(null)} onContextMenu={(e) => { e.preventDefault(); setMenu(null); }} />
      )}

      {renaming && (
        <>
          <div style={{ position: 'fixed', inset: 0, zIndex: 60, background: 'rgba(0,0,0,.5)' }} onClick={() => setRenaming(null)} />
          <div style={{ position: 'fixed', left: '50%', top: '50%', transform: 'translate(-50%, -50%)', zIndex: 61, width: 340, background: '#0d1320', border: `1px solid ${C.accentLine}`, borderRadius: 14, padding: 20, boxShadow: '0 20px 60px rgba(0,0,0,.7)', fontFamily: C.ui }}>
            <div style={{ fontFamily: C.mono, fontSize: 11, letterSpacing: '.14em', color: C.textDim, textTransform: 'uppercase', marginBottom: 6 }}>{renaming.ip}</div>
            <div style={{ fontSize: 16, fontWeight: 600, color: C.textHi, marginBottom: 14 }}>Задать имя</div>
            <input
              value={newName}
              onChange={(e) => setNewName(e.target.value)}
              onKeyDown={(e) => { if (e.key === 'Enter') saveName(); if (e.key === 'Escape') setRenaming(null); }}
              placeholder="Имя устройства…"
              style={{ width: '100%', ...ui.s.input }}
              autoFocus
            />
            <div style={{ display: 'flex', gap: 8, marginTop: 16, justifyContent: 'flex-end' }}>
              <button onClick={() => setRenaming(null)} style={ui.s.btn}>Отмена</button>
              <button onClick={saveName} style={ui.s.btnActive}>Сохранить</button>
            </div>
          </div>
        </>
      )}

      {snapshot && (
        <>
          <div style={{ position: 'fixed', inset: 0, zIndex: 60, background: 'rgba(0,0,0,.55)' }} onClick={() => setSnapshot(null)} />
          <div style={{ position: 'fixed', left: '50%', top: '50%', transform: 'translate(-50%, -50%)', zIndex: 61, maxWidth: '94vw', maxHeight: '92vh', background: '#0d1320', border: `1px solid ${C.accentLine}`, borderRadius: 14, padding: 16, boxShadow: '0 20px 60px rgba(0,0,0,.7)', display: 'flex', flexDirection: 'column', fontFamily: C.ui }}>
            <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 12, marginBottom: 12 }}>
              <span style={{ fontFamily: C.mono, fontSize: 11, letterSpacing: '.14em', color: C.textDim, textTransform: 'uppercase' }}>СНИМОК · {snapshot.device.name || snapshot.device.ip}</span>
              <button onClick={() => setSnapshot(null)} style={ui.s.btn}>Закрыть</button>
            </div>
            {snapshot.data ? (
              <img src={'data:image/jpeg;base64,' + snapshot.data} alt="" style={{ maxWidth: '90vw', maxHeight: '80vh', objectFit: 'contain', borderRadius: 10 }} />
            ) : (
              <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 14, padding: 40, fontFamily: C.mono, fontSize: 12, color: C.textDim }}>
                <div>{snapshot.message || 'Не удалось получить снимок.'}</div>
                <button onClick={() => retryWithCreds(snapshot.device)} style={ui.s.btnActive}>Указать логин/пароль</button>
              </div>
            )}
          </div>
        </>
      )}

      {analyzeResult !== null && (
        <>
          <div style={{ position: 'fixed', inset: 0, zIndex: 60, background: 'rgba(0,0,0,.5)' }} onClick={() => setAnalyzeResult(null)} />
          <div style={{ position: 'fixed', left: '50%', top: '50%', transform: 'translate(-50%, -50%)', zIndex: 61, width: 480, maxWidth: '92vw', maxHeight: '80vh', overflowY: 'auto', background: '#0d1320', border: `1px solid ${C.accentLine}`, borderRadius: 14, padding: 20, boxShadow: '0 20px 60px rgba(0,0,0,.7)', fontFamily: C.ui }}>
            <div style={{ fontFamily: C.mono, fontSize: 11, letterSpacing: '.14em', color: C.textDim, textTransform: 'uppercase', marginBottom: 6 }}>распознавание кадра</div>
            <div style={{ fontSize: 16, fontWeight: 600, color: C.textHi, marginBottom: 14 }}>Результат распознавания</div>
            <div style={{ fontFamily: C.mono, fontSize: 12, color: C.tableText, lineHeight: 1.7, whiteSpace: 'pre-wrap' }}>{analyzeResult}</div>
            <div style={{ display: 'flex', gap: 8, marginTop: 16, justifyContent: 'flex-end' }}>
              <button onClick={() => setAnalyzeResult(null)} style={ui.s.btn}>Закрыть</button>
            </div>
          </div>
        </>
      )}
    </div>
  );
}

function MenuItem({ onClick, children }: { onClick: () => void; children: React.ReactNode }) {
  return (
    <button
      onClick={onClick}
      onMouseEnter={(e) => { e.currentTarget.style.background = acc(0.12); e.currentTarget.style.color = C.textHi; }}
      onMouseLeave={(e) => { e.currentTarget.style.background = 'transparent'; e.currentTarget.style.color = C.textMid; }}
      style={{ display: 'block', width: '100%', textAlign: 'left', padding: '8px 12px', border: 'none', background: 'transparent', color: C.textMid, fontFamily: C.ui, fontSize: 13, cursor: 'pointer', borderRadius: 6 }}
    >
      {children}
    </button>
  );
}

const spinner: React.CSSProperties = {
  width: 14, height: 14, borderRadius: '50%', border: `2px solid ${C.line}`,
  borderTopColor: C.cyan, animation: 'ariaSpin .7s linear infinite', display: 'inline-block',
};
