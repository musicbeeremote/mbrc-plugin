/*
 * The service worker: what makes the app installable, and what makes it open
 * without a blank page when MusicBee is not reachable.
 *
 * It caches nothing at install time. The asset names carry their content hash
 * and are only knowable after a build, so they are cached as they are asked for
 * instead - which costs one ordinary load and then never asks again, because a
 * fingerprinted name never changes what it points at.
 *
 * Nothing under /api/ is ever cached or served from a cache. That is the live
 * state of a player: a stale answer there is worse than an error.
 */

const SHELL = 'mbrc-shell-v1'

/** The key the entry document is kept under, whatever path a navigation asked for. */
const SHELL_KEY = '/index.html'

/*
 * Take over at once rather than waiting for every tab to close. The entry
 * document is fetched from the network first and the assets it names carry
 * their hashes, so a running app is never handed half of one build and half of
 * another.
 */
self.addEventListener('install', () => self.skipWaiting())

self.addEventListener('activate', (event) => {
  event.waitUntil(
    (async () => {
      const names = await caches.keys()
      await Promise.all(names.filter((name) => name !== SHELL).map((name) => caches.delete(name)))
      await self.clients.claim()
    })(),
  )
})

self.addEventListener('fetch', (event) => {
  const request = event.request
  if (request.method !== 'GET') return

  const url = new URL(request.url)
  if (url.origin !== self.location.origin) return
  if (url.pathname.startsWith('/api/') || url.pathname.startsWith('/ws')) return

  if (request.mode === 'navigate') {
    event.respondWith(shell(request))
  } else if (url.pathname.startsWith('/assets/')) {
    event.respondWith(fingerprinted(request))
  }
})

/**
 * The entry document, from the network when there is one.
 *
 * Network first because it is the only unfingerprinted file: served from a
 * cache it would point a new build's browser at asset names that no longer
 * exist. The copy is kept only so the app still opens when the server is gone,
 * where it can say so rather than showing the browser's error page.
 */
async function shell(request) {
  try {
    const response = await fetch(request)
    if (response.ok) {
      const cache = await caches.open(SHELL)
      await cache.put(SHELL_KEY, response.clone())
    }
    return response
  } catch (error) {
    const cached = await caches.match(SHELL_KEY)
    if (cached) return cached
    throw error
  }
}

/** A hashed asset: the name is the version, so a hit is always the right answer. */
async function fingerprinted(request) {
  const cached = await caches.match(request)
  if (cached) return cached

  const response = await fetch(request)
  if (response.ok) {
    const cache = await caches.open(SHELL)
    await cache.put(request, response.clone())
  }
  return response
}
