import { defineStore } from 'pinia'
import { ref } from 'vue'

import { client } from '../api/client'
import { Op } from '../api/ops'
import type { OutputDevices } from '../api/responses'

/**
 * Which audio device MusicBee is playing through.
 *
 * Apart from the player: the device outlives every track, MusicBee announces no
 * change to it, and the list is read only while the picker is open rather than
 * kept current.
 */
export const useOutputStore = defineStore('output', () => {
  const outputs = ref<OutputDevices>({ active: '', devices: [] })

  async function refreshOutputs() {
    outputs.value = await client.call(Op.PlayerOutput)
  }

  /** Answers with the full list, so the active device needs no second read. */
  async function setOutput(device: string) {
    outputs.value = await client.call(Op.PlayerSetOutput, { device })
  }

  return { outputs, refreshOutputs, setOutput }
})
