import { useEffect, useRef, useState } from 'react';
import { C, acc } from '../lib/theme.js';
import { ui } from '../lib/ui.js';
import { TablePanel, TableHead, TableRow, Td } from '../lib/Table.js';
import { Icon } from './Icon.jsx';
import type { AssistantState } from '../hooks/useAssistant.js';
import type { Device } from '../lib/data.js';

const TYPE_COLOR: Record<string, string> = {
  camera: '#ff6b5b',
  web: '#38bdf8',
  osc: '#a882ff',
  artnet: '#39d98a',
  switch: '#ffb454',
  computer: '#60a5fa',
  printer: '#f472b6',
  unknown: '#94a3b8',
};

const TYPE_LABEL: Record<string, string> = {
  camera: 'Камера',
  web: 'Веб-узел',
  osc: 'OSC',
  artnet: 'Art-Net',
  switch: 'Коммутатор',
  computer: 'Компьютер',
  printer: 'Принтер',
  unknown: 'Неизвестно',
};

type Tab = 'online' | 'all';

const COLS = '150px 1.6fr 110px 96px 1.8fr 90px';

const COLS_DEF = [
  { key: '150px', label: 'IP АДРЕС' },
  { key: '1.6fr', label: 'ИМЯ УЗЛА' },
  { key: '110px', label: 'ТИП' },
  { key: '96px', label: 'СТАТУС' },
  { key: '1.8fr', label: 'СЕРВИСЫ' },
  { key: '90px', label: 'ОТКЛИК' },
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
      {/* ─── Devices Top Controls Bar ─── */}
      <div style={{
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'space-between',
        padding: '12px 28px',
        borderBottom: '1px solid rgba(255,255,255,0.06)',
        background: 'rgba(6, 8, 13, 0.55)',
        backdropFilter: 'blur(16px)',
      }}>
        {/* Subtabs Switcher */}
        <div style={{
          display: 'flex',
          gap: 4,
          background: 'rgba(255,255,255,0.03)',
          padding: 3,
          borderRadius: 10,
          border: '1px solid rgba(255,255,255,0.05)',
        }}>
          {(['online', 'all'] as Tab[]).map((t) => {
            const active = tab === t;
            return (
              <button
                key={t}
                onClick={() => setTab(t)}
                style={{
                  padding: '5px 14px',
                  borderRadius: 7,
                  border: 'none',
                  cursor: 'pointer',
                  fontSize: 11,
                  fontFamily: C.mono,
                  letterSpacing: '.06em',
                  fontWeight: active ? 700 : 500,
                  background: active ? 'rgba(255,107,91,0.18)' : 'transparent',
                  color: active ? '#ff8a7a' : 'rgba(255,255,255,0.5)',
                  boxShadow: active ? '0 2px 10px rgba(255,107,91,0.15)' : 'none',
                  transition: 'all .18s',
                }}
              >
                {{ online: `В сети (${onlineCount})`, all: `Все узлы (${a.devices.length})` }[t]}
              </button>
            );
          })}
        </div>

        {/* Scan Network Trigger Button */}
        <button
          onClick={scan}
          disabled={a.devicesScanning}
          style={{
            display: 'inline-flex',
            alignItems: 'center',
            gap: 8,
            padding: '6px 16px',
            borderRadius: 8,
            border: '1px solid rgba(255,107,91,0.3)',
            background: a.devicesScanning ? 'rgba(255,107,91,0.08)' : 'rgba(255,107,91,0.14)',
            color: '#ff8a7a',
            cursor: a.devicesScanning ? 'default' : 'pointer',
            fontFamily: C.mono,
            fontSize: 11,
            fontWeight: 600,
            letterSpacing: '.06em',
            boxShadow: '0 2px 12px rgba(255,107,91,0.15)',
            transition: 'all .18s',
          }}
          onMouseEnter={(e) => {
            if (!a.devicesScanning) {
              e.currentTarget.style.background = 'rgba(255,107,91,0.22)';
              e.currentTarget.style.boxShadow = '0 0 16px rgba(255,107,91,0.35)';
            }
          }}
          onMouseLeave={(e) => {
            if (!a.devicesScanning) {
              e.currentTarget.style.background = 'rgba(255,107,91,0.14)';
              e.currentTarget.style.boxShadow = '0 2px 12px rgba(255,107,91,0.15)';
            }
          }}
        >
          {a.devicesScanning && <span style={spinner} />}
          <span>{a.devicesScanning ? 'Сканирование сети…' : 'Сканировать сеть'}</span>
        </button>
      </div>

      {a.devicesScanning && (
        <div style={{
          display: 'flex',
          alignItems: 'center',
          gap: 12,
          padding: '10px 28px',
          borderBottom: '1px solid rgba(255,255,255,0.05)',
          background: 'rgba(56,189,248,0.06)',
          fontFamily: C.mono,
          fontSize: 11.5,
          color: '#38bdf8',
        }}>
          <span style={spinner} />
          <span>Сканирование локальной подсети и поиск IP узлов…</span>
        </div>
      )}

      {analyzing && (
        <div style={{
          display: 'flex',
          alignItems: 'center',
          gap: 12,
          padding: '10px 28px',
          borderBottom: '1px solid rgba(255,255,255,0.05)',
          background: 'rgba(56,189,248,0.06)',
          fontFamily: C.mono,
          fontSize: 11.5,
          color: '#38bdf8',
        }}>
          <span style={spinner} />
          <span>Распознавание кадра нейросетью ARIA Vision…</span>
        </div>
      )}

      {/* ─── Device List / Table ─── */}
      <div style={{ flex: 1, overflow: 'auto', padding: '20px 28px' }}>
        {list.length === 0 && !a.devicesScanning ? (
          <div style={{
            display: 'flex',
            flexDirection: 'column',
            alignItems: 'center',
            justifyContent: 'center',
            height: '60%',
            color: 'rgba(126,139,160,0.8)',
            fontFamily: C.mono,
            fontSize: 13,
            textAlign: 'center',
            gap: 14,
          }}>
            <div style={{
              width: 60,
              height: 60,
              borderRadius: '50%',
              background: 'rgba(255,255,255,0.03)',
              border: '1px solid rgba(255,255,255,0.08)',
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'center',
            }}>
              <Icon name="camera" color={C.textFaint} size={28} />
            </div>
            <div>{tab === 'online' ? 'В сети нет активных устройств' : 'Устройства не обнаружены — запустите сканирование'}</div>
          </div>
        ) : (
          <TablePanel style={{ minWidth: 920 }}>
            <TableHead cols={COLS_DEF} sortKey="" sortDir={1} onSort={() => {}} />
            {list.map((d) => (
              <TableRow key={d.ip} cols={COLS} onContextMenu={(e) => onRowContext(e, d)}>
                {/* IP Address */}
                <Td mono color={C.cyan} style={{ fontWeight: 600 }}>
                  {d.ip}
                </Td>

                {/* Node Name & Vendor */}
                <Td style={{ whiteSpace: 'normal' }}>
                  <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                    <span style={{ color: '#f1f5f9', fontWeight: 600, fontSize: 13 }}>{d.name || '—'}</span>
                    {d.vendor && (
                      <span style={{
                        fontFamily: C.mono,
                        fontSize: 9.5,
                        color: 'rgba(255,255,255,0.4)',
                        padding: '1px 6px',
                        borderRadius: 4,
                        background: 'rgba(255,255,255,0.04)',
                        border: '1px solid rgba(255,255,255,0.06)',
                      }}>
                        {d.vendor}
                      </span>
                    )}
                  </div>
                </Td>

                {/* Type Pill */}
                <Td>
                  <span style={{
                    padding: '2px 8px',
                    borderRadius: 6,
                    fontSize: 10,
                    fontWeight: 700,
                    fontFamily: C.mono,
                    color: TYPE_COLOR[d.type] || '#94a3b8',
                    background: `${TYPE_COLOR[d.type] || '#94a3b8'}1a`,
                    border: `1px solid ${TYPE_COLOR[d.type] || '#94a3b8'}33`,
                  }}>
                    {TYPE_LABEL[d.type] || d.type}
                  </span>
                </Td>

                {/* Status Indicator */}
                <Td>
                  <div style={{ display: 'flex', alignItems: 'center', gap: 7 }}>
                    <span style={{
                      width: 7,
                      height: 7,
                      borderRadius: '50%',
                      background: d.online ? '#39d98a' : '#64748b',
                      boxShadow: d.online ? '0 0 10px #39d98a' : 'none',
                    }} />
                    <span style={{
                      fontFamily: C.mono,
                      fontSize: 10,
                      fontWeight: 600,
                      color: d.online ? '#39d98a' : '#64748b',
                      letterSpacing: '.06em',
                    }}>
                      {d.online ? 'ONLINE' : 'OFFLINE'}
                    </span>
                  </div>
                </Td>

                {/* Services Chips */}
                <Td style={{ whiteSpace: 'normal' }}>
                  <div style={{ display: 'flex', flexWrap: 'wrap', gap: 5 }}>
                    {d.services.map((s) => (
                      <span
                        key={s}
                        style={{
                          padding: '2px 8px',
                          borderRadius: 6,
                          background: 'rgba(255,255,255,0.04)',
                          border: '1px solid rgba(255,255,255,0.07)',
                          color: '#cbd5e1',
                          fontFamily: C.mono,
                          fontSize: 10,
                        }}
                      >
                        {s}
                      </span>
                    ))}
                  </div>
                </Td>

                {/* Latency */}
                <Td mono color={d.responseMs > 150 ? '#ffb454' : d.responseMs > 0 ? '#39d98a' : 'rgba(255,255,255,0.3)'}>
                  {d.responseMs > 0 ? `${d.responseMs} мс` : '—'}
                </Td>
              </TableRow>
            ))}
          </TablePanel>
        )}
      </div>

      {/* ─── Context Menu ─── */}
      {menu && (
        <div style={{
          position: 'fixed',
          left: menu.x,
          top: menu.y,
          zIndex: 51,
          background: 'rgba(10, 15, 24, 0.95)',
          border: '1px solid rgba(255,255,255,0.12)',
          borderRadius: 12,
          padding: 6,
          minWidth: 210,
          boxShadow: '0 16px 48px rgba(0,0,0,0.7)',
          backdropFilter: 'blur(20px)',
        }}>
          {(menu.device.mjpegUrl || menu.device.type === 'camera') && (
            <MenuItem onClick={() => openCamera(menu.device)}>📹 Открыть поток камеры</MenuItem>
          )}
          {menu.device.type === 'camera' && (
            <>
              <MenuItem onClick={() => { snapReqDevice.current = menu.device; a.sendCameraSnapshot(menu.device.ip); setMenu(null); }}>
                📷 Сделать снимок
              </MenuItem>
              <MenuItem onClick={() => { setAnalyzing(menu.device.ip); a.sendCameraAnalyze(menu.device.ip); setMenu(null); }}>
                🔍 Распознать кадр (Vision)
              </MenuItem>
            </>
          )}
          <MenuItem onClick={() => openWeb(menu.device)}>🌐 Открыть веб-интерфейс</MenuItem>
          <MenuItem onClick={() => startRename(menu.device)}>✏️ Переименовать узел</MenuItem>
        </div>
      )}

      {menu && (
        <div style={{ position: 'fixed', inset: 0, zIndex: 50 }} onClick={() => setMenu(null)} onContextMenu={(e) => { e.preventDefault(); setMenu(null); }} />
      )}

      {/* ─── Rename Modal ─── */}
      {renaming && (
        <>
          <div style={{ position: 'fixed', inset: 0, zIndex: 60, background: 'rgba(0,0,0,0.6)', backdropFilter: 'blur(6px)' }} onClick={() => setRenaming(null)} />
          <div style={{
            position: 'fixed',
            left: '50%',
            top: '50%',
            transform: 'translate(-50%, -50%)',
            zIndex: 61,
            width: 360,
            background: 'rgba(10, 15, 24, 0.95)',
            border: '1px solid rgba(255,107,91,0.3)',
            borderRadius: 18,
            padding: 24,
            boxShadow: '0 24px 60px rgba(0,0,0,0.8)',
            backdropFilter: 'blur(24px)',
            fontFamily: "'Plus Jakarta Sans', sans-serif",
          }}>
            <div style={{ fontFamily: C.mono, fontSize: 11, letterSpacing: '.14em', color: C.cyan, marginBottom: 8 }}>
              {renaming.ip}
            </div>
            <div style={{ fontSize: 18, fontWeight: 700, color: '#f8fafc', marginBottom: 16 }}>
              Задать имя узла
            </div>
            <input
              value={newName}
              onChange={(e) => setNewName(e.target.value)}
              onKeyDown={(e) => {
                if (e.key === 'Enter') saveName();
                if (e.key === 'Escape') setRenaming(null);
              }}
              placeholder="Например: PTZ Camera 01…"
              style={{
                width: '100%',
                padding: '10px 14px',
                borderRadius: 8,
                border: '1px solid rgba(255,255,255,0.1)',
                background: 'rgba(255,255,255,0.04)',
                fontFamily: "'Plus Jakarta Sans', sans-serif",
                fontSize: 13,
                color: '#fff',
                outline: 'none',
              }}
              autoFocus
            />
            <div style={{ display: 'flex', gap: 10, marginTop: 20, justifyContent: 'flex-end' }}>
              <button onClick={() => setRenaming(null)} style={{ ...ui.s.btn, borderRadius: 8 }}>Отмена</button>
              <button onClick={saveName} style={{ ...ui.s.btnActive, borderRadius: 8 }}>Сохранить</button>
            </div>
          </div>
        </>
      )}

      {/* ─── Snapshot Modal ─── */}
      {snapshot && (
        <>
          <div style={{ position: 'fixed', inset: 0, zIndex: 60, background: 'rgba(0,0,0,0.7)', backdropFilter: 'blur(8px)' }} onClick={() => setSnapshot(null)} />
          <div style={{
            position: 'fixed',
            left: '50%',
            top: '50%',
            transform: 'translate(-50%, -50%)',
            zIndex: 61,
            maxWidth: '92vw',
            maxHeight: '90vh',
            background: 'rgba(10, 15, 24, 0.95)',
            border: '1px solid rgba(255,255,255,0.12)',
            borderRadius: 18,
            padding: 20,
            boxShadow: '0 24px 60px rgba(0,0,0,0.8)',
            display: 'flex',
            flexDirection: 'column',
            fontFamily: "'Plus Jakarta Sans', sans-serif",
          }}>
            <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 14, marginBottom: 14 }}>
              <span style={{ fontFamily: C.mono, fontSize: 11, letterSpacing: '.14em', color: '#38bdf8', fontWeight: 600 }}>
                КАДР КАМЕРЫ · {snapshot.device.name || snapshot.device.ip}
              </span>
              <button onClick={() => setSnapshot(null)} style={{ ...ui.s.btn, borderRadius: 6 }}>Закрыть</button>
            </div>
            {snapshot.data ? (
              <img src={'data:image/jpeg;base64,' + snapshot.data} alt="" style={{ maxWidth: '88vw', maxHeight: '76vh', objectFit: 'contain', borderRadius: 12, border: '1px solid rgba(255,255,255,0.1)' }} />
            ) : (
              <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 16, padding: '40px 20px', fontFamily: C.mono, fontSize: 12, color: C.textDim }}>
                <div>{snapshot.message || 'Не удалось получить снимок с камеры.'}</div>
                <button onClick={() => retryWithCreds(snapshot.device)} style={{ ...ui.s.btnActive, borderRadius: 8 }}>
                  Указать логин / пароль
                </button>
              </div>
            )}
          </div>
        </>
      )}

      {/* ─── Vision Analyze Modal ─── */}
      {analyzeResult !== null && (
        <>
          <div style={{ position: 'fixed', inset: 0, zIndex: 60, background: 'rgba(0,0,0,0.65)', backdropFilter: 'blur(6px)' }} onClick={() => setAnalyzeResult(null)} />
          <div style={{
            position: 'fixed',
            left: '50%',
            top: '50%',
            transform: 'translate(-50%, -50%)',
            zIndex: 61,
            width: 500,
            maxWidth: '92vw',
            maxHeight: '80vh',
            overflowY: 'auto',
            background: 'rgba(10, 15, 24, 0.95)',
            border: '1px solid rgba(56,189,248,0.3)',
            borderRadius: 18,
            padding: 24,
            boxShadow: '0 24px 60px rgba(0,0,0,0.8)',
            backdropFilter: 'blur(24px)',
            fontFamily: "'Plus Jakarta Sans', sans-serif",
          }}>
            <div style={{ fontFamily: C.mono, fontSize: 11, letterSpacing: '.14em', color: '#38bdf8', marginBottom: 8, fontWeight: 700 }}>
              ARIA VISION · АНАЛИЗ КАДРА
            </div>
            <div style={{ fontSize: 18, fontWeight: 700, color: '#f8fafc', marginBottom: 14 }}>
              Результат распознавания
            </div>
            <div style={{ fontFamily: "'Plus Jakarta Sans', sans-serif", fontSize: 13.5, color: '#cbd5e1', lineHeight: 1.7, whiteSpace: 'pre-wrap' }}>
              {analyzeResult}
            </div>
            <div style={{ display: 'flex', gap: 10, marginTop: 20, justifyContent: 'flex-end' }}>
              <button onClick={() => setAnalyzeResult(null)} style={{ ...ui.s.btnActive, borderRadius: 8 }}>
                Закрыть
              </button>
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
      onMouseEnter={(e) => {
        e.currentTarget.style.background = 'rgba(255,107,91,0.15)';
        e.currentTarget.style.color = '#fff';
      }}
      onMouseLeave={(e) => {
        e.currentTarget.style.background = 'transparent';
        e.currentTarget.style.color = '#cbd5e1';
      }}
      style={{
        display: 'block',
        width: '100%',
        textAlign: 'left',
        padding: '9px 12px',
        border: 'none',
        background: 'transparent',
        color: '#cbd5e1',
        fontFamily: "'Plus Jakarta Sans', sans-serif",
        fontSize: 12.5,
        cursor: 'pointer',
        borderRadius: 8,
        transition: 'all .15s',
      }}
    >
      {children}
    </button>
  );
}

const spinner: React.CSSProperties = {
  width: 14,
  height: 14,
  borderRadius: '50%',
  border: '2px solid rgba(255,255,255,0.1)',
  borderTopColor: '#38bdf8',
  animation: 'ariaSpin .7s linear infinite',
  display: 'inline-block',
};
