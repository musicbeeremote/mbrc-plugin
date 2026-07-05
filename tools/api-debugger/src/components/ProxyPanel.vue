<script setup lang="ts">
import { computed, onMounted, watch } from "vue";
import { storeToRefs } from "pinia";
import { useClipboard, useVirtualList } from "@vueuse/core";
import type { ProxyDir } from "../lib/api";
import { highlightJson } from "../lib/jsonHighlight";
import { useProxyStore, type ProxyEntry } from "../stores/proxy";

const { copy, copied } = useClipboard();

// Fixed row height (px) - must match the row markup below for virtualization.
const ROW_HEIGHT = 28;

// All buffer/subscription/filter state lives in the store, so it survives tab
// switches; this component is just the view over it.
const store = useProxyStore();
const {
  rows,
  selected,
  listening,
  status,
  stateLabel,
  connectedCount,
  listen,
  upstream,
  captureEnabled,
  output,
  followTail,
  dirFilter,
  connFilter,
  search,
  connections,
  filteredRows,
} = storeToRefs(store);
const { start: doStart, stop: doStop, clear } = store;

const {
  list: virtualRows,
  containerProps,
  wrapperProps,
  scrollTo,
} = useVirtualList(filteredRows, { itemHeight: ROW_HEIGHT });

// Follow-tail: stick to the newest frame while enabled, but never yank the view
// while the user is inspecting an older row (toggle off to browse freely).
watch(
  () => filteredRows.value.length,
  (len) => {
    if (followTail.value) scrollTo(len - 1);
  },
);
watch(followTail, (on) => {
  if (on) scrollTo(filteredRows.value.length - 1);
});

// On (re)mount, jump to the tail so a tab switch resumes at the latest frame.
onMounted(() => {
  if (followTail.value) scrollTo(filteredRows.value.length - 1);
});

function pretty(rec: ProxyEntry): string {
  if (rec.frame !== undefined) {
    try {
      return JSON.stringify(rec.frame, null, 2);
    } catch {
      /* fall through to raw */
    }
  }
  return rec.raw ?? "";
}

const selectedHtml = computed(() => (selected.value ? highlightJson(pretty(selected.value)) : ""));

function badgeClass(dir: ProxyDir): string {
  // c2s = client→server (like a sent command); s2c = server→client (a reply).
  return dir === "c2s"
    ? "bg-sky-500/15 text-sky-300 border-sky-500/30"
    : "bg-emerald-500/15 text-emerald-300 border-emerald-500/30";
}

function dirLabel(dir: ProxyDir): string {
  return dir === "c2s" ? "C→S" : "S→C";
}
</script>

<template>
  <div class="flex h-full flex-col gap-3">
    <!-- proxy config -->
    <div class="flex flex-wrap items-end gap-3 rounded border border-zinc-800 bg-zinc-900/50 p-3">
      <label class="flex flex-col gap-1 text-xs text-zinc-400">
        Listen
        <input
          v-model="listen"
          :disabled="listening"
          class="w-40 rounded bg-zinc-800 px-2 py-1 text-sm text-zinc-100 outline-none disabled:opacity-50"
        />
      </label>
      <label class="flex flex-col gap-1 text-xs text-zinc-400">
        Upstream
        <input
          v-model="upstream"
          :disabled="listening"
          class="w-40 rounded bg-zinc-800 px-2 py-1 text-sm text-zinc-100 outline-none disabled:opacity-50"
        />
      </label>
      <label class="flex flex-col gap-1 text-xs text-zinc-400">
        <span class="flex items-center gap-2">
          <input v-model="captureEnabled" type="checkbox" :disabled="listening" />
          Capture to JSONL
        </span>
        <input
          v-model="output"
          :disabled="listening || !captureEnabled"
          placeholder="session.jsonl"
          class="w-56 rounded bg-zinc-800 px-2 py-1 text-sm text-zinc-100 outline-none disabled:opacity-50"
        />
      </label>

      <button
        v-if="!listening"
        class="rounded bg-emerald-600 px-4 py-1.5 text-sm font-medium text-white hover:bg-emerald-500"
        @click="doStart"
      >
        Start
      </button>
      <button
        v-else
        class="rounded bg-rose-600 px-4 py-1.5 text-sm font-medium text-white hover:bg-rose-500"
        @click="doStop"
      >
        Stop
      </button>

      <span class="ml-auto flex min-w-0 items-center gap-2 text-xs">
        <span
          class="shrink-0 font-medium"
          :class="!listening ? 'text-zinc-500' : connectedCount > 0 ? 'text-emerald-400' : 'text-amber-400'"
        >
          &#9679; {{ stateLabel }}
        </span>
        <span class="truncate text-zinc-600" :title="status">{{ status }}</span>
      </span>
    </div>

    <!-- captured frames toolbar -->
    <div class="flex flex-wrap items-center gap-3 px-1">
      <span class="text-xs text-zinc-500">
        {{ filteredRows.length }}<span v-if="filteredRows.length !== rows.length"> / {{ rows.length }}</span> frames
      </span>

      <!-- direction filter -->
      <div class="flex overflow-hidden rounded border border-zinc-800 text-[10px]">
        <button
          v-for="opt in (['all', 'c2s', 's2c'] as const)"
          :key="opt"
          class="px-2 py-0.5"
          :class="dirFilter === opt ? 'bg-zinc-700 text-zinc-100' : 'text-zinc-500 hover:text-zinc-300'"
          @click="dirFilter = opt"
        >
          {{ opt === "all" ? "All" : opt === "c2s" ? "C→S" : "S→C" }}
        </button>
      </div>

      <!-- connection filter (shown once more than one connection is seen) -->
      <select
        v-if="connections.length > 1"
        v-model="connFilter"
        class="rounded border border-zinc-800 bg-zinc-800 px-2 py-1 text-xs text-zinc-100 outline-none"
      >
        <option :value="'all'">All conns</option>
        <option v-for="c in connections" :key="c.conn_id" :value="c.conn_id">
          conn {{ c.conn_id }} · {{ c.peer }}
        </option>
      </select>

      <!-- search -->
      <input
        v-model="search"
        placeholder="Filter context / raw…"
        class="w-48 rounded bg-zinc-800 px-2 py-1 text-xs text-zinc-100 outline-none"
      />

      <label class="flex items-center gap-1.5 text-xs text-zinc-500">
        <input v-model="followTail" type="checkbox" />
        Follow tail
      </label>

      <button class="ml-auto text-xs text-zinc-500 hover:text-zinc-300" @click="clear">Clear</button>
    </div>
    <div class="flex flex-1 gap-3 overflow-hidden">
      <!-- virtualized fixed-height list -->
      <div
        v-bind="containerProps"
        class="flex-1 rounded border border-zinc-800 bg-zinc-950 font-mono text-xs"
      >
        <div v-if="rows.length === 0" class="p-4 text-center text-zinc-600">
          No frames captured yet. Point a client at <span class="text-zinc-400">{{ listen }}</span>.
        </div>
        <div v-else-if="filteredRows.length === 0" class="p-4 text-center text-zinc-600">
          No frames match the current filter.
        </div>
        <div v-bind="wrapperProps">
          <div
            v-for="{ data: entry } in virtualRows"
            :key="entry.id"
            class="flex cursor-pointer items-center gap-2 border-b border-zinc-900 px-2 hover:bg-zinc-900/50"
            :class="selected?.id === entry.id ? 'bg-zinc-800/70' : ''"
            :style="{ height: `${ROW_HEIGHT}px` }"
            @click="selected = entry"
          >
            <span class="w-10 shrink-0 text-right text-zinc-600">{{ entry.seq }}</span>
            <span
              v-if="connections.length > 1"
              class="shrink-0 rounded bg-zinc-800 px-1.5 py-0.5 text-[10px] text-zinc-400"
              :title="entry.peer"
            >
              c{{ entry.conn_id }}
            </span>
            <span
              class="shrink-0 rounded border px-1.5 py-0.5 text-[10px]"
              :class="badgeClass(entry.dir)"
            >
              {{ dirLabel(entry.dir) }}
            </span>
            <span class="truncate text-zinc-200">{{ entry.context }}</span>
            <span class="ml-auto shrink-0 text-zinc-600">{{ entry.elapsed_ms }}ms</span>
          </div>
        </div>
      </div>

      <!-- detail pane -->
      <div class="flex w-1/2 flex-col rounded border border-zinc-800 bg-zinc-950">
        <div
          v-if="!selected"
          class="flex flex-1 items-center justify-center p-4 text-center text-xs text-zinc-600"
        >
          Select a frame to inspect.
        </div>
        <template v-else>
          <div class="flex items-center gap-2 border-b border-zinc-800 px-3 py-2 text-xs">
            <span class="text-zinc-600">#{{ selected.seq }}</span>
            <span class="rounded border px-1.5 py-0.5 text-[10px]" :class="badgeClass(selected.dir)">
              {{ dirLabel(selected.dir) }}
            </span>
            <span class="text-zinc-200">{{ selected.context }}</span>
            <span class="text-zinc-600" :title="selected.peer">conn {{ selected.conn_id }}</span>
            <span v-if="selected.reply_to !== undefined" class="text-zinc-600">↩ #{{ selected.reply_to }}</span>
            <span class="text-zinc-600">{{ selected.ts }}</span>
            <button class="ml-auto text-zinc-500 hover:text-zinc-300" @click="copy(pretty(selected))">
              {{ copied ? "Copied" : "Copy" }}
            </button>
            <button class="text-zinc-500 hover:text-zinc-300" @click="selected = null">&times;</button>
          </div>
          <pre
            class="flex-1 overflow-auto whitespace-pre-wrap break-all p-3 font-mono text-xs text-zinc-400"
            v-html="selectedHtml"
          ></pre>
        </template>
      </div>
    </div>
  </div>
</template>
