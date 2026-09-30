const envWsUrl = import.meta.env.VITE_WS_URL as string | undefined
const envApiUrl = import.meta.env.VITE_API_URL as string | undefined

const protocol = window.location.protocol === 'https:' ? 'wss:' : 'ws:'
export const WS_URL = envWsUrl || `${protocol}//${window.location.host}/ws`
export const API_URL = (envApiUrl || '').replace(/\/+$/, '')
