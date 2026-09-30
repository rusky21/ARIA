import { useState, useRef, useEffect, useCallback } from 'react';
import {
  type Packet, type Message, type TermLine, type ProcessInfo, type TaskItem,
  type TaskLoad, type NetworkInterface, type DevMessage, type DevFileChange, type DevAgentInfo,
  type LlmConfig, type TrafficReport, type ConversationMeta,
  type FsEntry, type FsReadResult, type UserEntry, type WakeTrainResult, type UserResult,
  type Device,
  type UserLlmProfile, type TaskModelSelection, type LlmCapabilities, type ProviderCapabilities, type ModelCapability,
  type TtsConf, type TtsModels, type TtsCheck,
} from '../lib/data.js';

export interface AsrModels {
  local: string[];
  api: string[];
}

export interface AsrCheck {
  provider: string;
  model: string;
  ok: boolean;
  ms: number;
  engine: string;
  message?: string;
}
import { getSession, apiFetch } from '../lib/auth.js';

const MAX_PACKETS = 10000;

function recordUntilSilence(maxDurationMs = 5000, sampleRate = 16000, abortSignal?: AbortSignal): Promise<ArrayBuffer> {
  return new Promise(async (resolve, reject) => {
    try {
      const stream = await navigator.mediaDevices.getUserMedia({ audio: true });
      const ctx = new AudioContext({ sampleRate });
      if (ctx.state === 'suspended') ctx.resume().catch(() => {});
      const src = ctx.createMediaStreamSource(stream);
      const node = ctx.createScriptProcessor(4096, 1, 1);

      const samples: number[] = [];
      const rmsRing: number[] = [];
      let silenceStart: number | null = null;
      let threshold = 0.03;
      let shouldStop = false;
      let finished = false;
      let calibrated = false;

      node.onaudioprocess = (e) => {
        if (shouldStop || abortSignal?.aborted) return;
        const ch = e.inputBuffer.getChannelData(0);
        let sumSq = 0;
        const cap = sampleRate * 2.5;
        for (let i = 0; i < ch.length; i++) {
          sumSq += ch[i] * ch[i];
          if (samples.length < cap) samples.push(ch[i]);
        }
        const rms = Math.sqrt(sumSq / ch.length);

        if (!calibrated && samples.length >= sampleRate * 0.5) {
          const avg = rmsRing.reduce((a, b) => a + b, 0) / rmsRing.length;
          threshold = Math.max(0.02, Math.min(0.06, avg * 2));
          calibrated = true;
        }

        if (!calibrated) {
          rmsRing.push(rms);
        }

        if (rms < threshold) {
          if (silenceStart === null) silenceStart = Date.now();
          else if (Date.now() - silenceStart >= 400 && samples.length >= sampleRate) {
            shouldStop = true;
          }
        } else {
          silenceStart = null;
        }
      };

      src.connect(node);

      const iv = setInterval(() => {
        if ((shouldStop || abortSignal?.aborted) && !finished) finish();
      }, 100);

      const timeout = setTimeout(finish, maxDurationMs);

      function finish() {
        if (finished) return;
        finished = true;
        clearInterval(iv);
        clearTimeout(timeout);
        src.disconnect();
        node.disconnect();
        stream.getTracks().forEach((t) => t.stop());
        ctx.close();

        const chunkSize = sampleRate / 10;
        let startIdx = 0;
        for (let i = 0; i < Math.min(samples.length, sampleRate * 2); i += chunkSize) {
          const chunk = samples.slice(i, i + chunkSize);
          let ssq = 0;
          for (const s of chunk) ssq += s * s;
          if (Math.sqrt(ssq / chunk.length) > threshold * 0.7) {
            startIdx = Math.max(0, i - chunkSize);
            break;
          }
        }

        const trimmed = samples.slice(startIdx);
        if (trimmed.length < sampleRate / 4) { resolve(new ArrayBuffer(0)); return; }

        const numSamples = trimmed.length;
        const buffer = new ArrayBuffer(44 + numSamples * 2);
        const view = new DataView(buffer);

        const writeStr = (off: number, str: string) => {
          for (let i = 0; i < str.length; i++) view.setUint8(off + i, str.charCodeAt(i));
        };
        writeStr(0, 'RIFF');
        view.setUint32(4, 36 + numSamples * 2, true);
        writeStr(8, 'WAVE');
        writeStr(12, 'fmt ');
        view.setUint32(16, 16, true);
        view.setUint16(20, 1, true);
        view.setUint16(22, 1, true);
        view.setUint32(24, sampleRate, true);
        view.setUint32(28, sampleRate * 2, true);
        view.setUint16(32, 2, true);
        view.setUint16(34, 16, true);
        writeStr(36, 'data');
        view.setUint32(40, numSamples * 2, true);

        for (let i = 0; i < numSamples; i++) {
          const s = Math.max(-1, Math.min(1, trimmed[i]));
          view.setInt16(44 + i * 2, s < 0 ? s * 0x8000 : s * 0x7FFF, true);
        }

        resolve(buffer);
      }
    } catch (e) {
      reject(e);
    }
  });
}

export interface PopupData {
  title: string;
  body: string;
  meta: string;
  accent: string;
  hasMeta: boolean;
  style?: React.CSSProperties;
}

export interface WakeConfig {
  enabled: boolean;
  threshold: number;
  trained: boolean;
  templates: { frames: number; durationSec: number }[];
}

export type ScreenKey = 'home' | 'voice' | 'chat' | 'network' | 'devices' | 'system' | 'terminal' | 'files' | 'tasks' | 'settings' | 'dev';

export interface SystemMetrics {
  cpu: number;
  ram: number;
  ramTotal: string;
  ramUsed: string;
  ramFree: string;
  gpu: number;
  vramUsed: string;
  vramLimit: string;
  uptime: string;
  processes: ProcessInfo[];
  tasks: TaskLoad[];
}

export interface FloatingWindow {
  id: string;
  kind: 'browser' | 'camera';
  url: string;
  title: string;
  x: number;
  y: number;
  width: number;
  height: number;
  zIndex: number;
}

export interface AssistantState {
  screen: ScreenKey;
  listening: boolean;
  speaking: boolean;
  transcript: string;
  ariaReply: string;
  popups: PopupData[];
  messages: Message[];
  input: string;
  thinking: boolean;
  termLines: TermLine[];
  termInput: string;
  selPacket: number;
  rate: number;
  wake: boolean;
  wakeConfig: WakeConfig;
  lastWakeScore: number;
  setWakeThreshold: (v: number) => void;
  setWakeScore: (v: number) => void;
  tasks: TaskItem[];
  metrics: SystemMetrics;
  packets: Packet[];
  highlightIds: Set<string>;
  captureRunning: boolean;
  interfaces: NetworkInterface[];
  currentInterface: string | null;
  stateRef: React.RefObject<{ screen: string; listening: boolean; speaking: boolean }>;
  go: (scr: ScreenKey) => void;
  goHome: () => void;
  enterVoice: () => void;
  exitVoice: () => void;
  retryListen: () => void;
  setInput: (v: string) => void;
  sendMsg: (textOverride?: string) => void;
  chatImage: string | null;
  setChatImage: (v: string | null) => void;
  username: string;
  role: string;
  isAdmin: boolean;
  setTermInput: (v: string) => void;
  submitTerm: () => void;
  toggleTask: (id: string) => void;
  runTask: (id: string) => void;
  refreshInterfaces: () => void;
  selectInterface: (ip: string) => void;
  llm: LlmConfig;
  refreshLlm: () => void;
  saveLlm: (opts: { provider: 'local' | 'api'; local?: { url?: string; model?: string }; api?: { baseUrl?: string; model?: string; key?: string } }) => void;
  asrModels: AsrModels | null;
  speechCheck: AsrCheck | null;
  requestAsrModels: () => void;
  sendAsrCheck: (provider: string, model: string) => void;
  ttsConf: TtsConf | null;
  ttsModels: TtsModels | null;
  ttsCheck: TtsCheck | null;
  requestTts: () => void;
  requestTtsModels: () => void;
  sendTtsSet: (engine: 'local' | 'api', voice: string) => void;
  sendTtsCheck: (engine: string, voice: string) => void;
  // LLM Profile & Capabilities
  llmProfile: UserLlmProfile | null;
  llmCapabilities: LlmCapabilities | null;
  llmPing: Record<string, { ok: boolean; ms: number; model: string } | undefined>;
  llmResolve: Record<string, { provider: string; model: string; ok: boolean } | undefined>;
  camSnapshot: { ip: string; data: string; ok: boolean; message: string } | null;
  camAnalyze: { ip: string; ok: boolean; result: string } | null;
  saveLlmProfile: (profile: UserLlmProfile) => void;
  requestLlmProfile: () => void;
  requestLlmCapabilities: (refresh?: boolean) => void;
  sendLlmPing: (provider: string, model: string) => void;
  sendLlmResolve: (task: string) => void;
  sendCameraSnapshot: (ip: string, user?: string, pass?: string) => void;
  sendCameraAnalyze: (ip: string) => void;
  trafficReport: TrafficReport | null;
  trafficExplanation: string | null;
  requestInstantAnalysis: () => void;
  requestDailyAnalysis: () => void;
  requestWeeklyAnalysis: () => void;
  explainTrafficItem: (context: string, relatedIp?: string, explainType?: string) => void;
  setSelPacket: (i: number) => void;
  setRate: (v: number) => void;
  setWake: () => void;
  connected: boolean;
  devAuthed: boolean;
  devCanChange: boolean;
  devAuthError: string;
  devMessages: DevMessage[];
  devInput: string;
  devThinking: boolean;
  devChanges: DevFileChange[] | null;
  devAgents: DevAgentInfo[];
  devModels: string[];
  devAgent: string;
  devModel: string;
  devThinkingText: string;
  devReasoning: string;
  devAuth: (pw: string) => void;
  devChangePassword: (current: string, next: string) => void;
  setDevInput: (v: string) => void;
  devSendMsg: () => void;
  devApplyChanges: () => void;
  devRejectChanges: () => void;
  devNewChat: () => void;
  setDevAgent: (v: string) => void;
  setDevModel: (v: string) => void;
  conversations: ConversationMeta[];
  activeConversation: string | null;
  chatThinking: boolean;
  chatThinkingText: string;
  chatReasoning: string;
  contextMenu: {x:number;y:number;index:number;role:'user'|'assistant';text:string}|null;
  likedMessages: Set<number>;
  dislikedMessages: Set<number>;
  editingIndex: number|null;
  createChat: (title?: string) => void;
  switchChat: (id: string) => void;
  deleteChat: (id: string) => void;
  copyToClipboard: (text: string) => void;
  readAloud: (text: string) => void;
  shareMessage: (text: string) => void;
  likeMessage: (idx: number) => void;
  dislikeMessage: (idx: number) => void;
  closeContextMenu: () => void;
  setContextMenu: (v: {x:number;y:number;index:number;role:'user'|'assistant';text:string}|null) => void;
  setEditingIndex: (v: number|null) => void;
  // FS
  fsEntries: FsEntry[];
  fsPath: string;
  fsParent: string | null;
  fsContent: FsReadResult | null;
  fsReadPath: string | null;
  fsLoading: boolean;
  fsList: (path?: string) => void;
  fsRead: (path: string) => void;
  // Users
  users: UserEntry[];
  refreshUsers: () => void;
  createUser: (username: string, password: string, role?: string) => void;
  deleteUser: (username: string) => void;
  setUserRole: (username: string, role: string) => void;
  changePassword: (current: string, next: string) => void;
  userResult: UserResult | null;
  // Traffic
  trafficFiltered: Packet[];
  trafficFilter: (filter: string) => void;
  trafficExport: (format: 'csv' | 'pcap') => void;
  // Wake
  wakeTrainResult: WakeTrainResult | null;
  wakeTrain: (audioB64: string) => void;
  wakeReset: () => void;
  // Devices
  devices: Device[];
  devicesScanning: boolean;
  registerIp: (ip: string, name: string) => void;
  // Floating windows
  floatingWindows: FloatingWindow[];
  openFloatingWindow: (kind: 'browser' | 'camera', url: string, title: string) => void;
  closeFloatingWindow: (id: string) => void;
  focusFloatingWindow: (id: string) => void;
}

export function useAssistant(ws: WebSocket | null): AssistantState {
  const [screen, setScreen] = useState<ScreenKey>('home');
  const [listening, setListening] = useState(false);
  const [speaking, setSpeaking] = useState(false);
  const [transcript, setTranscript] = useState('');
  const [ariaReply, setAriaReply] = useState('Готова помочь. Слушаю…');
  const [popups, setPopups] = useState<PopupData[]>([]);

  const [messages, setMessages] = useState<Message[]>([]);
  const [input, setInput] = useState('');
  const [chatImage, setChatImage] = useState<string | null>(null);
  const [thinking, setThinking] = useState(false);

  const [termLines, setTermLines] = useState<TermLine[]>([]);
  const [termInput, setTermInput] = useState('');

  const [selPacket, setSelPacket] = useState(-1);
  const [rate, setRate] = useState(1.0);
  const [wake, setWake] = useState(true);
  const [wakeConfig, setWakeConfig] = useState<WakeConfig>({ enabled: true, threshold: 0.3, trained: false, templates: [] });
  const [lastWakeScore, setWakeScoreState] = useState(0);
  const [tasks, setTasks] = useState<TaskItem[]>([]);
  const [connected, setConnected] = useState(false);

  const [metrics, setMetrics] = useState<SystemMetrics>({ cpu: 0, ram: 0, ramTotal: '', ramUsed: '', ramFree: '', gpu: 0, vramUsed: '', vramLimit: '', uptime: '', processes: [], tasks: [] });
  const [packets, setPackets] = useState<Packet[]>([]);
  const [captureRunning, setCaptureRunning] = useState(true);
  const [highlightIds, setHighlightIds] = useState<Set<string>>(new Set());
  const [interfaces, setInterfaces] = useState<NetworkInterface[]>([]);
  const [currentInterface, setCurrentInterface] = useState<string | null>(null);

  const [devAuthed, setDevAuthed] = useState(false);
  const [devCanChange, setDevCanChange] = useState(false);
  const [devAuthError, setDevAuthError] = useState('');
  const [devMessages, setDevMessages] = useState<DevMessage[]>([]);
  const [devInput, setDevInput] = useState('');
  const [devThinking, setDevThinking] = useState(false);
  const [devChanges, setDevChanges] = useState<DevFileChange[] | null>(null);
  const [devAgents, setDevAgents] = useState<DevAgentInfo[]>([]);
  const [devModels, setDevModels] = useState<string[]>([]);
  const [devAgent, setDevAgentState] = useState<string>(() => localStorage.getItem('ai_dev_agent') || 'build');
  const [devModel, setDevModelState] = useState<string>(() => localStorage.getItem('ai_dev_model') || '');
  const [devThinkingText, setDevThinkingText] = useState('');
  const [devReasoning, setDevReasoning] = useState('');

  const [llm, setLlm] = useState<LlmConfig>({
    provider: 'local',
    local: { url: '', model: '' },
    api: { baseUrl: '', model: '' },
    status: null,
  });

  const [llmProfile, setLlmProfile] = useState<UserLlmProfile | null>(null);
  const [llmCapabilities, setLlmCapabilities] = useState<LlmCapabilities | null>(null);
  const [llmPing, setLlmPing] = useState<Record<string, { ok: boolean; ms: number; model: string } | undefined>>({});
  const [llmResolve, setLlmResolve] = useState<Record<string, { provider: string; model: string; ok: boolean } | undefined>>({});
  const [asrModels, setAsrModels] = useState<AsrModels | null>(null);
  const [speechCheck, setSpeechCheck] = useState<AsrCheck | null>(null);
  const [ttsConf, setTtsConf] = useState<TtsConf | null>(null);
  const [ttsModels, setTtsModels] = useState<TtsModels | null>(null);
  const [ttsCheck, setTtsCheck] = useState<TtsCheck | null>(null);
  const [camSnapshot, setCamSnapshot] = useState<{ ip: string; data: string; ok: boolean; message: string } | null>(null);
  const [camAnalyze, setCamAnalyze] = useState<{ ip: string; ok: boolean; result: string } | null>(null);

  const [trafficReport, setTrafficReport] = useState<TrafficReport | null>(null);
  const [trafficExplanation, setTrafficExplanation] = useState<string | null>(null);
  const [conversations, setConversations] = useState<ConversationMeta[]>([]);
  const [activeConversation, setActiveConversation] = useState<string | null>(null);
  const [chatThinking, setChatThinking] = useState(false);
  const [chatThinkingText, setChatThinkingText] = useState('');
  const [chatReasoning, setChatReasoning] = useState('');
  const chatReasoningRef = useRef('');
  const [contextMenu, setContextMenu] = useState<{x:number;y:number;index:number;role:'user'|'assistant';text:string}|null>(null);
  const [likedMessages, setLikedMessages] = useState<Set<number>>(new Set());
  const [dislikedMessages, setDislikedMessages] = useState<Set<number>>(new Set());
  const [editingIndex, setEditingIndex] = useState<number|null>(null);

  const [fsEntries, setFsEntries] = useState<FsEntry[]>([]);
  const [fsPath, setFsPath] = useState('/');
  const [fsParent, setFsParent] = useState<string | null>(null);
  const [fsContent, setFsContent] = useState<FsReadResult | null>(null);
  const [fsReadPath, setFsReadPath] = useState<string | null>(null);
  const [fsLoading, setFsLoading] = useState(false);
  const [users, setUsers] = useState<UserEntry[]>([]);
  const [trafficFiltered, setTrafficFiltered] = useState<Packet[]>([]);
  const [wakeTrainResult, setWakeTrainResult] = useState<WakeTrainResult | null>(null);
  const [userResult, setUserResult] = useState<UserResult | null>(null);
  const [devices, setDevices] = useState<Device[]>([]);
  const [devicesScanning, setDevicesScanning] = useState(false);
  const [floatingWindows, setFloatingWindows] = useState<FloatingWindow[]>([]);
  const floatingZRef = useRef(10);
  const openFloatingWindowRef = useRef<(kind: 'browser' | 'camera', url: string, title: string) => void>(() => {});

  const stateRef = useRef({ screen, listening, speaking });
  stateRef.current = { screen, listening, speaking };

  const session = getSession();
  const username = session?.username ?? '';
  const role = session?.role ?? 'user';
  const isAdmin = role === 'admin';

  useEffect(() => {
    if (screen !== 'dev') {
      setDevAuthed(false);
      setDevAuthError('');
    }
  }, [screen]);

  const devReasoningRef = useRef('');

  const mediaStreamRef = useRef<MediaStream | null>(null);
  const abortListenRef = useRef<AbortController | null>(null);
  const speakTimer = useRef<ReturnType<typeof setTimeout>>();
  const audioRef = useRef<HTMLAudioElement | null>(null);

  const popupStyleFor = (pos: number): React.CSSProperties => {
    const map: React.CSSProperties[] = [
      { top: '15%', left: '7%' },
      { top: '30%', right: '8%' },
      { bottom: '26%', left: '10%' },
      { top: '18%', right: '30%' },
    ];
    return {
      position: 'absolute', width: 270, padding: 18, borderRadius: 16,
      background: 'rgba(12,18,28,0.82)', border: '1px solid rgba(255,255,255,0.1)',
      backdropFilter: 'blur(14px)', boxShadow: '0 16px 50px rgba(0,0,0,0.5)',
      animation: `ariaPop .5s ease both, ariaFloat 5s ease-in-out infinite`,
      animationDelay: `${pos * 0.15}s, ${pos * 0.4}s`,
      ...map[pos % map.length],
    };
  };

  // WS status
  useEffect(() => {
    if (!ws) return;
    const onOpen = () => setConnected(true);
    const onClose = () => {
      setConnected(false);
      setDevAuthed(false);
      setDevAuthError('');
    };
    ws.addEventListener('open', onOpen);
    ws.addEventListener('close', onClose);
    if (ws.readyState === WebSocket.OPEN) setConnected(true);
    return () => {
      ws.removeEventListener('open', onOpen);
      ws.removeEventListener('close', onClose);
    };
  }, [ws]);

  // WS messages → all state
  useEffect(() => {
    if (!ws) return;
    const handler = (e: MessageEvent) => {
      try {
        const parsed = JSON.parse(e.data);

        switch (parsed.type) {
          case 'chat':
            if (parsed.done) {
              const finalText = parsed.text || chatReasoningRef.current || 'Готово.';
              setMessages(prev => [...prev, { role: 'assistant', text: finalText }]);
              chatReasoningRef.current = '';
              setChatReasoning('');
              setChatThinking(false);
              setChatThinkingText('');
              setThinking(false);
            }
            break;

          case 'chat:history':
            if (Array.isArray(parsed.messages)) {
              setMessages(
                parsed.messages
                  .filter((m: any) => m && typeof m.text === 'string')
                  .map((m: any) => ({ role: m.role === 'user' ? 'user' : 'assistant', text: m.text, ...(typeof m.image === 'string' && m.image ? { image: m.image } : {}) })),
              );
            }
            break;

          case 'chat:history:cleared':
            setMessages([]);
            break;

          case 'chat:list':
            if (Array.isArray(parsed.conversations)) setConversations(parsed.conversations);
            break;
          case 'chat:created':
            setConversations(prev => [...prev, { id: parsed.id, title: parsed.title, createdAt: parsed.createdAt, lastMessageAt: parsed.lastMessageAt }]);
            setActiveConversation(parsed.id);
            break;
          case 'chat:switched':
            setActiveConversation(parsed.conversationId);
            if (Array.isArray(parsed.messages)) {
              setMessages(parsed.messages.filter((m: any) => m?.text).map((m: any) => ({ role: m.role === 'user' ? 'user' : 'assistant', text: m.text, ...(typeof m.image === 'string' && m.image ? { image: m.image } : {}) })));
            }
            break;
          case 'chat:deleted':
            setConversations(prev => prev.filter(c => c.id !== parsed.conversationId));
            if (activeConversation === parsed.conversationId) setActiveConversation(null);
            break;
          case 'chat:renamed':
            setConversations(prev => prev.map(c => c.id === parsed.conversationId ? { ...c, title: parsed.title } : c));
            break;
          case 'chat:thinking':
            setChatThinking(true);
            setChatThinkingText(parsed.text || 'Думаю…');
            break;
          case 'chat:reasoning':
            if (parsed.text) { chatReasoningRef.current += parsed.text; setChatReasoning(chatReasoningRef.current); }
            break;

          case 'terminal':
            if (parsed.text) {
              setTermLines((prev) => [...prev, { text: parsed.text, color: parsed.color || '#9fb0c4' }]);
            }
            break;

          case 'log':
            break;

          case 'popup':
            setPopups((prev) => {
              const p: PopupData = {
                title: parsed.title || '',
                body: parsed.body || '',
                meta: parsed.meta || '',
                accent: parsed.accent || '#ff6b5b',
                hasMeta: parsed.hasMeta || false,
                style: popupStyleFor(prev.length),
              };
              return [p, ...prev].slice(0, 4).map((x, i) => ({ ...x, style: popupStyleFor(i) }));
            });
            break;

          case 'nav':
            if (parsed.screen) setScreen(parsed.screen as ScreenKey);
            break;

          case 'system:stats':
            setMetrics({
              cpu: parsed.cpu ?? 0,
              ram: parsed.ram ?? 0,
              ramTotal: parsed.ramTotal ?? '',
              ramUsed: parsed.ramUsed ?? '',
              ramFree: parsed.ramFree ?? '',
              gpu: parsed.gpu ?? 0,
              vramUsed: parsed.vramUsed ?? '',
              vramLimit: parsed.vramLimit ?? '',
              uptime: parsed.uptime ?? '',
              processes: parsed.processes ?? [],
              tasks: parsed.tasks ?? [],
            });
            break;

          case 'traffic:log':
            if (parsed.records) {
              const incoming = (parsed.records as any[]).map((r: any) => ({
                no: Number(r.seq ?? 0),
                time: r.time,
                src: r.src,
                dst: r.dst,
                proto: r.proto,
                sport: r.sport,
                dport: r.dport,
                ttl: r.ttl,
                raw: r.raw,
                len: r.len,
                info: r.msg || '-',
                seq: Number(r.seq ?? 0),
              }));
              setPackets((prev) => {
                if (parsed.replace !== false) return incoming;
                const lastSeq = prev.length > 0 ? prev[prev.length - 1].seq ?? 0 : 0;
                const fresh = incoming.filter((pk: Packet) => (pk.seq ?? 0) > lastSeq);
                if (fresh.length === 0) return prev;
                const merged = [...prev, ...fresh];
                return merged.length > MAX_PACKETS ? merged.slice(merged.length - MAX_PACKETS) : merged;
              });
            }
            break;

          case 'traffic:capture':
            setCaptureRunning(!!parsed.running);
            break;

          case 'traffic:cleared':
            setPackets([]);
            setHighlightIds(new Set());
            break;

          case 'traffic:interfaces':
            if (Array.isArray(parsed.interfaces)) setInterfaces(parsed.interfaces);
            setCurrentInterface(parsed.currentIp || null);
            break;

          case 'traffic:interface:set':
            if (parsed.ok) setCurrentInterface(parsed.ip || null);
            break;

          case 'traffic:query':
            if (parsed.records) {
              const ids = new Set<string>();
              for (const r of parsed.records) {
                ids.add(`${r.time}|${r.src}|${r.dst}|${r.proto}|${r.len}`);
              }
              setHighlightIds(ids);
            }
            break;

          case 'traffic:patterns':
            break;

          case 'traffic:patterns_cleared':
            break;

          case 'traffic:report':
            setTrafficReport(parsed.report ?? null);
            break;

          case 'traffic:explanation':
            setTrafficExplanation(parsed.text ?? null);
            break;

          case 'tasks:list':
            if (parsed.tasks) setTasks(parsed.tasks);
            break;

          case 'ip-registry':
            break;

          case 'artnet:poll-result':
            break;

          case 'device:test-result':
            break;

          case 'voice:transcription':
            if (parsed.text) setTranscript(parsed.text);
            break;

          case 'voice:response':
            if (parsed.text) {
              setAriaReply(parsed.text);
              setMessages((prev) => [...prev, { role: 'assistant', text: parsed.text }]);
              speak(parsed.text);
            }
            setListening(false);
            break;

          case 'voice:audio':
            if (parsed.audio) {
              try {
                const binary = atob(parsed.audio);
                const bytes = new Uint8Array(binary.length);
                for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
                const blob = new Blob([bytes], { type: 'audio/wav' });
                const url = URL.createObjectURL(blob);
                const audio = new Audio(url);
                audioRef.current = audio;
                const cleanup = () => { URL.revokeObjectURL(url); setSpeaking(false); audioRef.current = null; };
                audio.onended = cleanup;
                audio.onerror = cleanup;
                audio.play().catch(cleanup);
                setSpeaking(true);
              } catch {}
            }
            break;

          case 'voice:speak:done':
            if (!parsed.ok) setSpeaking(false);
            break;

          case 'dev:auth:result':
            setDevAuthed(!!parsed.success);
            setDevCanChange(!!parsed.canChange);
            setDevAuthError(parsed.success ? '' : 'Неверный пароль.');
            if (parsed.success && ws?.readyState === WebSocket.OPEN) {
              ws.send(JSON.stringify({ type: 'dev:options' }));
            }
            break;

          case 'dev:options:result':
            if (Array.isArray(parsed.agents)) setDevAgents(parsed.agents);
            if (Array.isArray(parsed.models)) setDevModels(parsed.models);
            break;

          case 'dev:history':
            if (Array.isArray(parsed.messages)) setDevMessages(parsed.messages);
            break;

          case 'dev:thinking':
            setDevThinking(true);
            setDevThinkingText(parsed.text || '');
            break;

          case 'dev:reasoning':
            if (parsed.text) {
              devReasoningRef.current += parsed.text;
              setDevReasoning(devReasoningRef.current);
            }
            break;

          case 'dev:response':
            if (parsed.text) {
              setDevMessages((prev) => [
                ...prev,
                { role: 'assistant', text: parsed.text, reasoning: devReasoningRef.current || undefined },
              ]);
            }
            devReasoningRef.current = '';
            setDevReasoning('');
            setDevThinking(false);
            break;

          case 'dev:done':
            devReasoningRef.current = '';
            setDevReasoning('');
            setDevThinking(false);
            setDevThinkingText('');
            break;

          case 'dev:changes':
            setDevChanges(Array.isArray(parsed.files) ? parsed.files : null);
            break;

          case 'dev:applied':
            setDevChanges(null);
            setDevThinking(false);
            setDevThinkingText('');
            break;

          case 'dev:error':
            if (parsed.message) setDevMessages((prev) => [...prev, { role: 'assistant', text: '⚠ ' + parsed.message }]);
            devReasoningRef.current = '';
            setDevReasoning('');
            setDevThinking(false);
            setDevThinkingText('');
            break;

          case 'settings:llm':
            setLlm((prev) => ({
              provider: parsed.provider === 'api' ? 'api' : 'local',
              local: { url: parsed.local?.url ?? '', model: parsed.local?.model ?? '' },
              api: { baseUrl: parsed.api?.baseUrl ?? '', model: parsed.api?.model ?? '' },
              status: prev.status,
            }));
            break;

          case 'settings:llm:status':
            setLlm((prev) => ({
              ...prev,
              status: { ok: !!parsed.ok, ms: parsed.ms ?? 0, model: parsed.model ?? '' },
            }));
            break;

          case 'settings:llm:set':
            setPopups((prev) => {
              const p: PopupData = {
                title: 'ИИ-модель',
                body: parsed.ok ? 'Настройки сохранены.' : ('Ошибка: ' + (parsed.error || 'неизвестная')),
                meta: '',
                accent: parsed.ok ? '#4cd97b' : '#ff6b5b',
                hasMeta: false,
                style: popupStyleFor(prev.length),
              };
              return [p, ...prev].slice(0, 4).map((x, i) => ({ ...x, style: popupStyleFor(i) }));
            });
            break;

          case 'wake:config':
            setWakeConfig({
              enabled: !!parsed.enabled,
              threshold: typeof parsed.threshold === 'number' ? parsed.threshold : 0.3,
              trained: !!parsed.trained,
              templates: Array.isArray(parsed.templates) ? parsed.templates : [],
            });
            setWake(!!parsed.enabled);
            break;

          case 'fs:list:result':
            if (Array.isArray(parsed.entries)) setFsEntries(parsed.entries);
            if (typeof parsed.path === 'string') setFsPath(parsed.path);
            if ('parent' in parsed) setFsParent(parsed.parent ?? null);
            setFsLoading(false);
            break;

          case 'fs:read:result':
            setFsContent({
              path: parsed.path ?? null,
              content: parsed.content ?? null,
              truncated: !!parsed.truncated,
              size: parsed.size ?? 0,
              binary: !!parsed.binary,
            });
            setFsReadPath(parsed.path ?? null);
            setFsLoading(false);
            break;

          case 'user:list:result':
            if (Array.isArray(parsed.users)) setUsers(parsed.users);
            break;

          case 'user:result':
            setUserResult({ ok: !!parsed.ok, error: parsed.error ?? null });
            break;

          case 'traffic:filter:result':
            if (Array.isArray(parsed.records)) {
              setTrafficFiltered(parsed.records.map((r: any) => ({
                no: Number(r.no ?? r.seq ?? 0),
                time: r.time ?? '',
                src: r.src ?? '',
                dst: r.dst ?? '',
                proto: r.proto ?? '',
                sport: Number(r.sport ?? 0),
                dport: Number(r.dport ?? 0),
                ttl: Number(r.ttl ?? 0),
                raw: r.raw ?? null,
                len: Number(r.len ?? 0),
                info: r.info ?? '-',
                seq: Number(r.seq ?? 0),
              })));
            }
            break;

          case 'traffic:export:result':
            if (parsed.ok && parsed.data) {
              const bin = atob(parsed.data);
              const bytes = new Uint8Array(bin.length);
              for (let i = 0; i < bin.length; i++) bytes[i] = bin.charCodeAt(i);
              const ext = parsed.filename?.endsWith('.pcap') ? 'application/vnd.tcpdump.pcap' : 'text/csv';
              const blob = new Blob([bytes], { type: ext });
              const url = URL.createObjectURL(blob);
              const a = document.createElement('a');
              a.href = url;
              a.download = parsed.filename || 'traffic.csv';
              document.body.appendChild(a);
              a.click();
              document.body.removeChild(a);
              URL.revokeObjectURL(url);
            }
            break;

          case 'wake:train:result':
            setWakeTrainResult({
              ok: !!parsed.ok,
              frames: parsed.frames ?? 0,
              durationSec: parsed.durationSec ?? 0,
              templatesCount: parsed.templatesCount ?? 0,
              error: parsed.error ?? null,
            });
            break;

          case 'wake:reset:result':
            break;

          case 'devices:list':
            if (parsed.devices) setDevices(parsed.devices);
            break;

          case 'devices:found':
            if (parsed.device) {
              const found = parsed.device as Device;
              setDevices(prev => {
                const idx = prev.findIndex(d => d.ip === found.ip);
                if (idx < 0) return [...prev, found];
                const next = [...prev];
                next[idx] = found;
                return next;
              });
            }
            break;

          case 'devices:scanning':
            setDevicesScanning(!!parsed.scanning);
            break;

          case 'ui:window:open':
            openFloatingWindowRef.current(parsed.kind, parsed.url, parsed.title);
            break;

          case 'llm:profile':
            if (parsed.profile) setLlmProfile(parsed.profile);
            break;

          case 'llm:capabilities':
            if (parsed.caps) setLlmCapabilities(parsed.caps);
            break;

          case 'llm:profile:saved':
            if (parsed.ok) {
              requestLlmProfile();
            } else {
              setPopups((prev) => {
                const p: PopupData = {
                  title: 'ИИ-модели',
                  body: 'Ошибка сохранения профиля',
                  meta: '',
                  accent: '#ff6b5b',
                  hasMeta: false,
                  style: popupStyleFor(prev.length),
                };
                return [p, ...prev].slice(0, 4).map((x, i) => ({ ...x, style: popupStyleFor(i) }));
              });
            }
            break;

          case 'llm:ping:result':
            setLlmPing(prev => ({ ...prev, [`${parsed.provider}:${parsed.model}`]: { ok: !!parsed.ok, ms: parsed.ms ?? 0, model: parsed.model ?? '' } }));
            break;

          case 'llm:resolve:result':
            setLlmResolve(prev => ({ ...prev, [parsed.task]: { provider: parsed.provider, model: parsed.model, ok: !!parsed.ok } }));
            break;

          case 'asr:models':
            if (parsed && Array.isArray(parsed.local) && Array.isArray(parsed.api)) {
              setAsrModels({ local: (parsed.local as string[]).map(String), api: (parsed.api as string[]).map(String) });
            }
            break;

          case 'asr:check:result':
            setSpeechCheck({
              provider: parsed.provider ?? '',
              model: parsed.model ?? '',
              ok: !!parsed.ok,
              ms: Number(parsed.ms ?? 0),
              engine: parsed.engine ?? '',
              ...(parsed.message ? { message: String(parsed.message) } : {}),
            });
            break;

          case 'voice:error':
            setAriaReply(parsed.message ?? 'Ошибка распознавания');
            setListening(false);
            setSpeaking(false);
            break;

          case 'tts:models':
            if (parsed && Array.isArray(parsed.local) && Array.isArray(parsed.api)) {
              setTtsModels({ local: (parsed.local as string[]).map(String), api: (parsed.api as string[]).map(String) });
            }
            break;

          case 'tts:conf':
            setTtsConf({
              engine: parsed.engine === 'api' ? 'api' : 'local',
              voice: parsed.voice ?? '',
              ok: !!parsed.ok,
            });
            break;

          case 'tts:set:result':
            setPopups((prev) => {
              const p: PopupData = {
                title: 'Озвучка',
                body: parsed.ok ? 'Голос сохранён' : ('Ошибка: ' + (parsed.error || 'неизвестная')),
                meta: '',
                accent: parsed.ok ? '#4cd97b' : '#ff6b5b',
                hasMeta: false,
                style: popupStyleFor(prev.length),
              };
              return [p, ...prev].slice(0, 4).map((x, i) => ({ ...x, style: popupStyleFor(i) }));
            });
            break;

          case 'tts:check:result':
            setTtsCheck({
              engine: parsed.engine ?? '',
              voice: parsed.voice ?? '',
              ok: !!parsed.ok,
              ms: Number(parsed.ms ?? 0),
              ...(parsed.message ? { message: String(parsed.message) } : {}),
            });
            break;

          case 'camera:snapshot:result':
            setCamSnapshot({ ip: parsed.ip, data: parsed.data ?? '', ok: !!parsed.ok, message: parsed.message || '' });
            break;

          case 'camera:analyze:result':
            setCamAnalyze({ ip: parsed.ip, ok: !!parsed.ok, result: parsed.result ?? '' });
            break;
        }
      } catch {}
    };
    ws.addEventListener('message', handler);
    return () => ws.removeEventListener('message', handler);
  }, [ws]);

  useEffect(() => () => {
    try { mediaStreamRef.current?.getTracks().forEach(t => t.stop()); audioRef.current?.pause(); } catch {}
  }, []);

  const speak = useCallback((text: string) => {
    setSpeaking(true);
    clearTimeout(speakTimer.current);

    if (ws?.readyState === WebSocket.OPEN) {
      ws.send(JSON.stringify({ type: 'voice:speak', text, rate }));
    } else {
      setSpeaking(false);
    }

    speakTimer.current = setTimeout(() => setSpeaking(false), Math.min(9000, 1400 + text.length * 55));
  }, [ws, rate]);

  const go = useCallback((scr: ScreenKey) => {
    try { mediaStreamRef.current?.getTracks().forEach(t => t.stop()); } catch {}
    setScreen(scr);
    if (scr !== 'voice') setListening(false);
  }, []);

  const startListen = useCallback(async () => {
    setTranscript('');
    setAriaReply('Слушаю…');
    setListening(true);
    setSpeaking(false);

    abortListenRef.current?.abort();
    abortListenRef.current = new AbortController();
    const signal = abortListenRef.current.signal;

    let audioData: ArrayBuffer | null = null;
    try {
      audioData = await recordUntilSilence(5000, 16000, signal);
    } catch {
      if (signal.aborted) return;
      setAriaReply('Нет доступа к микрофону. Разрешите доступ в настройках браузера, затем попробуйте снова, или введите текст ниже.');
      setListening(false);
      return;
    }

    if (signal.aborted || stateRef.current.screen !== 'voice') return;

    if (audioData.byteLength === 0) {
      setAriaReply('Не удалось распознать речь. Попробуйте ещё раз или введите текст ниже.');
      setListening(false);
      return;
    }

    setAriaReply('Распознаю через Whisper…');

    if (ws?.readyState === WebSocket.OPEN) {
      ws.send(audioData);
    } else {
      setAriaReply('Нет соединения с сервером. Попробуйте ещё раз.');
      setListening(false);
    }
  }, [ws]);

  const retryListen = useCallback(() => {
    setTranscript('');
    setAriaReply('Повторная попытка…');
    startListen();
  }, [startListen]);

  const goHome = useCallback(() => go('home'), [go]);

  const enterVoice = useCallback(() => {
    setScreen('voice');
    setTranscript('');
    setAriaReply('Слушаю вас…');
    setPopups([]);
    speak('Слушаю вас.');
    startListen();
  }, [startListen, speak]);

  const exitVoice = useCallback(() => {
    abortListenRef.current?.abort();
    try { mediaStreamRef.current?.getTracks().forEach(t => t.stop()); audioRef.current?.pause(); } catch {}
    setScreen('home');
    setListening(false);
    setSpeaking(false);
  }, []);

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape' && screen === 'voice') exitVoice(); };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [screen, exitVoice]);

  const sendMsg = useCallback((textOverride?: string) => {
    const text = (textOverride ?? input).trim();
    const img = chatImage;
    if (!text && !img) return;
    setInput('');
    setChatImage(null);
    setThinking(true);
    setMessages((prev) => [...prev, { role: 'user', text, ...(img ? { image: img } : {}) }]);

    if (ws?.readyState === WebSocket.OPEN) {
      ws.send(JSON.stringify({ type: 'chat', text, conversationId: activeConversation, ...(img ? { images: [img] } : {}) }));
    } else {
      setThinking(false);
      setMessages((prev) => [...prev, { role: 'assistant', text: 'Нет соединения с сервером.' }]);
    }
  }, [input, chatImage, ws, activeConversation]);

  const submitTerm = useCallback(() => {
    const cmd = termInput.trim();
    if (!cmd) return;
    setTermLines((prev) => [...prev, { text: 'aria ➜ ' + cmd, color: '#ff6b5b' }]);
    setTermInput('');

    if (ws?.readyState === WebSocket.OPEN) {
      ws.send(JSON.stringify({ type: 'terminal:exec', command: cmd }));
    } else {
      setTermLines((prev) => [...prev, { text: 'Нет соединения с сервером.', color: '#ff5470' }]);
    }
  }, [termInput, ws]);

  const toggleTask = useCallback((id: string) => {
    if (ws?.readyState === WebSocket.OPEN) {
      ws.send(JSON.stringify({ type: 'tasks:toggle', id }));
    }
  }, [ws]);

  const runTask = useCallback((id: string) => {
    if (ws?.readyState === WebSocket.OPEN) {
      ws.send(JSON.stringify({ type: 'tasks:run', id }));
    }
  }, [ws]);

  const refreshInterfaces = useCallback(() => {
    if (ws?.readyState === WebSocket.OPEN) {
      ws.send(JSON.stringify({ type: 'traffic:interfaces' }));
    }
  }, [ws]);

  const selectInterface = useCallback((ip: string) => {
    if (ws?.readyState === WebSocket.OPEN) {
      ws.send(JSON.stringify({ type: 'traffic:interface:set', ip }));
    }
  }, [ws]);

  const refreshLlm = useCallback(() => {
    if (ws?.readyState === WebSocket.OPEN) {
      ws.send(JSON.stringify({ type: 'settings:llm:get' }));
    }
  }, [ws]);

  const saveLlm = useCallback((opts: { provider: 'local' | 'api'; local?: { url?: string; model?: string }; api?: { baseUrl?: string; model?: string; key?: string } }) => {
    if (ws?.readyState !== WebSocket.OPEN) return;
    const api = opts.api
      ? (opts.api.key && opts.api.key.trim()
        ? { baseUrl: opts.api.baseUrl, model: opts.api.model, key: opts.api.key.trim() }
        : { baseUrl: opts.api.baseUrl, model: opts.api.model })
      : undefined;
    ws.send(JSON.stringify({
      type: 'settings:llm:set',
      provider: opts.provider,
      ...(opts.local ? { local: opts.local } : {}),
      ...(api ? { api } : {}),
    }));
  }, [ws]);

  const requestAsrModels = useCallback(() => {
    if (ws?.readyState === WebSocket.OPEN) {
      ws.send(JSON.stringify({ type: 'asr:models' }));
    }
  }, [ws]);

  const sendAsrCheck = useCallback((provider: string, model: string) => {
    if (ws?.readyState === WebSocket.OPEN) {
      ws.send(JSON.stringify({ type: 'asr:check', provider, model }));
    }
  }, [ws]);

  const requestTts = useCallback(() => {
    if (ws?.readyState === WebSocket.OPEN) {
      ws.send(JSON.stringify({ type: 'tts:get' }));
    }
  }, [ws]);

  const requestTtsModels = useCallback(() => {
    if (ws?.readyState === WebSocket.OPEN) {
      ws.send(JSON.stringify({ type: 'tts:models' }));
    }
  }, [ws]);

  const sendTtsSet = useCallback((engine: 'local' | 'api', voice: string) => {
    if (ws?.readyState === WebSocket.OPEN) {
      ws.send(JSON.stringify({ type: 'tts:set', engine, voice }));
    }
  }, [ws]);

  const sendTtsCheck = useCallback((engine: string, voice: string) => {
    if (ws?.readyState === WebSocket.OPEN) {
      ws.send(JSON.stringify({ type: 'tts:check', engine, voice }));
    }
  }, [ws]);

  const saveLlmProfile = useCallback((profile: UserLlmProfile) => {
    if (ws?.readyState === WebSocket.OPEN) {
      ws.send(JSON.stringify({ type: 'llm:profile:save', profile }));
    }
  }, [ws]);

  const requestLlmProfile = useCallback(() => {
    if (ws?.readyState === WebSocket.OPEN) {
      ws.send(JSON.stringify({ type: 'llm:profile:get' }));
    }
  }, [ws]);

  const requestLlmCapabilities = useCallback((refresh?: boolean) => {
    if (ws?.readyState === WebSocket.OPEN) {
      ws.send(JSON.stringify({ type: 'llm:capabilities:get', ...(refresh ? { refresh: true } : {}) }));
    }
  }, [ws]);

  const sendLlmPing = useCallback((provider: string, model: string) => {
    if (ws?.readyState === WebSocket.OPEN) {
      ws.send(JSON.stringify({ type: 'llm:ping', provider, model }));
    }
  }, [ws]);

  const sendLlmResolve = useCallback((task: string) => {
    if (ws?.readyState === WebSocket.OPEN) {
      ws.send(JSON.stringify({ type: 'llm:resolve', task }));
    }
  }, [ws]);

  const sendCameraSnapshot = useCallback((ip: string, user?: string, pass?: string) => {
    if (ws?.readyState === WebSocket.OPEN) {
      ws.send(JSON.stringify({ type: 'camera:snapshot', ip, ...(user ? { user, pass } : {}) }));
    }
  }, [ws]);

  const sendCameraAnalyze = useCallback((ip: string) => {
    if (ws?.readyState === WebSocket.OPEN) {
      ws.send(JSON.stringify({ type: 'camera:analyze', ip }));
    }
  }, [ws]);

  const requestInstantAnalysis = useCallback(() => {
    setTrafficReport(null);
    if (ws?.readyState === WebSocket.OPEN) {
      ws.send(JSON.stringify({ type: 'traffic:analysis:instant' }));
    }
  }, [ws]);

  const requestDailyAnalysis = useCallback(() => {
    setTrafficReport(null);
    if (ws?.readyState === WebSocket.OPEN) {
      ws.send(JSON.stringify({ type: 'traffic:analysis:daily' }));
    }
  }, [ws]);

  const requestWeeklyAnalysis = useCallback(() => {
    setTrafficReport(null);
    if (ws?.readyState === WebSocket.OPEN) {
      ws.send(JSON.stringify({ type: 'traffic:analysis:weekly' }));
    }
  }, [ws]);

  const explainTrafficItem = useCallback((context: string, relatedIp?: string, explainType?: string) => {
    setTrafficExplanation(null);
    if (ws?.readyState === WebSocket.OPEN) {
      ws.send(JSON.stringify({ type: 'traffic:explain', context, related_ips: relatedIp ? [relatedIp] : [], explain_type: explainType || 'pattern' }));
    }
  }, [ws]);

  const devAuth = useCallback((pw: string) => {
    setDevAuthError('');
    if (ws?.readyState === WebSocket.OPEN) {
      ws.send(JSON.stringify({ type: 'dev:auth', password: pw }));
    }
  }, [ws]);

  const devChangePassword = useCallback((current: string, next: string) => {
    if (ws?.readyState === WebSocket.OPEN) {
      ws.send(JSON.stringify({ type: 'dev:change-password', current, next }));
    }
  }, [ws]);

  const devSendMsg = useCallback(() => {
    const text = devInput.trim();
    if (!text) return;
    setDevInput('');
    setDevThinking(true);
    setDevMessages((prev) => [...prev, { role: 'user', text }]);
    setDevChanges(null);
    devReasoningRef.current = '';
    setDevReasoning('');
    setDevThinkingText('');

    if (ws?.readyState === WebSocket.OPEN) {
      ws.send(JSON.stringify({ type: 'dev:chat', text, agent: devAgent, model: devModel }));
    }
  }, [devInput, ws, devAgent, devModel]);

  const devApplyChanges = useCallback(() => {
    if (!devChanges || devChanges.length === 0) return;
    if (ws?.readyState === WebSocket.OPEN) {
      ws.send(JSON.stringify({ type: 'dev:apply', files: devChanges }));
    }
  }, [devChanges, ws]);

  const devRejectChanges = useCallback(() => {
    setDevChanges(null);
    if (ws?.readyState === WebSocket.OPEN) {
      ws.send(JSON.stringify({ type: 'dev:reject' }));
    }
  }, [ws]);

  const devNewChat = useCallback(() => {
    setDevMessages([]);
    setDevChanges(null);
    setDevReasoning('');
    setDevThinkingText('');
    if (ws?.readyState === WebSocket.OPEN) {
      ws.send(JSON.stringify({ type: 'dev:new-chat' }));
    }
  }, [ws]);

  const setDevAgent = useCallback((v: string) => {
    setDevAgentState(v);
    try { localStorage.setItem('ai_dev_agent', v); } catch {}
  }, []);

  const setDevModel = useCallback((v: string) => {
    setDevModelState(v);
    try { localStorage.setItem('ai_dev_model', v); } catch {}
  }, []);

  const toggleWake = useCallback(() => {
    const next = !wake;
    setWake(next);
    if (ws?.readyState === WebSocket.OPEN) {
      ws.send(JSON.stringify({ type: 'wake:set', enabled: next }));
    }
  }, [wake, ws]);

  const setWakeThreshold = useCallback((v: number) => {
    if (ws?.readyState === WebSocket.OPEN) {
      ws.send(JSON.stringify({ type: 'wake:threshold', threshold: v }));
    }
  }, [ws]);

  const setWakeScore = useCallback((v: number) => {
    setWakeScoreState(v);
  }, []);

  const createChat = useCallback((title?: string) => { if (ws?.readyState === WebSocket.OPEN) ws.send(JSON.stringify({ type: 'chat:create', title })); }, [ws]);
  const switchChat = useCallback((id: string) => { if (ws?.readyState === WebSocket.OPEN) ws.send(JSON.stringify({ type: 'chat:switch', conversationId: id })); }, [ws]);
  const deleteChat = useCallback((id: string) => { if (ws?.readyState === WebSocket.OPEN) ws.send(JSON.stringify({ type: 'chat:delete', conversationId: id })); }, [ws]);
  const copyToClipboard = useCallback((text: string) => { navigator.clipboard.writeText(text); }, []);
  const readAloud = useCallback((text: string) => { if (ws?.readyState === WebSocket.OPEN) ws.send(JSON.stringify({ type: 'voice:speak', text })); }, [ws]);
  const shareMessage = useCallback((text: string) => { navigator.clipboard.writeText(text); }, []);
  const likeMessage = useCallback((idx: number) => { setLikedMessages(prev => { const n = new Set(prev); n.has(idx) ? n.delete(idx) : n.add(idx); return n; }); setDislikedMessages(prev => { const n = new Set(prev); n.delete(idx); return n; }); }, []);
  const dislikeMessage = useCallback((idx: number) => { setDislikedMessages(prev => { const n = new Set(prev); n.has(idx) ? n.delete(idx) : n.add(idx); return n; }); setLikedMessages(prev => { const n = new Set(prev); n.delete(idx); return n; }); }, []);
  const closeContextMenu = useCallback(() => setContextMenu(null), []);

  // ── FS actions ──

  const fsList = useCallback((path?: string) => {
    setFsLoading(true);
    if (ws?.readyState === WebSocket.OPEN) {
      ws.send(JSON.stringify({ type: 'fs:list', path: path ?? null }));
    }
  }, [ws]);

  const fsRead = useCallback((path: string) => {
    setFsLoading(true);
    setFsContent(null);
    if (ws?.readyState === WebSocket.OPEN) {
      ws.send(JSON.stringify({ type: 'fs:read', path }));
    }
  }, [ws]);

  // ── User actions ──

  const refreshUsers = useCallback(() => {
    if (ws?.readyState === WebSocket.OPEN) {
      ws.send(JSON.stringify({ type: 'user:list' }));
    }
  }, [ws]);

  const createUser = useCallback((username: string, password: string, role?: string) => {
    if (ws?.readyState === WebSocket.OPEN) {
      ws.send(JSON.stringify({ type: 'user:create', username, password, role: role ?? 'user' }));
    }
  }, [ws]);

  const deleteUser = useCallback((username: string) => {
    if (ws?.readyState === WebSocket.OPEN) {
      ws.send(JSON.stringify({ type: 'user:delete', username }));
    }
  }, [ws]);

  const setUserRole = useCallback((username: string, role: string) => {
    if (ws?.readyState === WebSocket.OPEN) {
      ws.send(JSON.stringify({ type: 'user:role', username, role }));
    }
  }, [ws]);

  const changePassword = useCallback((current: string, next: string) => {
    if (ws?.readyState === WebSocket.OPEN) {
      ws.send(JSON.stringify({ type: 'user:password', current, next }));
    }
  }, [ws]);

  // ── Device memory (IP ↔ name) ──

  const registerIp = useCallback(async (ip: string, name: string) => {
    try {
      const res = await apiFetch('/api/ip-registry', { method: 'POST', body: JSON.stringify({ ip, name }) });
      if (res.ok) {
        setDevices((prev) => prev.map((d) => (d.ip === ip ? { ...d, name } : d)));
      }
    } catch {}
  }, []);

  // ── Traffic actions ──

  const trafficFilter = useCallback((filter: string) => {
    if (ws?.readyState === WebSocket.OPEN) {
      ws.send(JSON.stringify({ type: 'traffic:filter', filter }));
    }
  }, [ws]);

  const trafficExport = useCallback((format: 'csv' | 'pcap') => {
    if (ws?.readyState === WebSocket.OPEN) {
      ws.send(JSON.stringify({ type: 'traffic:export', format }));
    }
  }, [ws]);

  // ── Wake actions ──

  const wakeTrain = useCallback((audioB64: string) => {
    setWakeTrainResult(null);
    if (ws?.readyState === WebSocket.OPEN) {
      ws.send(JSON.stringify({ type: 'wake:train', audio: audioB64 }));
    }
  }, [ws]);

  const wakeReset = useCallback(() => {
    if (ws?.readyState === WebSocket.OPEN) {
      ws.send(JSON.stringify({ type: 'wake:reset' }));
    }
  }, [ws]);

  const openFloatingWindow = useCallback((kind: 'browser' | 'camera', url: string, title: string) => {
    const z = floatingZRef.current++;
    const count = floatingWindows.length;
    const w = 640;
    const h = kind === 'camera' ? 520 : 560;
    const step = 32;
    const gap = 10;
    const cx = Math.round((window.innerWidth - w) / 2);
    const cy = Math.round((window.innerHeight - h) / 2);
    const maxX = window.innerWidth - w - gap;
    const maxY = window.innerHeight - h - gap;
    const minX = gap;
    const minY = gap;
    const cols = Math.max(1, Math.floor((maxX - minX) / step) + 1);
    const col = count % cols;
    const row = Math.floor(count / cols);
    const x = Math.max(minX, Math.min(maxX, cx + col * step));
    const y = Math.max(minY, Math.min(maxY, cy + row * step));
    setFloatingWindows(prev => [...prev, {
      id: `${Date.now()}-${Math.random().toString(36).slice(2, 7)}`,
      kind, url, title,
      x, y,
      width: w, height: h, zIndex: z,
    }]);
  }, [floatingWindows.length]);

  useEffect(() => {
    openFloatingWindowRef.current = openFloatingWindow;
  }, [openFloatingWindow]);

  const closeFloatingWindow = useCallback((id: string) => {
    setFloatingWindows(prev => prev.filter(w => w.id !== id));
  }, []);

  const focusFloatingWindow = useCallback((id: string) => {
    const z = floatingZRef.current++;
    setFloatingWindows(prev => prev.map(w => w.id === id ? { ...w, zIndex: z } : w));
  }, []);

  return {
    screen, listening, speaking, transcript, ariaReply, popups,
    messages, input, thinking, termLines, termInput,
    selPacket, rate, wake, wakeConfig, lastWakeScore, tasks,
    metrics, packets, highlightIds, captureRunning,
    interfaces, currentInterface,
    stateRef,
    go, goHome, enterVoice, exitVoice, retryListen,
    setInput, sendMsg, chatImage, setChatImage,
    username, role, isAdmin,
    setTermInput, submitTerm,
    setSelPacket, setRate, setWake: toggleWake, setWakeThreshold, setWakeScore,
    toggleTask, runTask,
    refreshInterfaces, selectInterface,
    llm, refreshLlm, saveLlm,
    asrModels, speechCheck, requestAsrModels, sendAsrCheck,
    ttsConf, ttsModels, ttsCheck, requestTts, requestTtsModels, sendTtsSet, sendTtsCheck,
    llmProfile, llmCapabilities, saveLlmProfile, requestLlmProfile, requestLlmCapabilities,
    llmPing, llmResolve, sendLlmPing, sendLlmResolve,
    camSnapshot, camAnalyze, sendCameraSnapshot, sendCameraAnalyze,
    trafficReport, trafficExplanation,
    requestInstantAnalysis, requestDailyAnalysis, requestWeeklyAnalysis, explainTrafficItem,
    connected,
    devAuthed, devCanChange, devAuthError, devMessages, devInput, devThinking, devChanges,
    devAgents, devModels, devAgent, devModel, devThinkingText, devReasoning,
    devAuth, devChangePassword, setDevInput, devSendMsg, devApplyChanges, devRejectChanges, devNewChat,
    setDevAgent, setDevModel,
    conversations, activeConversation,
    chatThinking, chatThinkingText, chatReasoning,
    contextMenu, setContextMenu,
    likedMessages, dislikedMessages,
    editingIndex, setEditingIndex,
    createChat, switchChat, deleteChat,
    copyToClipboard, readAloud, shareMessage,
    likeMessage, dislikeMessage, closeContextMenu,
    fsEntries, fsPath, fsParent, fsContent, fsReadPath, fsLoading,
    fsList, fsRead,
    users, refreshUsers, createUser, deleteUser, setUserRole, changePassword,
    trafficFiltered, trafficFilter, trafficExport,
    wakeTrainResult, wakeTrain, wakeReset,
    userResult,
    devices, devicesScanning, registerIp,
    floatingWindows, openFloatingWindow, closeFloatingWindow, focusFloatingWindow,
  };
}
