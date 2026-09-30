import { useState } from 'react';
import { C } from '../lib/theme.js';
import { apiFetch, clearSession } from '../lib/auth.js';

export function ForcePasswordScreen({ username, onChanged }: { username: string; onChanged: () => void }) {
  const [current, setCurrent] = useState('');
  const [next, setNext] = useState('');
  const [confirm, setConfirm] = useState('');
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!current || next.length < 4) {
      setError('Новый пароль — минимум 4 символа.');
      return;
    }
    if (next !== confirm) {
      setError('Пароли не совпадают.');
      return;
    }
    if (next === current) {
      setError('Новый пароль не должен совпадать с текущим.');
      return;
    }
    setBusy(true);
    setError('');
    try {
      const res = await apiFetch('/api/users/password', {
        method: 'POST',
        body: JSON.stringify({ current, next }),
      });
      const data = await res.json();
      if (!res.ok) {
        setError(data.error || 'Не удалось сменить пароль.');
        return;
      }
      onChanged();
    } catch {
      setError('Нет соединения с сервером.');
    } finally {
      setBusy(false);
    }
  };

  const logout = () => {
    clearSession();
    onChanged();
    window.location.reload();
  };

  return (
    <div style={{ position: 'fixed', inset: 0, background: C.bg0, display: 'flex', alignItems: 'center', justifyContent: 'center', fontFamily: C.ui, color: C.textHi }}>
      <div style={{ position: 'absolute', inset: 0, background: 'radial-gradient(120% 90% at 50% 30%, rgba(255,107,91,.07), transparent 55%)' }} />
      <form onSubmit={submit} style={{ position: 'relative', width: 420, padding: 34, borderRadius: 20, background: C.panelSolid, border: `1px solid ${C.line}`, backdropFilter: 'blur(14px)', boxShadow: '0 20px 60px rgba(0,0,0,.5)' }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: 12, marginBottom: 8 }}>
          <div style={{ width: 9, height: 9, borderRadius: '50%', background: C.accent, boxShadow: `0 0 12px ${C.accent}` }} />
          <div style={{ fontSize: 15, fontWeight: 600, letterSpacing: '.3em' }}>A R I A</div>
        </div>
        <div style={{ fontSize: 12, color: C.amber, marginBottom: 6, fontWeight: 600, letterSpacing: '.06em' }}>
          ТРЕБУЕТСЯ СМЕНА ПАРОЛЯ
        </div>
        <div style={{ fontSize: 12, color: C.textDim, marginBottom: 26 }}>
          Вы вошли с учётной записью по умолчанию <b style={{ color: '#dbe3ee' }}>{username}</b>. Задайте новый пароль, чтобы продолжить.
        </div>

        <label style={{ fontSize: 12, color: C.textMid, display: 'block', marginBottom: 6 }}>Текущий пароль</label>
        <input type="password" value={current} onChange={(e) => setCurrent(e.target.value)} autoFocus style={input} placeholder="••••••••" />

        <label style={{ fontSize: 12, color: C.textMid, display: 'block', marginTop: 16, marginBottom: 6 }}>Новый пароль</label>
        <input type="password" value={next} onChange={(e) => setNext(e.target.value)} style={input} placeholder="минимум 4 символа" />

        <label style={{ fontSize: 12, color: C.textMid, display: 'block', marginTop: 16, marginBottom: 6 }}>Повторите новый пароль</label>
        <input type="password" value={confirm} onChange={(e) => setConfirm(e.target.value)} style={input} placeholder="••••••••" />

        {error && <div style={{ marginTop: 14, fontSize: 13, color: C.red }}>{error}</div>}

        <button
          type="submit"
          disabled={busy}
          style={{
            marginTop: 24, width: '100%', padding: '12px 0', borderRadius: 12, cursor: 'pointer',
            background: C.accent, color: '#1a0a07', border: 'none', fontFamily: C.mono, fontSize: 13,
            fontWeight: 600, letterSpacing: '.12em', opacity: busy ? 0.6 : 1,
          }}
        >
          {busy ? 'СОХРАНЕНИЕ…' : 'СМЕНИТЬ ПАРОЛЬ'}
        </button>

        <button
          type="button"
          onClick={logout}
          style={{
            marginTop: 10, width: '100%', padding: '10px 0', borderRadius: 12, cursor: 'pointer',
            background: 'transparent', color: C.textDim, border: `1px solid ${C.line}`,
            fontFamily: C.mono, fontSize: 12,
          }}
        >
          ВЫЙТИ
        </button>
      </form>
    </div>
  );
}

const input: React.CSSProperties = {
  width: '100%', padding: '11px 13px', borderRadius: 10, outline: 'none',
  background: 'rgba(0,0,0,.3)', border: `1px solid ${C.line}`,
  color: C.textHi, fontFamily: C.mono, fontSize: 14, boxSizing: 'border-box',
};