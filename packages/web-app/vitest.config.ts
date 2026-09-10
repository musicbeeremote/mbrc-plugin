import { defineConfig } from 'vitest/config'

export default defineConfig({
  test: {
    // The client is browser code: it reaches for WebSocket, fetch and
    // localStorage, none of which exist in a bare node environment.
    environment: 'happy-dom',
  },
})
