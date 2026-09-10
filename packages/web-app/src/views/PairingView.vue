<script setup lang="ts">
import { onMounted, onUnmounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'

import { thisBrowser } from '../composables/describeBrowser'

const emit = defineEmits<{ paired: [] }>()

const { t } = useI18n()

/**
 * Re-checks whether pairing is still being asked for.
 *
 * The setting can be turned off in MusicBee while this screen is open, and a
 * user typing a code nobody will ask for has no way out of it.
 */
const RECHECK_MS = 5000

const code = ref('')
const error = ref('')
const busy = ref(false)

/**
 * What this browser will be called in MusicBee's list of paired browsers.
 *
 * Filled in with what the browser can work out about itself, and editable
 * because that answer stops being useful the moment there are two of them: from
 * the panel, one Chrome on Windows looks exactly like another, and only the
 * person sitting at this one knows which it is.
 */
const name = ref(thisBrowser())

/** As long a label as the core keeps, so nothing typed here is silently cut. */
const MAX_NAME = 64

let recheck: number | undefined = undefined

onMounted(() => {
  recheck = window.setInterval(async () => {
    const response = await fetch('/api/pair/status').catch(() => null)
    const status = (await response?.json().catch(() => null)) as { auth_required?: boolean } | null
    if (status && status.auth_required === false) emit('paired')
  }, RECHECK_MS)
})

onUnmounted(() => {
  if (recheck !== undefined) clearInterval(recheck)
})

async function submit() {
  busy.value = true
  error.value = ''
  try {
    const response = await fetch('/api/pair', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ code: code.value, label: name.value.trim() || thisBrowser() }),
    })
    const body = (await response.json()) as { token?: string; error?: { message: string } }
    if (!response.ok || !body.token) {
      error.value = body.error?.message ?? t('pairing.failed')
      return
    }
    // Nothing is kept here: the answer set a cookie the page cannot read, which
    // is what the browser will send from now on without being asked.
    emit('paired')
  } catch {
    error.value = t('pairing.unreachable')
  } finally {
    busy.value = false
  }
}
</script>

<template>
  <div class="flex h-full flex-col items-center justify-center gap-4 p-8 text-center">
    <h1 class="text-xl font-semibold">Pair this browser</h1>
    <i18n-t keypath="pairing.help" tag="p" class="max-w-xs text-sm text-outline" scope="global">
      <template #button>
        <strong>{{ $t('pairing.button') }}</strong>
      </template>
    </i18n-t>
    <input
      v-model="code"
      inputmode="numeric"
      autocomplete="one-time-code"
      maxlength="6"
      placeholder="000000"
      class="w-40 rounded-control border border-outline/50 bg-transparent p-3 text-center text-2xl tracking-widest"
      @keyup.enter="submit"
    />
    <label class="flex w-64 flex-col gap-1 text-left">
      <span class="text-2xs text-outline">{{ $t('pairing.name') }}</span>
      <input
        v-model="name"
        :maxlength="MAX_NAME"
        autocomplete="off"
        class="rounded-control border border-outline/50 bg-transparent p-2 text-sm"
        @keyup.enter="submit"
      />
    </label>

    <button
      class="rounded-control bg-accent px-6 py-2 text-white disabled:opacity-50"
      :disabled="busy || code.length === 0"
      @click="submit"
    >
      {{ $t('pairing.submit') }}
    </button>
    <p v-if="error" class="text-sm text-red-500">{{ error }}</p>
  </div>
</template>
