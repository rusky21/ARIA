/// <reference types="vitest/config" />
import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

export default defineConfig({
  plugins: [react()],
  base: './',
  build: {
    outDir: 'dist',
  },
  test: {
    environment: 'jsdom',
    globals: true,
  },
  server: {
    proxy: {
      '/ws': {
        target: 'ws://localhost:5000',
        ws: true,
      },
      '/events': {
        target: 'http://localhost:5000',
      },
      '/api': {
        target: 'http://localhost:5000',
      },
    },
  },
})
