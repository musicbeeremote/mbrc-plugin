import { beforeEach, describe, expect, it, vi } from 'vitest'

import { client } from '../api/client'
import { Op, WireEvent } from '../api/ops'
import { useUpdateWatch } from './useUpdateWatch'

type Reconnect = () => Promise<void>

/** Answers `system_info` with each version in turn, then repeats the last. */
function serving(...versions: string[]) {
  let call = 0
  vi.spyOn(client, 'call').mockImplementation(async () => {
    const version = versions[Math.min(call, versions.length - 1)]
    call += 1
    return { plugin_version: version, protocol_version: 6 } as never
  })
}

/**
 * Binds a watch and hands back its reconnect, which is what an updated plugin
 * looks like from the browser: the socket drops and comes back.
 */
function watching(): [ReturnType<typeof useUpdateWatch>, Reconnect] {
  let listener: ((data: { connected: boolean }) => void) | undefined = undefined
  vi.spyOn(client, 'on').mockImplementation(((event: string, fn: unknown) => {
    if (event === WireEvent.ConnectionChanged) {
      listener = fn as (data: { connected: boolean }) => void
    }
    return () => undefined
  }) as never)

  const watch = useUpdateWatch()
  watch.bind()

  return [
    watch,
    async () => {
      listener?.({ connected: true })
      await vi.waitFor(() => expect(client.call).toHaveBeenCalledWith(Op.SystemInfo))
    },
  ]
}

beforeEach(() => {
  vi.restoreAllMocks()
})

describe('noticing the plugin was replaced', () => {
  it('takes the first version it sees as the one this page was loaded against', async () => {
    serving('1.5.0')
    const [watch, reconnect] = watching()

    await reconnect()

    expect(watch.loaded.value).toBe('1.5.0')
    expect(watch.available.value).toBeNull()
  })

  it('reports a version that is not the one it started with', async () => {
    serving('1.5.0', '1.6.0')
    const [watch, reconnect] = watching()

    await reconnect()
    await reconnect()

    expect(watch.available.value).toBe('1.6.0')
  })

  it('says nothing while the version holds', async () => {
    serving('1.5.0')
    const [watch, reconnect] = watching()

    await reconnect()
    await reconnect()

    expect(watch.available.value).toBeNull()
  })

  // Not knowing the version is not evidence of a new one, and a prompt raised
  // by a dropped request would be worse than the staleness it guards against.
  it('says nothing when the ask fails', async () => {
    vi.spyOn(client, 'call').mockRejectedValue(new Error('no route to host'))
    const [watch, reconnect] = watching()

    await reconnect()

    expect(watch.loaded.value).toBeNull()
    expect(watch.available.value).toBeNull()
  })
})
