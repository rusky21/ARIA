import { useState, useEffect, useRef, useCallback, Component, type ReactNode } from 'react'
import { WS_URL } from './lib/config.js'
import { getSession, saveSession, clearSession, type Session } from './lib/auth.js'
import { useAssistant } from './hooks/useAssistant.js'
import { useOrb } from './hooks/useOrb.js'
import { useWakeChunks } from './hooks/useWakeChunks.js'
import Login from './components/Login.jsx'
import Home from './components/Home.jsx'
import Voice from './components/Voice.jsx'
import Shell from './components/Shell.jsx'
import Chat from './components/Chat.jsx'
import Network from './components/Network.jsx'
import Devices from './components/Devices.jsx'
import System from './components/System.jsx'
import Terminal from './components/Terminal.jsx'
import Files from './components/Files.jsx'
import Tasks from './components/Tasks.jsx'
import Settings from './components/Settings.jsx'
import Dev from './components/Dev.jsx'
import FloatingWindows from './components/FloatingWindows.jsx'
import { ForcePasswordScreen } from './components/ForcePassword.jsx'

type Screen = 'home' | 'voice' | 'chat' | 'network' | 'devices' | 'system' | 'terminal' | 'files' | 'tasks' | 'settings' | 'dev'

const SCREENS: Record<string, React.ComponentType<{ a: ReturnType<typeof useAssistant>; ws?: WebSocket | null }>> = {
  chat: Chat, network: Network, devices: Devices, system: System, terminal: Terminal,
  files: Files, tasks: Tasks, settings: Settings, dev: Dev,
}

function App() {
  const [session, setSession] = useState<Session | null>(() => getSession())
  const [connected, setConnected] = useState(false)
  const wsRef = useRef<WebSocket | null>(null)
  const tokenRef = useRef<string | null>(session?.token ?? null)
  const [ws, setWs] = useState<WebSocket | null>(null)
  const [forcePassword, setForcePassword] = useState(() => !!session?.mustChangePassword)

  useEffect(() => {
    if (!tokenRef.current) return
    let disposed = false
    let timer: ReturnType<typeof setTimeout> | undefined
    let sock: WebSocket | null = null

    const connect = () => {
      if (disposed || !tokenRef.current) return
      const url = WS_URL + '?token=' + encodeURIComponent(tokenRef.current)
      const ws = new WebSocket(url)
      sock = ws
      wsRef.current = ws
      setWs(ws)
      ws.onopen = () => setConnected(true)
      ws.onerror = () => setConnected(false)
      ws.onclose = () => {
        setConnected(false)
        if (!disposed && tokenRef.current) timer = setTimeout(connect, 2000)
      }
    }

    connect()

    return () => {
      disposed = true
      if (timer) clearTimeout(timer)
      sock?.close()
      wsRef.current = null
      setWs(null)
    }
  }, [session?.token])

  const handleLogin = useCallback((token: string, username: string, role: string, mustChangePassword?: boolean) => {
    saveSession(token, username, role, mustChangePassword)
    tokenRef.current = token
    setSession({ token, username, role: role === 'admin' ? 'admin' : 'user', mustChangePassword })
    setForcePassword(!!mustChangePassword)
  }, [])

  const handlePasswordChanged = useCallback(() => {
    setSession(s => (s ? { ...s, mustChangePassword: false } : s))
    setForcePassword(false)
    const raw = localStorage.getItem('ai_supporter_user')
    if (raw) {
      try {
        const parsed = JSON.parse(raw)
        localStorage.setItem('ai_supporter_user', JSON.stringify({ ...parsed, mustChangePassword: false }))
      } catch {}
    }
  }, [])

  const handleLogout = useCallback(() => {
    clearSession()
    tokenRef.current = null
    wsRef.current?.close()
    wsRef.current = null
    setWs(null)
    setConnected(false)
    setSession(null)
  }, [])

  const a = useAssistant(ws)
  const canvasRef = useRef<HTMLCanvasElement>(null)
  useOrb(canvasRef, a.stateRef)
  useWakeChunks(ws, a.wake && a.screen !== 'voice' && !a.speaking && !!session, a.enterVoice, a.setWakeScore)

  if (!session) {
    return <Login onSuccess={handleLogin} />
  }

  if (forcePassword) {
    const username = session.username
    return (
      <ForcePasswordScreen
        username={username}
        onChanged={handlePasswordChanged}
      />
    )
  }

  const isTab = a.screen !== 'home' && a.screen !== 'voice'
  const screen = (!a.isAdmin && (a.screen === 'dev' || a.screen === 'files')) ? ('settings' as Screen) : a.screen
  const Screen = SCREENS[screen]

  return (
    <div style={{ position: 'fixed', inset: 0, background: '#05080f', fontFamily: "'Chakra Petch', sans-serif", color: '#e8edf5', overflow: 'hidden' }}>
      <canvas ref={canvasRef} style={{ position: 'absolute', inset: 0, width: '100%', height: '100%', zIndex: 0, pointerEvents: 'none' }} />
      <div style={{ position: 'absolute', inset: 0, zIndex: 1, pointerEvents: 'none', background: 'radial-gradient(120% 90% at 50% 30%, rgba(255,107,91,.05), transparent 55%), linear-gradient(180deg, rgba(5,8,15,.2), rgba(5,8,15,.65))' }} />

      {a.screen === 'home' && <Home a={a} />}
      {a.screen === 'voice' && <Voice a={a} />}

      {isTab && (
        <Shell a={a} onLogout={handleLogout} connected={connected}>
          <TabBoundary>{Screen && <Screen a={a} ws={ws} />}</TabBoundary>
        </Shell>
      )}

      {a.floatingWindows.length > 0 && (
        <FloatingWindows
          windows={a.floatingWindows}
          onClose={a.closeFloatingWindow}
          onFocus={a.focusFloatingWindow}
        />
      )}
    </div>
  )
}

class TabBoundary extends Component<{ children: ReactNode }, { error: Error | null }> {
  state: { error: Error | null } = { error: null }
  static getDerivedStateFromError(error: Error) {
    return { error }
  }
  render() {
    if (this.state.error) {
      return (
        <div style={{ padding: '40px 50px' }}>
          <div style={{ fontSize: 14, fontWeight: 600, color: '#ff5470', marginBottom: 8 }}>Ошибка вкладки</div>
          <div style={{ fontFamily: 'monospace', fontSize: 12, color: '#7e8ba0', whiteSpace: 'pre-wrap' }}>{this.state.error.message}</div>
        </div>
      )
    }
    return this.props.children
  }
}

export default App
