import { useState } from 'react';
import { C, acc, R } from '../lib/theme.js';
import { API_URL } from '../lib/config.js';

export default function Login({ onSuccess }: { onSuccess: (token: string, username: string, role: string, mustChangePassword?: boolean) => void }) {
  const [username, setUsername] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!username.trim() || !password || busy) return;
    setBusy(true);
    setError('');
    try {
      const res = await fetch(`${API_URL}/api/auth/login`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ username: username.trim(), password }),
      });
      const data = await res.json();
      if (!res.ok) {
        setError(data.error === 'too many attempts, try again later' ? 'Слишком много попыток. Подождите.' : 'Неверные учётные данные.');
        return;
      }
      onSuccess(data.token, data.username, data.role, !!data.mustChangePassword);
    } catch {
      setError('Нет соединения с сервером.');
    } finally {
      setBusy(false);
    }
  };

  return (
    <div style={{ position: 'fixed', inset: 0, background: C.bg0, display: 'flex', alignItems: 'center', justifyContent: 'center', fontFamily: C.ui, color: C.textHi, animation: 'ariaFadeFast .22s ease both' }}>
      <div style={{ position: 'absolute', inset: 0, background: `radial-gradient(120% 90% at 50% 30%, ${acc(0.07)}, transparent 55%)` }} />
      <form onSubmit={submit} style={{ position: 'relative', width: 380, padding: 34, borderRadius: R.xxl, background: C.panelSolid, border: `1px solid ${C.line}`, backdropFilter: 'blur(14px)', boxShadow: '0 20px 60px rgba(0,0,0,.5)' }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: 12, marginBottom: 8 }}>
          <div style={{ width: 9, height: 9, borderRadius: '50%', background: C.accent, boxShadow: `0 0 12px ${C.accent}` }} />
          <div style={{ fontSize: 15, fontWeight: 600, letterSpacing: '.3em' }}>A R I A</div>
        </div>
        <div style={{ fontSize: 12, color: C.textDim, marginBottom: 26 }}>авторизация · AI Supporter</div>

        <label style={{ fontSize: 12, color: C.textMid, display: 'block', marginBottom: 6 }}>Логин</label>
        <input
          value={username}
          onChange={(e) => setUsername(e.target.value)}
          autoFocus
          style={input}
          placeholder="admin"
        />

        <label style={{ fontSize: 12, color: C.textMid, display: 'block', marginTop: 16, marginBottom: 6 }}>Пароль</label>
        <input
          type="password"
          value={password}
          onChange={(e) => setPassword(e.target.value)}
          style={input}
          placeholder="••••••••"
        />

        {error && <div style={{ marginTop: 14, fontSize: 13, color: C.red }}>{error}</div>}

        <button
          type="submit"
          disabled={busy}
          style={{
            marginTop: 24, width: '100%', padding: '12px 0', borderRadius: R.lg, cursor: 'pointer',
            background: C.accent, color: C.onAccent, border: 'none', fontFamily: C.mono, fontSize: 13,
            fontWeight: 600, letterSpacing: '.12em', opacity: busy ? 0.6 : 1,
            transition: 'opacity .18s, transform .18s',
          }}
        >
          {busy ? 'ПОДОЖДИТЕ…' : 'ВОЙТИ'}
        </button>
      </form>
    </div>
  );
}

const input: React.CSSProperties = {
  width: '100%', padding: '11px 13px', borderRadius: R.m, outline: 'none',
  background: C.inputBg, border: `1px solid ${C.line}`,
  color: C.textHi, fontFamily: C.mono, fontSize: 14, boxSizing: 'border-box',
};
