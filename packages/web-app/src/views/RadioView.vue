<script setup lang="ts">
import { onMounted } from 'vue'

import IconRadio from '~icons/lucide/radio'

import EmptyState from '../components/EmptyState.vue'
import { useRadioStore } from '../stores/radio'

const radio = useRadioStore()

onMounted(() => {
  void radio.load()
})
</script>

<template>
  <div class="flex h-full flex-col">
    <div class="flex items-center justify-between border-b border-surface-2 p-3 text-sm font-medium">
      <span>{{ $t('radio.title') }}</span>
      <span class="text-2xs tabular-nums text-outline">{{ radio.total }}</span>
    </div>

    <div class="flex flex-1 flex-col overflow-y-auto">
      <ul>
        <li v-for="station in radio.stations" :key="station.url">
          <button
            class="flex w-full items-center gap-3 border-b border-surface-2 px-3 py-3 text-left transition-colors hover:bg-surface-2/40 active:bg-surface-2/70"
            @click="radio.play(station.url)"
          >
            <IconRadio class="size-5 shrink-0 text-outline" />
            <span class="min-w-0 flex-1 truncate text-sm">{{ station.name }}</span>
          </button>
        </li>
      </ul>

      <EmptyState
        v-if="!radio.loading && radio.stations.length === 0"
        :icon="IconRadio"
        :title="$t('radio.empty.title')"
        :hint="$t('radio.empty.hint')"
      />

      <button
        v-if="radio.hasMore"
        class="w-full p-4 text-sm text-accent"
        :disabled="radio.loading"
        @click="radio.load(true)"
      >
        {{ radio.loading ? $t('common.state.loading') : $t('common.state.loadMore') }}
      </button>
    </div>
  </div>
</template>
