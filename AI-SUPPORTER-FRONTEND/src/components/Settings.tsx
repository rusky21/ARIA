import { useEffect, useRef, useState } from 'react';
import { C, w, R, cyanA } from '../lib/theme.js';
import { Icon, MicIcon } from './Icon.jsx';
import { Switch } from './Tasks.jsx';
import type { AssistantState, AsrModels, AsrCheck } from '../hooks/useAssistant.js';
import type { UserLlmProfile, TaskModelSelection, LlmCapabilities, ProviderCapabilities, ModelCapability, SpeechSelection, LlmConfig } from '../lib/data.js';

const TASKS = ['chat', 'voice', 'vision', 'toolCalling'];
const DEFAULT_SPEECH: SpeechSelection = { provider: 'local', model: '', lang: 'ru' };
const LANG_OPTIONS = [
  { value: 'ru', label: 'Русский' },
  { value: 'en', label: 'English' },
  { value: 'auto', label: 'Авто' },
];
const WHISPER_HINT: Record<string, string> = {
  tiny: 'быстрее',
  base: 'быстро',
  small: 'баланс',
  medium: 'точнее',
  large: 'высокая точность',
  'large-v3': 'максимальная точность',
  turbo: 'быстро и точно',
};
const whisperLabel = (m: string): string => (WHISPER_HINT[m] ? `${m} · ${WHISPER_HINT[m]}` : m);

const ttsVoiceLabel = (v: string, engine: 'local' | 'api'): string => {
  if (engine === 'api') return v.toLowerCase();
  const m = /^\w{2}_\w{2}-(.+)$/.exec(v);
  return m ? m[1] : v;
};

function arrayBufferToBase64(buf: ArrayBuffer): string {
  const bytes = new Uint8Array(buf);
  let binary = '';
  for (let i = 0; i < bytes.length; i++) binary += String.fromCharCode(bytes[i]);
  return btoa(binary);
}

function encodeWav16(samples: Float32Array, sampleRate: number): ArrayBuffer {
  const buffer = new ArrayBuffer(44 + samples.length * 2);
  const view = new DataView(buffer);
  const writeStr = (off: number, str: string) => { for (let i = 0; i < str.length; i++) view.setUint8(off + i, str.charCodeAt(i)); };
  writeStr(0, 'RIFF');
  view.setUint32(4, 36 + samples.length * 2, true);
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
  view.setUint32(40, samples.length * 2, true);
  for (let i = 0; i < samples.length; i++) {
    const s = Math.max(-1, Math.min(1, samples[i]));
    view.setInt16(44 + i * 2, s < 0 ? s * 0x8000 : s * 0x7FFF, true);
  }
  return buffer;
}

function recordWord(maxDurationMs = 3000): Promise<ArrayBuffer> {
  return new Promise(async (resolve, reject) => {
    try {
      const stream = await navigator.mediaDevices.getUserMedia({ audio: true });
      const ctx = new AudioContext({ sampleRate: 16000 });
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
        if (shouldStop) return;
        const ch = e.inputBuffer.getChannelData(0);
        let sumSq = 0;
        for (let i = 0; i < ch.length; i++) { sumSq += ch[i] * ch[i]; samples.push(ch[i]); }
        const rms = Math.sqrt(sumSq / ch.length);
        if (!calibrated && samples.length >= 8000) {
          const avg = rmsRing.reduce((a, b) => a + b, 0) / rmsRing.length;
          threshold = Math.max(0.02, Math.min(0.06, avg * 2));
          calibrated = true;
        }
        if (!calibrated) rmsRing.push(rms);
        if (rms < threshold) {
          if (silenceStart === null) silenceStart = Date.now();
          else if (Date.now() - silenceStart >= 400 && samples.length >= 16000) shouldStop = true;
        } else {
          silenceStart = null;
        }
      };

      src.connect(node);
      const iv = setInterval(() => { if (shouldStop && !finished) finish(); }, 100);
      const timeout = setTimeout(finish, maxDurationMs);

      function finish() {
        if (finished) return;
        finished = true;
        clearInterval(iv);
        clearTimeout(timeout);
        src.disconnect();
        node.disconnect();
        stream.getTracks().forEach((t) => t.stop());
        void ctx.close();

        const chunkSize = 1600;
        let startIdx = 0;
        for (let i = 0; i < Math.min(samples.length, 32000); i += chunkSize) {
          const chunk = samples.slice(i, i + chunkSize);
          let ssq = 0;
          for (const s of chunk) ssq += s * s;
          if (Math.sqrt(ssq / chunk.length) > threshold * 0.7) { startIdx = Math.max(0, i - chunkSize); break; }
        }
        const trimmed = samples.slice(startIdx);
        if (trimmed.length < 4000) { resolve(new ArrayBuffer(0)); return; }
        resolve(encodeWav16(new Float32Array(trimmed), 16000));
      }
    } catch (e) {
      reject(e);
    }
  });
}

// ── Helper components for Granular LLM UI ──

function TaskModelCard({
  title,
  icon,
  iconColor,
  description,
  selection,
  capabilities,
  onChange,
  disabled,
  status,
  resolved,
  onCheck,
}: {
  title: string;
  icon: string;
  iconColor: string;
  description: string;
  selection: TaskModelSelection;
  capabilities: LlmCapabilities | null;
  onChange: (sel: Partial<TaskModelSelection>) => void;
  disabled: boolean;
  status?: { ok: boolean; model: string; ms?: number } | null;
  resolved?: { provider: string; model: string } | null;
  onCheck?: () => void;
}) {
  const localModels = capabilities?.local?.models || [];
  const apiModels = capabilities?.api?.models || [];

  const localHasVision = localModels.some((m: ModelCapability) => m.vision);
  const apiHasVision = apiModels.some((m: ModelCapability) => m.vision);

  const provider = selection.provider;
  const model = selection.model;
  const autoSelect = selection.autoSelect;

  const providerOptions = [
    { value: 'local' as const, label: 'ЛОКАЛЬНО (Ollama)', available: capabilities?.local?.available ?? false, hasVision: localHasVision },
    { value: 'api' as const, label: 'ОБЛАКО (API)', available: capabilities?.api?.available ?? false, hasVision: apiHasVision },
  ];

  const getModelsForProvider = (prov: 'local' | 'api') => {
    const models = prov === 'local' ? localModels : apiModels;
    return models.map((m: ModelCapability) => ({
      value: m.id,
      label: m.vision ? `${m.id} · ✓ Vision` : m.id,
    }));
  };

  const currentModels = getModelsForProvider(provider);

  return (
    <div style={{ ...panel, opacity: disabled ? 0.6 : 1 }}>
      <div style={{ ...panelHead, cursor: disabled ? 'not-allowed' : 'pointer' }}>
        <Icon name={icon as 'chat' | 'network' | 'system' | 'terminal' | 'files' | 'tasks' | 'settings' | 'folder' | 'file' | 'clock' | 'shield' | 'refresh' | 'tool' | 'send' | 'close' | 'audio' | 'wifi' | 'camera' | 'like' | 'dislike' | 'copy' | 'edit' | 'share' | 'play' | 'check' | 'stop'} color={iconColor} size={18} width={1.7} />
        <span>{title}</span>
      </div>
      <div style={{ fontSize: 12, color: C.textDim, marginBottom: 14 }}>{description}</div>

      {/* Provider selector */}
      <div style={{ display: 'flex', gap: 8, marginBottom: 14, flexWrap: 'wrap' }}>
        {providerOptions.map(p => (
          <button
            key={p.value}
            onClick={() => !disabled && onChange({ provider: p.value, autoSelect: true, model: '' })}
            disabled={disabled || !p.available}
            style={{
              flex: 1, minWidth: 140, padding: '10px 12px', borderRadius: 10, cursor: p.available ? 'pointer' : 'not-allowed',
              fontFamily: C.mono, fontSize: 12, letterSpacing: '.04em',
              border: `1px solid ${provider === p.value ? C.cyan : (p.available ? C.line : C.textDim)}`,
              background: provider === p.value ? 'rgba(80,200,255,.12)' : (p.available ? 'transparent' : 'rgba(255,255,255,.02)'),
              color: provider === p.value ? C.cyan : (p.available ? C.textMid : C.textDim),
              opacity: p.available ? 1 : 0.4,
            }}
          >
            {p.label} {p.hasVision && <span style={{ marginLeft: 6, fontSize: 10, color: C.green }}>✓ Vision</span>}
          </button>
        ))}
      </div>

      {/* Auto-select toggle */}
      <div style={{ display: 'flex', alignItems: 'center', gap: 10, marginBottom: 12 }}>
        <label style={{ display: 'flex', alignItems: 'center', gap: 8, cursor: disabled ? 'not-allowed' : 'pointer', fontSize: 13, color: C.tableText }}>
          <input
            type="checkbox"
            checked={autoSelect}
            onChange={(e) => !disabled && onChange({ autoSelect: e.target.checked })}
            disabled={disabled}
            style={{ width: 16, height: 16, accentColor: C.cyan }}
          />
          <span>Авто-выбор лучшей модели для этой задачи</span>
        </label>
      </div>

      <div style={{ display: 'flex', alignItems: 'center', gap: 8, marginBottom: 12 }}>
        <span style={{ width: 8, height: 8, borderRadius: '50%', flexShrink: 0, background: status ? (status.ok ? C.green : C.red) : C.textDim, boxShadow: `0 0 8px ${status ? (status.ok ? C.green : C.red) : C.textDim}` }} />
        <span style={{ fontFamily: C.mono, fontSize: 11, letterSpacing: '.06em', color: status ? (status.ok ? C.green : C.red) : C.textDim }}>
          {status ? (status.ok ? 'ДОСТУПНА' : 'НЕДОСТУПНА') : '—'}
        </span>
        {status?.ms != null && <span style={{ fontFamily: C.mono, fontSize: 11, color: C.textDim }}>{status.ms}ms</span>}
      </div>
      {autoSelect && resolved && resolved.model && (
        <div style={{ fontSize: 11, color: C.textDim, marginBottom: 12 }}>
          резолвится: <span style={{ fontFamily: C.mono }}>{resolved.model}</span> · {resolved.provider === 'local' ? 'Локально' : 'Облако'}
        </div>
      )}

      {/* Model selection */}
      <div style={{ marginBottom: 12 }}>
        <div style={{ fontSize: 13, color: C.tableText, marginBottom: 6 }}>
          Модель ({provider === 'local' ? 'Ollama' : 'API'})
          {autoSelect && (
            <span style={{ color: C.textDim }}> · приоритет при авто-выборе</span>
          )}
          {!autoSelect && currentModels.length > 0 && (
            <span style={{ color: C.textDim }}> · {currentModels.length} {currentModels.length === 1 ? 'модель' : currentModels.length < 5 ? 'модели' : 'моделей'}</span>
          )}
        </div>
        <select
          value={model}
          onChange={(e) => !disabled && onChange({ model: e.target.value })}
          disabled={disabled || currentModels.length === 0}
          style={{ ...input, width: '100%', cursor: disabled ? 'not-allowed' : 'pointer', opacity: disabled || currentModels.length === 0 ? 0.5 : autoSelect ? 0.8 : 1 }}
        >
          {model && !currentModels.some((m) => m.value === model) && (
            <option value={model}>{model}</option>
          )}
          {currentModels.length === 0 ? (
            <option value="" disabled>Нет доступных моделей — проверьте подключение</option>
          ) : (
            currentModels.map(m => <option key={m.value} value={m.value}>{m.label}</option>)
          )}
        </select>
        {currentModels.length === 0 && (
          <div style={{ fontSize: 11, color: C.amber, marginTop: 4 }}>
            {provider === 'local' ? 'Запустите Ollama и добавьте модель (ollama pull llava:13b для Vision)' : 'Введите API-ключ в настройках профиля'}
          </div>
        )}
      </div>

      {/* Capability badges */}
      <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap', marginTop: 8 }}>
        <span style={{ ...badge, background: capabilities?.local?.chat ? 'rgba(76,217,123,.14)' : 'rgba(255,255,255,.04)', color: capabilities?.local?.chat ? C.green : C.textDim, borderColor: capabilities?.local?.chat ? 'rgba(76,217,123,.5)' : C.line }}>
          <Icon name="chat" size={10} /> Чат
        </span>
        <span style={{ ...badge, background: capabilities?.local?.tools ? 'rgba(76,217,123,.14)' : 'rgba(255,255,255,.04)', color: capabilities?.local?.tools ? C.green : C.textDim, borderColor: capabilities?.local?.tools ? 'rgba(76,217,123,.5)' : C.line }}>
          <Icon name="tool" size={10} width={1.7} /> Tools
        </span>
        <span style={{ ...badge, background: (capabilities?.local?.vision ?? false) ? 'rgba(76,217,123,.14)' : 'rgba(255,255,255,.04)', color: (capabilities?.local?.vision ?? false) ? C.green : C.textDim, borderColor: (capabilities?.local?.vision ?? false) ? 'rgba(76,217,123,.5)' : C.line }}>
          <Icon name="camera" size={10} /> Vision
        </span>
        <span style={{ ...badge, background: capabilities?.api?.chat ? 'rgba(80,200,255,.14)' : 'rgba(255,255,255,.04)', color: capabilities?.api?.chat ? C.cyan : C.textDim, borderColor: capabilities?.api?.chat ? 'rgba(80,200,255,.5)' : C.line }}>
          <Icon name="chat" size={10} /> Чат (API)
        </span>
        <span style={{ ...badge, background: capabilities?.api?.tools ? 'rgba(80,200,255,.14)' : 'rgba(255,255,255,.04)', color: capabilities?.api?.tools ? C.cyan : C.textDim, borderColor: capabilities?.api?.tools ? 'rgba(80,200,255,.5)' : C.line }}>
          <Icon name="tool" size={10} width={1.7} /> Tools (API)
        </span>
        <span style={{ ...badge, background: (capabilities?.api?.vision ?? false) ? 'rgba(80,200,255,.14)' : 'rgba(255,255,255,.04)', color: (capabilities?.api?.vision ?? false) ? C.cyan : C.textDim, borderColor: (capabilities?.api?.vision ?? false) ? 'rgba(80,200,255,.5)' : C.line }}>
          <Icon name="camera" size={10} /> Vision (API)
        </span>
      </div>
      {onCheck && (
        <div style={{ display: 'flex', justifyContent: 'flex-end', marginTop: 10 }}>
          <button style={{ ...btn, padding: '6px 14px' }} onClick={onCheck} disabled={disabled}>Проверить</button>
        </div>
      )}
    </div>
  );
}

function SpeechCard({
  selection,
  asrModels,
  speechCheck,
  onChange,
  onCheck,
}: {
  selection: SpeechSelection;
  asrModels: AsrModels | null;
  speechCheck: AsrCheck | null;
  onChange: (sel: Partial<SpeechSelection>) => void;
  onCheck: () => void;
}) {
  const provider = selection.provider;
  const model = selection.model;
  const lang = selection.lang;
  const modelOptions = asrModels ? (provider === 'local' ? asrModels.local : asrModels.api) : [];
  const providerOptions = [
    { value: 'local' as const, label: 'ЛОКАЛЬНО (Whisper)' },
    { value: 'api' as const, label: 'ОБЛАКО (API)' },
  ];
  const status = speechCheck && speechCheck.provider === provider && (model === '' || speechCheck.model === model)
    ? speechCheck
    : null;

  return (
    <div style={{ ...panel }}>
      <div style={{ ...panelHead }}>
        <Icon name="audio" color={C.green} size={18} width={1.7} />
        <span>РАСПОЗНАВАНИЕ РЕЧИ (WHISPER)</span>
      </div>
      <div style={{ fontSize: 12, color: C.textDim, marginBottom: 14 }}>
        Распознавание речи: команды, диктовка, транскрипция аудио через Whisper
      </div>
      <div style={{ fontSize: 12, color: C.textDim, marginBottom: 12 }}>
        <div>ЛОКАЛЬНО — whisper.cpp на вашем ПК, работает офлайн.</div>
        <div>ОБЛАКО — API-транскрипция (proxyapi), точнее; ключ задаётся в «Провайдер».</div>
      </div>

      <div style={{ display: 'flex', gap: 8, marginBottom: 14, flexWrap: 'wrap' }}>
        {providerOptions.map(p => (
          <button
            key={p.value}
            onClick={() => provider !== p.value && onChange({ provider: p.value, model: '' })}
            style={{
              flex: 1, minWidth: 140, padding: '10px 12px', borderRadius: 10, cursor: 'pointer',
              fontFamily: C.mono, fontSize: 12, letterSpacing: '.04em',
              border: `1px solid ${provider === p.value ? C.cyan : C.line}`,
              background: provider === p.value ? 'rgba(80,200,255,.12)' : 'transparent',
              color: provider === p.value ? C.cyan : C.textMid,
            }}
          >
            {p.label}
          </button>
        ))}
      </div>

      <div style={{ marginBottom: 12 }}>
        <div style={{ fontSize: 13, color: C.tableText, marginBottom: 6 }}>
          Модель ({provider === 'local' ? 'локально' : 'API'})
        </div>
        <select
          value={model}
          onChange={(e) => onChange({ model: e.target.value })}
          disabled={!asrModels}
          style={{ ...input, width: '100%', cursor: asrModels ? 'pointer' : 'not-allowed', opacity: asrModels ? 1 : 0.5 }}
        >
          {!asrModels ? (
            model ? <option value={model}>{whisperLabel(model)}</option> : <option value="" disabled>—</option>
          ) : model && !modelOptions.includes(model) ? (
            <option value={model}>{whisperLabel(model)}</option>
          ) : modelOptions.length === 0 ? (
            <option value="">по умолчанию</option>
          ) : (
            <>
              {model === '' && <option value="">по умолчанию</option>}
              {modelOptions.map(m => <option key={m} value={m}>{whisperLabel(m)}</option>)}
            </>
          )}
        </select>
      </div>

      <div style={{ marginBottom: 12 }}>
        <div style={{ fontSize: 13, color: C.tableText, marginBottom: 6 }}>Язык распознавания</div>
        <select
          value={lang}
          onChange={(e) => onChange({ lang: e.target.value })}
          style={{ ...input, width: '100%', cursor: 'pointer' }}
        >
          {LANG_OPTIONS.map(o => <option key={o.value} value={o.value}>{o.label}</option>)}
        </select>
      </div>

      <div style={{ display: 'flex', alignItems: 'center', gap: 8, flexWrap: 'wrap', marginTop: 8 }}>
        <button style={{ ...btn, padding: '7px 14px' }} onClick={onCheck}>Проверить</button>
        {status && (
          <>
            <span style={{ width: 8, height: 8, borderRadius: '50%', flexShrink: 0, background: status.ok ? C.green : C.red, boxShadow: `0 0 8px ${status.ok ? C.green : C.red}` }} />
            <span style={{ fontFamily: C.mono, fontSize: 11, letterSpacing: '.06em', color: status.ok ? C.green : C.red }}>
              {status.ok ? 'ДОСТУПНА' : 'НЕДОСТУПНА'}
            </span>
            {status.engine && <span style={{ fontFamily: C.mono, fontSize: 11, color: C.textDim }}>{status.engine}</span>}
            {status.ms > 0 ? <span style={{ fontFamily: C.mono, fontSize: 11, color: C.textDim }}>· {status.ms}ms</span> : ''}
          </>
        )}
      </div>
      {status && (status.ok || status.message) && (
        <div style={{ fontSize: 11, color: status.ok ? C.textDim : C.red, marginTop: 6 }}>
          {status.message || (status.ok ? 'Доступен' : '')}
        </div>
      )}
    </div>
  );
}

const badge: React.CSSProperties = {
  display: 'inline-flex', alignItems: 'center', gap: 4, padding: '4px 10px', borderRadius: R.pill,
  fontSize: 11, fontFamily: C.mono, border: `1px solid ${C.line}`, background: 'rgba(255,255,255,.04)',
  color: C.textDim,
};

export default function Settings({ a }: { a: AssistantState }) {
  const [trainState, setTrainState] = useState<'idle' | 'recording' | 'sending' | 'ok' | 'fail'>('idle');
  const [trainCount, setTrainCount] = useState(0);
  const [trainMsg, setTrainMsg] = useState('');

  // Local state for profile editing
  const [profile, setProfile] = useState<UserLlmProfile | null>(null);
  const [profileInited, setProfileInited] = useState(false);
  const [dirty, setDirty] = useState(false);
  const [saveMsg, setSaveMsg] = useState<string | null>(null);
  const syncAfterSaveRef = useRef(false);
  const alreadyPinged = useRef<Set<string>>(new Set());

  useEffect(() => {
    a.requestLlmProfile();
    a.requestLlmCapabilities();
    a.refreshLlm();
    a.requestAsrModels();
    a.requestTts();
    a.requestTtsModels();
    const t = setTimeout(() => { for (const task of TASKS) a.sendLlmResolve(task); }, 100);
    return () => clearTimeout(t);
  }, []);

  useEffect(() => {
    if (!a.llmProfile) return;
    if (!profileInited) {
      setProfile({ ...a.llmProfile });
      setProfileInited(true);
      setDirty(false);
      return;
    }
    if (syncAfterSaveRef.current) {
      syncAfterSaveRef.current = false;
      setProfile({ ...a.llmProfile });
      setDirty(false);
      setSaveMsg('Сохранено');
    }
  }, [a.llmProfile]);

  useEffect(() => {
    for (const task of TASKS) {
      const r = a.llmResolve[task];
      if (!r || !r.model) continue;
      const key = `${r.provider}:${r.model}`;
      if (alreadyPinged.current.has(key)) continue;
      alreadyPinged.current.add(key);
      a.sendLlmPing(r.provider, r.model);
    }
  }, [a.llmResolve]);

  const llmSigRef = useRef('');
  const [llmDraft, setLlmDraft] = useState<Omit<LlmConfig, 'status'>>({ provider: 'local', local: { url: '', model: '' }, api: { baseUrl: '', model: '' } });
  const [apiKey, setApiKey] = useState('');

  useEffect(() => {
    const sig = `${a.llm.provider}|${a.llm.local.url}|${a.llm.local.model}|${a.llm.api.baseUrl}|${a.llm.api.model}`;
    if (sig === llmSigRef.current) return;
    llmSigRef.current = sig;
    setLlmDraft({
      provider: a.llm.provider,
      local: { url: a.llm.local.url, model: a.llm.local.model },
      api: { baseUrl: a.llm.api.baseUrl, model: a.llm.api.model },
    });
  }, [a.llm]);

  const ttsSigRef = useRef('');
  const [ttsEngine, setTtsEngine] = useState<'local' | 'api'>('local');
  const [ttsVoice, setTtsVoice] = useState('');

  useEffect(() => {
    const sig = `${a.ttsConf?.engine ?? ''}|${a.ttsConf?.voice ?? ''}`;
    if (sig === ttsSigRef.current) return;
    ttsSigRef.current = sig;
    setTtsEngine(a.ttsConf?.engine ?? 'local');
    setTtsVoice(a.ttsConf?.voice ?? '');
  }, [a.ttsConf]);

  const st = a.llm.status;
  const statusColor = !st ? C.textDim : st.ok ? C.green : C.red;
  const statusLabel = !st ? '—' : st.ok ? 'ОНЛАЙН' : 'ОФФЛАЙН';

  const updateSelection = (task: keyof UserLlmProfile, changes: Partial<TaskModelSelection>) => {
    if (!profile) return;
    setDirty(true);
    setSaveMsg(null);
    setProfile(prev => {
      if (!prev) return null;
      const current = (prev[task] ?? { provider: 'local', model: '', autoSelect: true }) as TaskModelSelection;
      return { ...prev, [task]: { ...current, ...changes } };
    });
  };

  const updateSpeech = (changes: Partial<SpeechSelection>) => {
    if (!profile) return;
    setDirty(true);
    setSaveMsg(null);
    setProfile(prev => {
      if (!prev) return null;
      return { ...prev, speech: { ...(prev.speech ?? DEFAULT_SPEECH), ...changes } };
    });
  };

  const checkTask = (task: 'chat' | 'voice' | 'vision' | 'toolCalling') => {
    if (!profile) return;
    const sel = profile[task] ?? { provider: 'local', model: '', autoSelect: true };
    if (sel.autoSelect || !sel.model) {
      a.sendLlmResolve(task);
    } else {
      a.sendLlmPing(sel.provider, sel.model);
    }
  };

  const checkSpeech = () => {
    a.sendAsrCheck(profile?.speech?.provider ?? DEFAULT_SPEECH.provider, profile?.speech?.model ?? '');
  };

  const saveLlmSettings = () => {
    const key = apiKey.trim();
    a.saveLlm({
      provider: llmDraft.provider,
      local: { url: llmDraft.local.url, model: llmDraft.local.model },
      api: { baseUrl: llmDraft.api.baseUrl, model: llmDraft.api.model, ...(key ? { key } : {}) },
    });
    setTimeout(() => a.refreshLlm(), 300);
  };

  const saveProfile = () => {
    if (!profile || !dirty) return;
    syncAfterSaveRef.current = true;
    a.saveLlmProfile(profile);
    setSaveMsg('Сохранение…');
  };

  const checkAll = () => {
    a.refreshLlm();
    alreadyPinged.current.clear();
    a.requestLlmCapabilities(true);
    a.requestLlmProfile();
    a.requestAsrModels();
    a.requestTts();
    a.requestTtsModels();
    setTimeout(() => { for (const task of TASKS) a.sendLlmResolve(task); }, 100);
  };

  const resetProfile = () => {
    syncAfterSaveRef.current = false;
    setProfileInited(false);
    setProfile(null);
    setDirty(false);
    setSaveMsg(null);
    a.requestLlmProfile();
  };

  const trainWake = async () => {
    try {
      for (let i = 0; i < 3; i++) {
        setTrainCount(i);
        setTrainState('recording');
        const wav = await recordWord();
        if (wav.byteLength === 0) {
          setTrainMsg('Речь не распознана — произнесите слово и сделайте паузу');
          setTrainState('fail');
          return;
        }
        setTrainState('sending');
        const b64 = arrayBufferToBase64(wav);
        a.wakeTrain(b64);
        setTrainMsg('');
        setTrainState('ok');
      }
    } catch {
      setTrainMsg('Ошибка записи — разрешите доступ к микрофону');
      setTrainState('fail');
    }
  };

  const resetWake = () => {
    a.wakeReset();
    setTrainState('idle');
  };

  const chatSel = profile?.chat ?? { provider: 'local', model: '', autoSelect: true };
  const voiceSel = profile?.voice ?? { provider: 'local', model: '', autoSelect: true };
  const visionSel = profile?.vision ?? { provider: 'local', model: '', autoSelect: true };
  const toolSel = profile?.toolCalling ?? { provider: 'local', model: '', autoSelect: true };
  const speechSel = profile?.speech ?? DEFAULT_SPEECH;

  const cardStatus = (sel: TaskModelSelection, task: string): { ok: boolean; model: string; ms?: number } | null => {
    const resolved = a.llmResolve[task];
    const key = resolved && resolved.model ? `${resolved.provider}:${resolved.model}` : (sel.model ? `${sel.provider}:${sel.model}` : null);
    const ping = key ? a.llmPing[key] : undefined;
    return ping ? { ok: ping.ok, model: ping.model, ms: ping.ms } : null;
  };

  const voiceOptions = a.ttsModels ? (ttsEngine === 'local' ? a.ttsModels.local : a.ttsModels.api) : [];
  const ttsStatus = a.ttsCheck && a.ttsCheck.engine === ttsEngine ? a.ttsCheck : null;

  return (
    <div style={{ position: 'absolute', inset: 0, overflowY: 'auto', padding: '26px 30px', animation: 'ariaFadeFast .22s ease both' }}>
      <div style={{ maxWidth: 760, display: 'flex', flexDirection: 'column', gap: 18 }}>
        <div style={panel}>
          <div style={panelHead}><MicIcon color={C.accent} size={18} />Голос и речь</div>
          <div style={{ ...row, borderBottom: '1px solid rgba(255,255,255,.05)', alignItems: 'flex-start' }}>
            <div>
              <div style={{ fontSize: 14, color: C.tableText }}>Движок озвучки</div>
              <div style={{ fontSize: 12, color: C.textDim }}>Piper — офлайн · API — облачные голоса</div>
            </div>
            <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }}>
              {([
                { value: 'local' as const, label: 'ЛОКАЛЬНО (Piper)' },
                { value: 'api' as const, label: 'ОБЛАКО (API)' },
              ]).map(p => (
                <button
                  key={p.value}
                  onClick={() => { if (p.value !== ttsEngine) { setTtsEngine(p.value); a.sendTtsSet(p.value, ttsVoice); } }}
                  style={{
                    padding: '8px 12px', borderRadius: 10, cursor: 'pointer',
                    fontFamily: C.mono, fontSize: 11, letterSpacing: '.04em',
                    border: `1px solid ${ttsEngine === p.value ? C.cyan : C.line}`,
                    background: ttsEngine === p.value ? 'rgba(80,200,255,.12)' : 'transparent',
                    color: ttsEngine === p.value ? C.cyan : C.textMid,
                  }}
                >
                  {p.label}
                </button>
              ))}
            </div>
          </div>
          <div style={{ ...row, borderBottom: '1px solid rgba(255,255,255,.05)', alignItems: 'flex-start', gap: 12 }}>
            <div>
              <div style={{ fontSize: 14, color: C.tableText }}>Голос</div>
              <div style={{ fontSize: 12, color: C.textDim }}>{ttsEngine === 'local' ? 'локальные голоса' : 'облачные голоса API'}</div>
            </div>
            <select
              value={ttsVoice}
              onChange={(e) => { const v = e.target.value; setTtsVoice(v); a.sendTtsSet(ttsEngine, v); }}
              disabled={!a.ttsModels}
              style={{ ...input, width: 240, cursor: a.ttsModels ? 'pointer' : 'not-allowed', opacity: a.ttsModels ? 1 : 0.5 }}
            >
              {!a.ttsModels ? (
                <option value="" disabled>…</option>
              ) : ttsVoice && !voiceOptions.includes(ttsVoice) ? (
                <option value={ttsVoice}>{ttsVoiceLabel(ttsVoice, ttsEngine)}</option>
              ) : voiceOptions.length === 0 ? (
                <option value="" disabled>нет голосов</option>
              ) : (
                <>
                  {ttsVoice === '' && <option value="" disabled>—</option>}
                  {voiceOptions.map(v => <option key={v} value={v}>{ttsVoiceLabel(v, ttsEngine)}</option>)}
                </>
              )}
            </select>
          </div>
          <div style={{ padding: '12px 0', borderBottom: '1px solid rgba(255,255,255,.05)' }}>
            <div style={{ display: 'flex', alignItems: 'center', gap: 8, flexWrap: 'wrap' }}>
              <button style={{ ...btn, padding: '7px 14px' }} onClick={() => a.sendTtsCheck(ttsEngine, ttsVoice)}>Проверить</button>
              {ttsStatus && (
                <>
                  <span style={{ width: 8, height: 8, borderRadius: '50%', flexShrink: 0, background: ttsStatus.ok ? C.green : C.red, boxShadow: `0 0 8px ${ttsStatus.ok ? C.green : C.red}` }} />
                  <span style={{ fontFamily: C.mono, fontSize: 11, letterSpacing: '.06em', color: ttsStatus.ok ? C.green : C.red }}>
                    {ttsStatus.ok ? 'ДОСТУПНА' : 'НЕДОСТУПНА'}
                  </span>
                  <span style={{ fontFamily: C.mono, fontSize: 11, color: C.textDim }}>{ttsStatus.engine}</span>
                  {ttsStatus.ms > 0 && <span style={{ fontFamily: C.mono, fontSize: 11, color: C.textDim }}>· {ttsStatus.ms}ms</span>}
                </>
              )}
            </div>
            {ttsStatus?.message && (
              <div style={{ fontSize: 11, color: ttsStatus.ok ? C.textDim : C.red, marginTop: 6 }}>{ttsStatus.message}</div>
            )}
          </div>
          <div style={{ padding: '14px 0', borderBottom: '1px solid rgba(255,255,255,.05)' }}>
            <div style={{ display: 'flex', justifyContent: 'space-between', marginBottom: 10 }}>
              <span style={{ fontSize: 14, color: C.tableText }}>Скорость речи</span>
              <span style={{ fontFamily: C.mono, fontSize: 13, color: C.textMid }}>{a.rate.toFixed(1)}×</span>
            </div>
            <input type="range" min="0.6" max="1.6" step="0.1" value={a.rate} onChange={(e) => a.setRate(parseFloat(e.target.value))} style={{ width: '100%', accentColor: C.accent }} />
          </div>
          <div style={{ padding: '12px 0', borderBottom: '1px solid rgba(255,255,255,.05)' }}>
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
              <div>
                <div style={{ fontSize: 14, color: C.tableText }}>Слово активации</div>
                <div style={{ fontSize: 12, color: C.textDim }}>«Ария» · постоянное прослушивание</div>
              </div>
              <Switch on={a.wake} onClick={a.setWake} />
            </div>

            <div style={{ display: 'flex', justifyContent: 'space-between', marginTop: 14 }}>
              <span style={{ fontSize: 13, color: C.tableText }}>Порог срабатывания</span>
              <span style={{ fontFamily: C.mono, fontSize: 13, color: C.textMid }}>{a.wakeConfig.threshold.toFixed(2)}</span>
            </div>
            <input type="range" min="0.05" max="0.9" step="0.01" value={a.wakeConfig.threshold} onChange={(e) => a.setWakeThreshold(parseFloat(e.target.value))} style={{ width: '100%', accentColor: C.accent, marginTop: 8 }} />

            <div style={{ display: 'flex', alignItems: 'center', gap: 12, marginTop: 14 }}>
              <button
                onClick={trainWake}
                disabled={trainState === 'recording' || trainState === 'sending'}
                style={{
                  ...btn,
                  opacity: trainState === 'recording' || trainState === 'sending' ? 0.6 : 1,
                  background: trainState === 'ok' ? 'rgba(76,217,123,.14)' : undefined,
                  borderColor: trainState === 'ok' ? 'rgba(76,217,123,.5)' : undefined,
                  color: trainState === 'ok' ? '#4cd97b' : C.cyan,
                }}
              >
                {trainState === 'recording' ? `Говорите ${trainCount + 1}/3…` : trainState === 'sending' ? 'Обработка…' : 'Обучить детектор'}
              </button>
              <button
                onClick={resetWake}
                disabled={!a.wakeConfig.trained}
                style={{ ...btn, background: 'rgba(255,107,91,.1)', borderColor: 'rgba(255,107,91,.4)', color: C.accent, opacity: a.wakeConfig.trained ? 1 : 0.5 }}
              >
                Сбросить
              </button>
            </div>
            <div style={{ fontSize: 12, color: trainState === 'fail' ? C.red : C.textDim, marginTop: 8 }}>
              {trainState === 'ok'
                ? 'Шаблоны сохранены'
                : trainState === 'fail'
                  ? (trainMsg || 'Ошибка — повторите')
                  : a.wakeConfig.trained
                    ? `обучен · ${a.wakeConfig.templates.length}/3 записи`
                    : 'не обучен · произнесите слово 3 раза'}
            </div>
          </div>
</div>

        <div style={panel}>
          <div style={panelHead}><Icon name="settings" color={C.purple} size={18} width={1.7} />ПРОВАЙДЕР (ГЛОБАЛЬНО)</div>
          <div style={{ fontSize: 12, color: C.textDim, marginBottom: 14 }}>
            Глобальный LLM-провайдер для всех задач.
            {!a.isAdmin && <span style={{ color: C.amber }}> Изменение URL/ключа доступно только администратору.</span>}
          </div>

          <div style={{ display: 'flex', gap: 8, marginBottom: 16, flexWrap: 'wrap' }}>
            {[
              { value: 'local' as const, label: 'ЛОКАЛЬНО (Ollama)' },
              { value: 'api' as const, label: 'ОБЛАКО (API)' },
            ].map(p => (
              <button
                key={p.value}
                onClick={() => a.isAdmin && p.value !== llmDraft.provider && setLlmDraft(d => ({ ...d, provider: p.value }))}
                disabled={!a.isAdmin}
                style={{
                  flex: 1, minWidth: 140, padding: '10px 12px', borderRadius: 10, cursor: a.isAdmin ? 'pointer' : 'not-allowed',
                  fontFamily: C.mono, fontSize: 12, letterSpacing: '.04em',
                  border: `1px solid ${llmDraft.provider === p.value ? C.cyan : C.line}`,
                  background: llmDraft.provider === p.value ? 'rgba(80,200,255,.12)' : 'transparent',
                  color: llmDraft.provider === p.value ? C.cyan : C.textMid, opacity: a.isAdmin ? 1 : 0.5,
                }}
              >
                {p.label}
              </button>
            ))}
          </div>

          <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 14 }}>
            <Field label="Ссылка Ollama" hint="http://127.0.0.1:11434" value={llmDraft.local.url} onChange={(v) => setLlmDraft(d => ({ ...d, local: { ...d.local, url: v } }))} disabled={!a.isAdmin} />
            <Field label="Модель Ollama (по умолчанию)" hint="qwen2.5:7b" value={llmDraft.local.model} onChange={(v) => setLlmDraft(d => ({ ...d, local: { ...d.local, model: v } }))} disabled={!a.isAdmin} />
            <Field label="API-ссылка (base URL)" hint="https://api.openai.com/v1" value={llmDraft.api.baseUrl} onChange={(v) => setLlmDraft(d => ({ ...d, api: { ...d.api, baseUrl: v } }))} disabled={!a.isAdmin} />
            <Field label="API-модель (по умолчанию)" hint="gpt-4o" value={llmDraft.api.model} onChange={(v) => setLlmDraft(d => ({ ...d, api: { ...d.api, model: v } }))} disabled={!a.isAdmin} />
            <Field label="API-ключ" hint="ключ не отображается" value={apiKey} onChange={setApiKey} password disabled={!a.isAdmin} />
          </div>

          <div style={{ ...row, borderTop: '1px solid rgba(255,255,255,.06)', marginTop: 16 }}>
            <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
              <span style={{ width: 8, height: 8, borderRadius: '50%', background: statusColor, boxShadow: `0 0 8px ${statusColor}` }} />
              <span style={{ fontSize: 13, color: C.tableText }}>{st && st.ok ? `отклик ${st.ms}ms · ${st.model}` : (st ? 'недоступно' : 'статус неизвестен')}</span>
            </div>
            <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
              <span style={{ fontFamily: C.mono, fontSize: 11, color: statusColor, letterSpacing: '.06em' }}>{statusLabel}</span>
              <button style={btn} onClick={() => a.refreshLlm()}>Проверить</button>
              <button style={{ ...btn, background: 'rgba(76,217,123,.14)', borderColor: 'rgba(76,217,123,.5)', color: '#4cd97b', opacity: a.isAdmin ? 1 : 0.5, cursor: a.isAdmin ? 'pointer' : 'not-allowed' }} onClick={saveLlmSettings} disabled={!a.isAdmin}>Сохранить</button>
            </div>
          </div>
        </div>

        <div style={panel}>
          <div style={panelHead}><Icon name="tool" color={C.cyan} size={18} width={1.7} />ИИ-модели (по задачам)</div>
          <div style={{ fontSize: 12, color: C.textDim, marginBottom: 14 }}>
            Каждая задача использует свою модель. Настройте провайдер и модель для чата, голоса, зрения (камеры), инструментов и распознавания речи.
            <span style={{ color: C.amber }}> Настройки сохраняются в профиле пользователя.</span>
          </div>

          <div style={{ display: 'flex', flexDirection: 'column', gap: 14 }}>
            <TaskModelCard
              title="ЧАТ"
              icon="chat"
              iconColor={C.cyan}
              description="Общение, вопросы, объяснения, помощь в настройке"
              selection={chatSel}
              capabilities={a.llmCapabilities}
              onChange={c => updateSelection('chat', c)}
              disabled={false}
              status={cardStatus(chatSel, 'chat')}
              resolved={a.llmResolve['chat'] ?? null}
              onCheck={() => checkTask('chat')}
            />
            <TaskModelCard
              title="ГОЛОС"
              icon="audio"
              iconColor={C.accent}
              description="Голосовые команды, диалог через микрофон (нужна низкая задержка)"
              selection={voiceSel}
              capabilities={a.llmCapabilities}
              onChange={c => updateSelection('voice', c)}
              disabled={false}
              status={cardStatus(voiceSel, 'voice')}
              resolved={a.llmResolve['voice'] ?? null}
              onCheck={() => checkTask('voice')}
            />
            <TaskModelCard
              title="VISION (КАМЕРЫ)"
              icon="camera"
              iconColor={C.purple}
              description="Анализ кадров с камер: что происходит, объекты, люди, аномалии"
              selection={visionSel}
              capabilities={a.llmCapabilities}
              onChange={c => updateSelection('vision', c)}
              disabled={false}
              status={cardStatus(visionSel, 'vision')}
              resolved={a.llmResolve['vision'] ?? null}
              onCheck={() => checkTask('vision')}
            />
            <TaskModelCard
              title="TOOLS (КОМАНДЫ)"
              icon="tool"
              iconColor={C.green}
              description="Выполнение действий: управление светом, сканирование сети, открытие камер и др."
              selection={toolSel}
              capabilities={a.llmCapabilities}
              onChange={c => updateSelection('toolCalling', c)}
              disabled={false}
              status={cardStatus(toolSel, 'toolCalling')}
              resolved={a.llmResolve['toolCalling'] ?? null}
              onCheck={() => checkTask('toolCalling')}
            />
            <SpeechCard
              selection={speechSel}
              asrModels={a.asrModels}
              speechCheck={a.speechCheck}
              onChange={c => updateSpeech(c)}
              onCheck={checkSpeech}
            />
          </div>

          <div style={{ ...row, borderTop: '1px solid rgba(255,255,255,.06)', marginTop: 16 }}>
            <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
              <span style={{ width: 8, height: 8, borderRadius: '50%', background: statusColor, boxShadow: `0 0 8px ${statusColor}` }} />
              <span style={{ fontSize: 13, color: C.tableText }}>{st && st.ok ? `отклик ${st.ms}ms · ${st.model}` : (st ? 'недоступно' : 'статус неизвестен')}</span>
            </div>
            <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
              <span style={{ fontFamily: C.mono, fontSize: 11, color: statusColor, letterSpacing: '.06em' }}>{statusLabel}</span>
              <button style={btn} onClick={checkAll}>Проверить всё</button>
              <span style={{ fontSize: 11, color: dirty ? C.amber : (saveMsg ? C.green : C.textDim) }}>
                {dirty ? 'изменения не сохранены' : (saveMsg ?? '')}
              </span>
              <button
                style={{ ...btn, background: 'rgba(76,217,123,.14)', borderColor: 'rgba(76,217,123,.5)', color: '#4cd97b', opacity: dirty ? 1 : 0.55, cursor: dirty ? 'pointer' : 'not-allowed' }}
                onClick={saveProfile}
                disabled={!dirty}
              >
                Сохранить профиль
              </button>
              <button style={{ ...btn, background: 'rgba(255,107,91,.14)', borderColor: 'rgba(255,107,91,.5)', color: C.accent }} onClick={resetProfile}>Сбросить</button>
            </div>
          </div>
        </div>

        <PasswordPanel a={a} />
        {a.isAdmin && <UsersPanel a={a} />}
      </div>
    </div>
  );
}

function PasswordPanel({ a }: { a: AssistantState }) {
  const [cur, setCur] = useState('');
  const [next, setNext] = useState('');
  const [msg, setMsg] = useState<{ ok: boolean; text: string } | null>(null);

  useEffect(() => {
    if (a.userResult) {
      if (a.userResult.ok) {
        setCur(''); setNext(''); setMsg({ ok: true, text: 'Пароль изменён.' });
      } else {
        setMsg({ ok: false, text: a.userResult.error || 'Ошибка' });
      }
    }
  }, [a.userResult]);

  const submit = () => {
    if (!cur || next.length < 4) return;
    setMsg(null);
    a.changePassword(cur, next);
  };

  return (
    <div style={panel}>
      <div style={panelHead}><Icon name="settings" color={C.purple} size={18} width={1.7} />Пароль</div>
      <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr auto', gap: 10, alignItems: 'end' }}>
        <div>
          <div style={{ fontSize: 13, color: C.tableText, marginBottom: 4 }}>Текущий</div>
          <input type="password" value={cur} onChange={(e) => setCur(e.target.value)} placeholder="••••••••" style={input} />
        </div>
        <div>
          <div style={{ fontSize: 13, color: C.tableText, marginBottom: 4 }}>Новый</div>
          <input type="password" value={next} onChange={(e) => setNext(e.target.value)} placeholder="минимум 4 символа" style={input} />
        </div>
        <button style={{ ...btn, background: 'rgba(180,107,255,.12)', borderColor: 'rgba(180,107,255,.5)', color: C.purple, padding: '10px 16px' }} onClick={submit}>
          Сменить
        </button>
      </div>
      {msg && <div style={{ fontSize: 12, color: msg.ok ? C.green : C.red, marginTop: 10 }}>{msg.text}</div>}
    </div>
  );
}

function UsersPanel({ a }: { a: AssistantState }) {
  const [newName, setNewName] = useState('');
  const [newPass, setNewPass] = useState('');
  const [newRole, setNewRole] = useState<'user' | 'admin'>('user');
  const [msg, setMsg] = useState<{ ok: boolean; text: string } | null>(null);

  useEffect(() => {
    a.refreshUsers();
  }, []);

  useEffect(() => {
    if (a.userResult) {
      if (a.userResult.ok) {
        setNewName(''); setNewPass('');
        setMsg({ ok: true, text: 'Операция выполнена.' });
      } else {
        setMsg({ ok: false, text: a.userResult.error || 'Ошибка' });
      }
    }
  }, [a.userResult]);

  const addUser = () => {
    if (!newName.trim() || !newPass || newPass.length < 4) {
      setMsg({ ok: false, text: 'Заполните логин и пароль (мин. 4 символа).' });
      return;
    }
    a.createUser(newName.trim(), newPass, newRole);
  };

  return (
    <div style={panel}>
      <div style={panelHead}><Icon name="shield" color={C.cyan} size={18} width={1.7} />Пользователи</div>
      <div style={{ fontSize: 12, color: C.textDim, marginBottom: 14 }}>управление учётными записями · доступно только admin</div>

      <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr auto', gap: 10, marginBottom: 18, alignItems: 'end' }}>
        <div>
          <div style={{ fontSize: 13, color: C.tableText, marginBottom: 4 }}>Логин</div>
          <input value={newName} onChange={(e) => setNewName(e.target.value)} placeholder="имя · a-z 0-9 _ . -" style={input} />
        </div>
        <div>
          <div style={{ fontSize: 13, color: C.tableText, marginBottom: 4 }}>Пароль</div>
          <input type="password" value={newPass} onChange={(e) => setNewPass(e.target.value)} placeholder="мин. 4 символа" style={input} />
        </div>
        <div style={{ display: 'flex', gap: 8, alignItems: 'center' }}>
          <button style={{ ...roleBtn, ...(newRole === 'user' ? roleBtnActive : null), color: newRole === 'user' ? C.cyan : C.textMid }} onClick={() => setNewRole('user')}>USER</button>
          <button style={{ ...roleBtn, ...(newRole === 'admin' ? roleBtnActive : null), color: newRole === 'admin' ? C.accent : C.textMid }} onClick={() => setNewRole('admin')}>ADMIN</button>
          <button style={{ ...btn, background: 'rgba(76,217,123,.12)', borderColor: 'rgba(76,217,123,.5)', color: '#4cd97b', padding: '10px 16px' }} onClick={addUser}>Добавить</button>
        </div>
      </div>

      {a.users.map((u) => (
        <div key={u.username} style={{ display: 'flex', alignItems: 'center', gap: 12, padding: '10px 14px', borderRadius: 10, border: '1px solid rgba(255,255,255,.06)', marginBottom: 8 }}>
          <div style={{ flex: 1, minWidth: 0 }}>
            <div style={{ fontSize: 14, color: u.username === a.username ? C.accent : C.tableText }}>
              {u.username} {u.username === a.username && <span style={{ fontSize: 10, color: C.accent }}>(вы)</span>}
            </div>
            <div style={{ fontSize: 11, color: C.textDim, fontFamily: C.mono }}>
              создан {u.createdAtUtc ? new Date(u.createdAtUtc).toLocaleDateString('ru-RU') : '—'}
            </div>
          </div>
          <div style={{ display: 'flex', gap: 8 }}>
            <button style={{ ...roleBtn, ...(u.role === 'admin' ? roleBtnActive : null), color: u.role === 'admin' ? C.accent : C.textMid }} onClick={() => a.setUserRole(u.username, 'admin')}>ADMIN</button>
            <button style={{ ...roleBtn, ...(u.role === 'user' ? roleBtnActive : null), color: u.role === 'user' ? C.cyan : C.textMid }} onClick={() => a.setUserRole(u.username, 'user')}>USER</button>
            <button style={{ ...roleBtn, color: C.red, borderColor: 'rgba(255,84,112,.35)' }} onClick={() => a.deleteUser(u.username)} title="Удалить">УДАЛИТЬ</button>
          </div>
        </div>
      ))}

      {msg && <div style={{ fontSize: 12, color: msg.ok ? C.green : C.red, marginTop: 10 }}>{msg.text}</div>}
    </div>
  );
}

function Field({ label, hint, value, onChange, password, disabled }: { label: string; hint: string; value: string; onChange: (v: string) => void; password?: boolean; disabled?: boolean }) {
  return (
    <div style={{ marginBottom: 12 }}>
      <div style={{ fontSize: 13, color: C.tableText, marginBottom: 4 }}>{label}</div>
      <input
        type={password ? 'password' : 'text'}
        value={value}
        onChange={(e) => onChange(e.target.value)}
        placeholder={hint}
        disabled={disabled}
        style={{ ...input, opacity: disabled ? 0.45 : 1, cursor: disabled ? 'not-allowed' : 'text' }}
      />
      <div style={{ fontSize: 11, color: C.textDim, marginTop: 4 }}>{hint}</div>
    </div>
  );
}

const panel: React.CSSProperties = {
  background: 'rgba(10, 15, 24, 0.72)',
  border: '1px solid rgba(255,255,255,0.07)',
  borderRadius: 18,
  padding: 24,
  backdropFilter: 'blur(20px)',
  WebkitBackdropFilter: 'blur(20px)',
  boxShadow: '0 8px 32px rgba(0,0,0,0.3)',
};
const panelHead: React.CSSProperties = {
  fontFamily: "'Syne', sans-serif",
  fontSize: 16,
  fontWeight: 700,
  letterSpacing: '-0.01em',
  color: '#f8fafc',
  marginBottom: 14,
  display: 'flex',
  alignItems: 'center',
  gap: 10,
};
const row: React.CSSProperties = {
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'space-between',
  padding: '12px 0',
};
const input: React.CSSProperties = {
  width: '100%',
  padding: '10px 14px',
  borderRadius: 10,
  outline: 'none',
  background: 'rgba(255,255,255,0.03)',
  border: '1px solid rgba(255,255,255,0.09)',
  color: '#f8fafc',
  fontFamily: C.mono,
  fontSize: 12.5,
  transition: 'border-color .18s',
};
const btn: React.CSSProperties = {
  padding: '7px 16px',
  borderRadius: 8,
  cursor: 'pointer',
  fontFamily: C.mono,
  fontSize: 11.5,
  letterSpacing: '.06em',
  fontWeight: 600,
  background: 'rgba(56,189,248,0.12)',
  border: '1px solid rgba(56,189,248,0.35)',
  color: '#38bdf8',
  transition: 'all .18s',
};
const roleBtn: React.CSSProperties = {
  padding: '6px 12px',
  borderRadius: 8,
  cursor: 'pointer',
  fontFamily: C.mono,
  fontSize: 10.5,
  fontWeight: 600,
  letterSpacing: '.08em',
  background: 'rgba(255,255,255,0.03)',
  border: '1px solid rgba(255,255,255,0.08)',
  transition: 'all .18s',
};
const roleBtnActive: React.CSSProperties = {
  background: 'rgba(255,255,255,0.12)',
  borderColor: 'rgba(255,255,255,0.3)',
  boxShadow: '0 2px 10px rgba(0,0,0,0.3)',
};
