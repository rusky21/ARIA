import { beforeEach, describe, expect, it, vi } from 'vitest';
import { apiFetch, clearSession, getSession, getToken, saveSession } from './auth.js';

const TOKEN_KEY = 'ai_supporter_token';
const USER_KEY = 'ai_supporter_user';

describe('session storage', () => {
  beforeEach(() => {
    localStorage.clear();
  });

  it('сохраняет и читает сессию', () => {
    saveSession('tok123', 'alice', 'admin');

    expect(getSession()).toEqual({ token: 'tok123', username: 'alice', role: 'admin', mustChangePassword: false });
    expect(getToken()).toBe('tok123');
  });

  it('сохраняет и читает флаг принудительной смены пароля', () => {
    saveSession('t', 'alice', 'admin', true);

    expect(getSession()?.mustChangePassword).toBe(true);
  });

  it('нормализует неизвестную роль в user', () => {
    saveSession('t', 'bob', 'moderator');

    expect(getSession()?.role).toBe('user');
  });

  it('возвращает null без сохранённой сессии', () => {
    expect(getSession()).toBeNull();
    expect(getToken()).toBeNull();
  });

  it('возвращает null, если токен есть, а пользователя нет', () => {
    localStorage.setItem(TOKEN_KEY, 't');
    localStorage.setItem(USER_KEY, JSON.stringify({ role: 'admin' }));

    expect(getSession()).toBeNull();
  });

  it('возвращает null при битом JSON в хранилище', () => {
    localStorage.setItem(TOKEN_KEY, 't');
    localStorage.setItem(USER_KEY, '{oops');

    expect(getSession()).toBeNull();
    expect(getToken()).toBeNull();
  });

  it('clearSession удаляет данные', () => {
    saveSession('t', 'u', 'user');
    clearSession();

    expect(localStorage.getItem(TOKEN_KEY)).toBeNull();
    expect(localStorage.getItem(USER_KEY)).toBeNull();
    expect(getSession()).toBeNull();
  });
});

describe('apiFetch', () => {
  beforeEach(() => {
    localStorage.clear();
    vi.unstubAllGlobals();
  });

  it('добавляет Authorization Bearer и Content-Type для строкового body', async () => {
    saveSession('secret-token', 'u', 'user');
    const fetchMock = vi.fn(async () => new Response('{}'));
    vi.stubGlobal('fetch', fetchMock);

    await apiFetch('/api/ping', { method: 'POST', body: '{}' });

    expect(fetchMock).toHaveBeenCalledTimes(1);
    const [url, init] = fetchMock.mock.calls[0] as unknown as [string, RequestInit];
    expect(url).toBe('/api/ping');
    const headers = init.headers as Record<string, string>;
    expect(headers['Authorization']).toBe('Bearer secret-token');
    expect(headers['Content-Type']).toBe('application/json');
  });

  it('не добавляет токен, если сессии нет', async () => {
    const fetchMock = vi.fn(async () => new Response('{}'));
    vi.stubGlobal('fetch', fetchMock);

    await apiFetch('/api/ping');

    const [, init] = fetchMock.mock.calls[0] as unknown as [string, RequestInit];
    const headers = init.headers as Record<string, string>;
    expect(headers['Authorization']).toBeUndefined();
  });
});
