import { defineConfig } from 'vitest/config'

export default defineConfig({
  test: {
    // The client is browser code: it reaches for WebSocket, fetch and
    // localStorage, none of which exist in a bare node environment.
    environment: 'happy-dom',
    coverage: {
      provider: 'v8',
      // lcov for Codecov, text so a local run says something without a browser.
      reporter: ['text', 'lcov'],
      // The TypeScript the app is built out of: the client, the stores, the
      // composables, the router. Components are left out because nothing
      // renders them in a test, so counting them would report a number about
      // tests that do not exist rather than about the code.
      //
      // The tests themselves and the one stand-in they import are excluded:
      // counting the harness as covered says nothing about the product.
      // `all` so a module no test touches is reported as uncovered instead of
      // being left out of the total, which is how a gap hides.
      all: true,
      include: ['src/**/*.ts'],
      exclude: ['src/**/*.test.ts', 'src/api/fake-socket.ts', 'src/**/*.d.ts'],
    },
  },
})
