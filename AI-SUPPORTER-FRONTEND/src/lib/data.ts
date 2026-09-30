export interface Packet {
  no: number;
  time: string;
  src: string;
  dst: string;
  proto: string;
  sport?: number;
  dport?: number;
  ttl?: number;
  raw?: string;
  len: number;
  info: string;
  seq?: number;
}

export interface InspectorTarget {
  title: string;
  ips: string[];
}

export interface Message {
  role: 'user' | 'assistant' | 'tool';
  text?: string;
  name?: string;
  result?: string;
  ms?: number;
  image?: string;
}

export interface TermLine {
  text: string;
  color: string;
}

export interface ProcessInfo {
  pid: number;
  name: string;
  cpu: number;
  ram: number;
  mem: string;
  gpu: number;
}

export interface TaskLoad {
  id: string;
  name: string;
  icon: string;
  enabled: boolean;
  continuous: boolean;
  cpu: number;
  ram: number | null;
  ramMb: string | null;
  gpu: number | null;
  inFlight: boolean;
  lastRun?: string;
  nextRun?: string;
  lastError?: string;
}

export interface TaskItem {
  id: string;
  name: string;
  schedule: string;
  desc: string;
  lastRun: string;
  nextRun?: string;
  lastError?: string;
  enabled: boolean;
  icon: string;
}

export interface NetworkInterface {
  id: string;
  name: string;
  description: string;
  ip: string;
  mac: string;
  type: string;
  speed: number;
  up: boolean;
  loopback: boolean;
}

export interface FileItem {
  name: string;
  size: string;
  dir: boolean;
  sel?: boolean;
}

export interface DevMessage {
  role: 'user' | 'assistant';
  text: string;
  reasoning?: string;
}

export interface DevHunk {
  oldStart: number;
  oldLines: string[];
  newStart: number;
  newLines: string[];
}

export interface DevFileChange {
  path: string;
  hunks: DevHunk[];
}

export interface DevAgentInfo {
  id: string;
  type: string;
}

export interface LlmStatus {
  ok: boolean;
  ms: number;
  model: string;
}

export interface LlmConfig {
  provider: 'local' | 'api';
  local: { url: string; model: string };
  api: { baseUrl: string; model: string };
  status: LlmStatus | null;
}

export interface FlowSummary {
  srcIp: string; dstIp: string; srcPort: number; dstPort: number; protocol: string;
  packetCount: number; totalBytes: number; firstSeen: string; lastSeen: string;
}

export interface ProtocolStat {
  protocol: string; count: number; totalBytes: number; percentage: number;
}

export interface TimeSeriesPoint {
  timestamp: string; packetCount: number; totalBytes: number;
  protocols: Record<string, number>;
}

export interface HostStat {
  ip: string; name: string | null; sentPackets: number; receivedPackets: number;
  sentBytes: number; receivedBytes: number; uniqueDestinations: number; uniqueSources: number;
}

export interface AnomalyEvent {
  id: string; timestamp: string; type: string; description: string;
  relatedIp: string | null; severity: number;
}

export interface CommandPattern {
  signature: string; occurrences: number; intervalMs: number;
  description: string; relatedIps: string[];
}

export interface PeriodComparison {
  currentPackets: number; previousPackets: number; changePercent: number;
  currentFlows: number; previousFlows: number;
}

export interface TrafficReport {
  reportType: string; generatedAt: string; periodStart: string; periodEnd: string;
  totalPackets: number; totalBytes: number;
  protocols: ProtocolStat[];
  topFlows: FlowSummary[];
  topHosts: HostStat[];
  timeSeries: TimeSeriesPoint[];
  anomalies: AnomalyEvent[];
  commandPatterns: CommandPattern[];
  comparison: PeriodComparison | null;
  llmSummary: string | null;
}

export interface ConversationMeta {
  id: string;
  title: string;
  createdAt: string;
  lastMessageAt: string;
}

export interface FsEntry {
  name: string;
  path: string;
  isDir: boolean;
  size: number;
}

export interface FsReadResult {
  path: string | null;
  content: string | null;
  truncated: boolean;
  size: number;
  binary: boolean;
}

export interface UserEntry {
  username: string;
  role: string;
  createdAtUtc: string;
}

export interface WakeTrainResult {
  ok: boolean;
  frames: number;
  durationSec: number;
  templatesCount: number;
  error: string | null;
}

export interface UserResult {
  ok: boolean;
  error: string | null;
}

export interface Device {
  ip: string;
  name: string;
  type: string;
  online: boolean;
  services: string[];
  mjpegUrl: string | null;
  lastSeen: string;
  mac: string | null;
  vendor: string | null;
  responseMs: number;
  notes: string | null;
}

// ── LLM Profile & Capabilities ──

export interface TaskModelSelection {
  provider: 'local' | 'api';
  model: string;
  autoSelect: boolean;
}

export interface SpeechSelection {
  provider: 'local' | 'api';
  model: string;
  lang: string;
}

export interface UserLlmProfile {
  username: string;
  chat: TaskModelSelection;
  voice: TaskModelSelection;
  vision: TaskModelSelection;
  toolCalling: TaskModelSelection;
  speech: SpeechSelection;
}

export interface ModelCapability {
  id: string;
  vision: boolean;
  tasks: string[];
}

export interface ProviderCapabilities {
  available: boolean;
  chat: boolean;
  tools: boolean;
  vision: boolean;
  models: ModelCapability[];
}

export interface LlmCapabilities {
  local: ProviderCapabilities | null;
  api: ProviderCapabilities | null;
}

export interface TtsConf {
  engine: 'local' | 'api';
  voice: string;
  ok: boolean;
}

export interface TtsModels {
  local: string[];
  api: string[];
}

export interface TtsCheck {
  engine: string;
  voice: string;
  ok: boolean;
  ms: number;
  message?: string;
}
