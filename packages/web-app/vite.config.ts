import { env } from 'node:process'

import tailwindcss from '@tailwindcss/vite'
import vue from '@vitejs/plugin-vue'
import Icons from 'unplugin-icons/vite'
import { defineConfig } from 'vite'

/**
 * Where `pnpm dev` forwards the API to: a MusicBee running the plugin.
 *
 * Override with MBRC_TARGET to develop against another machine's MusicBee,
 * which is also how the UI gets tested against a real library without one on
 * the dev box.
 */
const target = env.MBRC_TARGET ?? 'http://127.0.0.1:3000'
const wsTarget = target.replace(/^http/, 'ws')

// https://vite.dev/config/
export default defineConfig({
  // Absolute, not relative: the router is in history mode, and a deep link like
  // /library/albums would resolve a relative asset against /library/, where the
  // server answers every unknown path with the entry document.
  base: '/',
  // Icons compile to inline SVG components at build time, so only the ones
  // actually imported reach the bundle and nothing is fetched at runtime - both
  // of which matter when the bundle is embedded in the DLL behind a
  // self-only CSP.
  plugins: [vue(), tailwindcss(), Icons({ compiler: 'vue3', scale: 1 })],
  server: {
    // Bound to every interface so a phone on the LAN can load the dev server;
    // the plugin is a phone remote, and a layout only a desktop ever sees is
    // one that ships broken.
    host: true,
    proxy: {
      // changeOrigin stays off: the core's Host allowlist accepts the dev
      // server's own host (an address or localhost), and rewriting it would
      // hide a rejection that a production build would hit.
      '/api': { target },
      '/ws': { target: wsTarget, ws: true },
    },
  },
  build: {
    // Every byte here is DLL size, since the bundle is embedded in
    // mbrc_core.dll. Inlining small assets avoids extra files for free.
    assetsInlineLimit: 8192,
  },
})
