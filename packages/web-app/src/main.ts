import { createPinia } from 'pinia'
import { createApp } from 'vue'

import { forgetLegacyToken } from './api/session'
import App from './App.vue'
import { i18n } from './i18n'
import { router } from './router'
import './style.css'

forgetLegacyToken()

createApp(App).use(createPinia()).use(router).use(i18n).mount('#app')

/*
 * The service worker, which is what lets a phone install this and stop looking
 * at the artwork through a browser's address bar.
 *
 * Not in development, where the dev server owns the same origin and a worker
 * caching its own output is a morning lost. `updateViaCache: 'none'` because
 * the worker's name is fixed: served from a cache it would keep a build alive
 * that nothing else on the page still belongs to.
 */
if (import.meta.env.PROD && 'serviceWorker' in navigator) {
  globalThis.addEventListener('load', () => {
    void navigator.serviceWorker.register('/sw.js', { updateViaCache: 'none' })
  })
}
