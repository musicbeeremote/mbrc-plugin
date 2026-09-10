/**
 * Noticing that the plugin under this page has been replaced.
 *
 * The app is served out of `mbrc_core.dll`, so updating the plugin swaps the
 * bundle a browser is already running. The old one keeps working until it asks
 * for something the new build renamed, and then fails somewhere unhelpful, so
 * it is better to say so plainly and offer a reload.
 *
 * The version the plugin reports is the signal. A service worker would be the
 * usual way, but those need a secure context and a phone reaches this server at
 * `http://<address>:3000`, where `navigator.serviceWorker` does not exist at
 * all - which is exactly the case that matters most.
 *
 * The plugin restarting is what an update looks like from here, so the check
 * rides the reconnect rather than a timer.
 */

import { readonly, ref } from 'vue'
import type { DeepReadonly, Ref } from 'vue'

import { client } from '../api/client'
import { Op, WireEvent } from '../api/ops'

export interface UpdateWatch {
  /** The plugin version this page was loaded against, once known. */
  loaded: DeepReadonly<Ref<string | null>>
  /** The version now being served, when it is not the one above. */
  available: DeepReadonly<Ref<string | null>>
  /** Starts watching. Called where the client's other listeners are bound. */
  bind: () => void
}

export function useUpdateWatch(): UpdateWatch {
  const loaded = ref<string | null>(null)
  const available = ref<string | null>(null)

  /**
   * A failed ask is left alone: not knowing the version is not evidence of a
   * new one, and a reload prompt raised by a dropped request would be worse
   * than the staleness it guards against.
   */
  async function servedVersion(): Promise<string | null> {
    try {
      const info = await client.call(Op.SystemInfo)
      return info.plugin_version
    } catch {
      return null
    }
  }

  async function check(): Promise<void> {
    const version = await servedVersion()
    if (version === null) return

    if (loaded.value === null) {
      loaded.value = version
      return
    }
    if (version !== loaded.value) available.value = version
  }

  function bind(): void {
    client.on(WireEvent.ConnectionChanged, (data) => {
      if (data.connected) void check()
    })
  }

  return { loaded: readonly(loaded), available: readonly(available), bind }
}
