import { API_URL } from './config.js';

export interface Session {
  token: string;
  username: string;
  role: 'admin' | 'user';
  mustChangePassword?: boolean;
}

const TOKEN_KEY = 'ai_supporter_token';
const USER_KEY = 'ai_supporter_user';

export function getSession(): Session | null {
  try {
    const token = localStorage.getItem(TOKEN_KEY);
    const raw = localStorage.getItem(USER_KEY);
    if (!token || !raw) return null;
    const parsed = JSON.parse(raw);
    if (!parsed.username) return null;
    return { token, username: parsed.username, role: parsed.role === 'admin' ? 'admin' : 'user', mustChangePassword: !!parsed.mustChangePassword };
  } catch {
    return null;
  }
}

export function saveSession(token: string, username: string, role: string, mustChangePassword?: boolean): void {
  try {
    localStorage.setItem(TOKEN_KEY, token);
    localStorage.setItem(USER_KEY, JSON.stringify({ username, role, mustChangePassword: !!mustChangePassword }));
  } catch {}
}

export function clearSession(): void {
  try {
    localStorage.removeItem(TOKEN_KEY);
    localStorage.removeItem(USER_KEY);
  } catch {}
}

export function getToken(): string | null {
  return getSession()?.token ?? null;
}

export function apiFetch(path: string, init?: RequestInit): Promise<Response> {
  const headers: Record<string, string> = { ...(init?.headers as Record<string, string> | undefined) };
  const token = getToken();
  if (token) headers['Authorization'] = `Bearer ${token}`;
  if (typeof init?.body === 'string' && !headers['Content-Type']) headers['Content-Type'] = 'application/json';
  return fetch(`${API_URL}${path}`, { ...init, headers });
}
