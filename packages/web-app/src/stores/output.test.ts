import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import type { client as V6Client } from '../api/client'

import { useOutputStore } from './output'

const { call } = vi.hoisted(() => ({
  call: vi.fn<(op: string, data?: Record<string, unknown>) => Promise<unknown>>(),
}))

vi.mock(import('../api/client'), () => ({
  client: { call, on: vi.fn<() => () => void>() } as unknown as typeof V6Client,
}))

beforeEach(() => {
  setActivePinia(createPinia())
  call.mockReset()
})

describe('the audio output', () => {
  it('reads the list and switches to a named device', async () => {
    const output = useOutputStore()
    call.mockResolvedValue({ active: 'Speakers', devices: ['Speakers', 'Headphones'] })

    await output.refreshOutputs()
    expect(output.outputs.devices).toStrictEqual(['Speakers', 'Headphones'])

    call.mockResolvedValue({ active: 'Headphones', devices: ['Speakers', 'Headphones'] })
    await output.setOutput('Headphones')

    expect(call).toHaveBeenCalledWith('player_set_output', { device: 'Headphones' })
    expect(output.outputs.active).toBe('Headphones')
  })
})
