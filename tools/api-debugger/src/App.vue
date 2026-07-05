<script setup lang="ts">
import { onMounted, ref } from "vue";
import DirectPanel from "./components/DirectPanel.vue";
import ProxyPanel from "./components/ProxyPanel.vue";
import SessionsPanel from "./components/SessionsPanel.vue";
import ComparePanel from "./components/ComparePanel.vue";
import { useProxyStore } from "./stores/proxy";
import { useDirectStore } from "./stores/direct";
import { usePlayerStore } from "./stores/player";

const tabs = ["Direct", "Proxy", "Sessions", "Compare"] as const;
const active = ref<(typeof tabs)[number]>("Direct");

// Subscribe the stores to backend events at app startup - not on panel mount -
// so their buffers accumulate across tab switches and even before a tab is
// first opened. Panels are plain views over the stores, so switching tabs
// (v-if below) can freely unmount/remount them without losing state.
onMounted(() => {
  useDirectStore().init();
  useProxyStore().init();
  usePlayerStore().init();
});
</script>

<template>
  <div class="flex h-screen flex-col bg-zinc-950 text-zinc-100">
    <header class="flex items-center gap-2.5 border-b border-zinc-800 px-4 py-3">
      <img src="/logo.png" alt="" class="h-6 w-6" />
      <h1 class="text-sm font-semibold tracking-wide text-zinc-200">
        MusicBee Remote · API Debugger
      </h1>
    </header>

    <nav class="flex gap-1 border-b border-zinc-800 px-2">
      <button
        v-for="t in tabs"
        :key="t"
        class="rounded-t px-3 py-2 text-xs font-medium transition-colors"
        :class="active === t ? 'bg-zinc-800 text-zinc-100' : 'text-zinc-400 hover:text-zinc-200'"
        @click="active = t"
      >
        {{ t }}
      </button>
    </nav>

    <main class="flex-1 overflow-hidden p-4">
      <DirectPanel v-if="active === 'Direct'" />
      <ProxyPanel v-else-if="active === 'Proxy'" />
      <SessionsPanel v-else-if="active === 'Sessions'" />
      <ComparePanel v-else-if="active === 'Compare'" />
    </main>
  </div>
</template>
