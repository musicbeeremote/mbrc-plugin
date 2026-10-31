import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { call } from '../api/fake-client'

import { useOutputStore } from './output'

vi.mock(import('../api/client'), () => import('../api/fake-client'))

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
