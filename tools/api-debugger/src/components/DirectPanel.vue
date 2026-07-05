<script setup lang="ts">
import { computed, onMounted, watch } from "vue";
import { storeToRefs } from "pinia";
import { useClipboard, useVirtualList } from "@vueuse/core";
import type { Direction } from "../lib/api";
import { highlightJson } from "../lib/jsonHighlight";
import { COMMAND_CATALOG, findCommand } from "../lib/commands";
import { useDirectStore, type Channel } from "../stores/direct";
import PlayerPane from "./PlayerPane.vue";

const { copy, copied } = useClipboard();

// Fixed row height (px) - must match the row markup below for virtualization.
const ROW_HEIGHT = 28;

const store = useDirectStore();
const {
  filteredLog,
  log,
  selected,
  primaryConnected,
  primaryStatus,
  secondaryConnected,
  secondaryStatus,
  host,
  port,
  clientType,
  protocolVersion,
  noBroadcast,
  command,
  sendTarget,
  channelFilter,
  discovered,
  discovering,
} = storeToRefs(store);
const { connect, disconnect, send: doSend, clear, discover, useDiscovered } = store;

// Keep the send target valid: if the secondary drops, fall back to primary.
watch(secondaryConnected, (on) => {
  if (!on && sendTarget.value === "secondary") sendTarget.value = "primary";
});

const target = computed(() => `${host.value}:${port.value}`);

const {
  list: virtualRows,
  containerProps,
  wrapperProps,
  scrollTo,
} = useVirtualList(filteredLog, { itemHeight: ROW_HEIGHT });

watch(() => filteredLog.value.length, (len) => scrollTo(len - 1));
onMounted(() => scrollTo(filteredLog.value.length - 1));

/** Drop a catalog command into the composer, then reset the picker. */
function pickCommand(e: Event) {
  const sel = e.target as HTMLSelectElement;
  if (sel.value) {
    command.value = sel.value;
    sel.value = "";
  }
}

function pretty(raw: string): string {
  try {
    return JSON.stringify(JSON.parse(raw), null, 2);
  } catch {
    return raw;
  }
}

const selectedHtml = computed(() => (selected.value ? highlightJson(pretty(selected.value.raw)) : ""));

function badgeClass(d: Direction): string {
  switch (d) {
    case "sent":
      return "bg-sky-500/15 text-sky-300 border-sky-500/30";
    case "received":
      return "bg-emerald-500/15 text-emerald-300 border-emerald-500/30";
    case "error":
      return "bg-rose-500/15 text-rose-300 border-rose-500/30";
    default:
      return "bg-zinc-500/15 text-zinc-300 border-zinc-500/30";
  }
}

function channelBadge(ch: Channel): string {
  return ch === "primary"
    ? "bg-indigo-500/15 text-indigo-300 border-indigo-500/30"
    : "bg-fuchsia-500/15 text-fuchsia-300 border-fuchsia-500/30";
}
const channelLabel = (ch: Channel): string => (ch === "primary" ? "P" : "S");

const canSend = computed(() =>
  sendTarget.value === "primary" ? primaryConnected.value : secondaryConnected.value,
);

// The catalog entry matching what's currently in the composer (for the format
// hint + Android-usage badge).
const currentCmd = computed(() => findCommand(command.value));
</script>

<template>
  <div class="flex flex-col gap-3 h-full">
    <!-- connection area -->
    <div class="flex flex-col gap-2 rounded border border-zinc-800 bg-zinc-900/50 p-3">
      <!-- primary form -->
      <div class="flex flex-wrap items-end gap-3">
        <label class="flex flex-col gap-1 text-xs text-zinc-400">
          Host
          <input
            v-model="host"
            :disabled="primaryConnected"
            class="w-36 rounded bg-zinc-800 px-2 py-1 text-sm text-zinc-100 outline-none disabled:opacity-50"
          />
        </label>
        <label class="flex flex-col gap-1 text-xs text-zinc-400">
          Port
          <input
            v-model.number="port"
            type="number"
            :disabled="primaryConnected"
            class="w-24 rounded bg-zinc-800 px-2 py-1 text-sm text-zinc-100 outline-none disabled:opacity-50"
          />
        </label>
        <label class="flex flex-col gap-1 text-xs text-zinc-400">
          Client
          <input
            v-model="clientType"
            :disabled="primaryConnected"
            class="w-28 rounded bg-zinc-800 px-2 py-1 text-sm text-zinc-100 outline-none disabled:opacity-50"
          />
        </label>
        <label class="flex flex-col gap-1 text-xs text-zinc-400">
          Protocol
          <select
            v-model.number="protocolVersion"
            :disabled="primaryConnected"
            class="rounded bg-zinc-800 px-2 py-1 text-sm text-zinc-100 outline-none disabled:opacity-50"
          >
            <option :value="2">2</option>
            <option :value="3">3</option>
            <option :value="4">4</option>
          </select>
        </label>
        <label class="flex items-center gap-2 text-xs text-zinc-400">
          <input v-model="noBroadcast" type="checkbox" :disabled="primaryConnected" />
          no_broadcast
        </label>

        <button
          v-if="!primaryConnected"
          class="rounded bg-emerald-600 px-4 py-1.5 text-sm font-medium text-white hover:bg-emerald-500"
          @click="connect('primary')"
        >
          Connect
        </button>
        <button
          v-else
          class="rounded bg-rose-600 px-4 py-1.5 text-sm font-medium text-white hover:bg-rose-500"
          @click="disconnect('primary')"
        >
          Disconnect
        </button>

        <button
          class="rounded bg-zinc-800 px-3 py-1.5 text-sm text-zinc-200 hover:bg-zinc-700 disabled:opacity-40"
          :disabled="primaryConnected || discovering"
          @click="discover"
        >
          {{ discovering ? "Discovering…" : "Discover" }}
        </button>

        <span class="ml-auto flex items-center gap-1 text-xs" :class="primaryConnected ? 'text-indigo-300' : 'text-zinc-500'">
          <span class="rounded border px-1 text-[10px]" :class="channelBadge('primary')">P</span>
          {{ primaryStatus }}
        </span>
      </div>

      <!-- discovered instances -->
      <div v-if="discovered.length" class="flex flex-wrap items-center gap-2 border-t border-zinc-800 pt-2">
        <span class="text-[11px] text-zinc-500">Found:</span>
        <button
          v-for="d in discovered"
          :key="`${d.address}:${d.port}`"
          class="rounded border border-zinc-700 bg-zinc-800 px-2 py-1 text-[11px] text-zinc-200 hover:border-sky-500/50 hover:bg-zinc-700 disabled:opacity-40"
          :disabled="primaryConnected"
          :title="`${d.address}:${d.port}`"
          @click="useDiscovered(d)"
        >
          {{ d.name }} · {{ d.address }}:{{ d.port }}
        </button>
      </div>

      <!-- secondary connection (same target, no_broadcast) -->
      <div class="flex items-center gap-3 border-t border-zinc-800 pt-2 text-xs">
        <span class="rounded border px-1 text-[10px]" :class="channelBadge('secondary')">S</span>
        <span class="text-zinc-400">
          Secondary → <span class="text-zinc-200">{{ target }}</span>
          <span class="ml-1 rounded bg-zinc-800 px-1.5 py-0.5 text-[10px] text-zinc-400">no_broadcast</span>
        </span>
        <button
          v-if="!secondaryConnected"
          class="rounded bg-emerald-600/80 px-3 py-1 text-xs font-medium text-white hover:bg-emerald-500"
          @click="connect('secondary')"
        >
          Connect
        </button>
        <button
          v-else
          class="rounded bg-rose-600/80 px-3 py-1 text-xs font-medium text-white hover:bg-rose-500"
          @click="disconnect('secondary')"
        >
          Disconnect
        </button>
        <span class="ml-auto" :class="secondaryConnected ? 'text-fuchsia-300' : 'text-zinc-500'">
          &#9679; {{ secondaryStatus }}
        </span>
      </div>
    </div>

    <!-- player pane (left) + debugging column (right) -->
    <div class="flex flex-1 gap-3 overflow-hidden">
      <PlayerPane />

      <div class="flex min-w-0 flex-1 flex-col gap-3 overflow-hidden">
    <!-- command sender -->
    <div class="flex flex-col gap-2 rounded border border-zinc-800 bg-zinc-900/50 p-3">
      <div class="flex gap-2">
        <!-- send target -->
        <div class="flex overflow-hidden rounded border border-zinc-800 text-[10px]">
          <button
            v-for="ch in (['primary', 'secondary'] as const)"
            :key="ch"
            class="px-2"
            :class="[
              sendTarget === ch ? 'bg-zinc-700 text-zinc-100' : 'text-zinc-500 hover:text-zinc-300',
              ch === 'secondary' && !secondaryConnected ? 'cursor-not-allowed opacity-40' : '',
            ]"
            :disabled="ch === 'secondary' && !secondaryConnected"
            @click="sendTarget = ch"
          >
            {{ channelLabel(ch) }}
          </button>
        </div>
        <input
          v-model="command"
          spellcheck="false"
          class="flex-1 rounded bg-zinc-800 px-2 py-1.5 font-mono text-xs text-zinc-100 outline-none"
          @keyup.enter="doSend"
        />
        <button
          :disabled="!canSend"
          class="rounded bg-sky-600 px-4 py-1.5 text-sm font-medium text-white hover:bg-sky-500 disabled:opacity-40"
          @click="doSend"
        >
          Send
        </button>
      </div>
      <div class="flex flex-wrap items-center gap-2">
        <select
          class="rounded border border-zinc-800 bg-zinc-800 px-2 py-1 text-[11px] text-zinc-300 outline-none"
          @change="pickCommand"
        >
          <option value="">Insert command…</option>
          <optgroup v-for="g in COMMAND_CATALOG" :key="g.name" :label="g.name">
            <option v-for="cmd in g.commands" :key="cmd.context" :value="cmd.template">
              {{ cmd.android ? "🟢" : "" }}{{ cmd.ios ? "🔵" : "" }} {{ cmd.context }} - {{ cmd.label }}
            </option>
          </optgroup>
        </select>

        <!-- usage badges + format hint for the composed command -->
        <span
          v-if="currentCmd?.android"
          class="rounded border border-emerald-500/40 bg-emerald-500/15 px-1 text-[10px] font-semibold text-emerald-300"
          title="Actively used by the Android 1.6.1 client"
        >
          A
        </span>
        <span
          v-if="currentCmd?.ios"
          class="rounded border border-sky-500/40 bg-sky-500/15 px-1 text-[10px] font-semibold text-sky-300"
          title="Used by the third-party iOS app (from the captures)"
        >
          i
        </span>
        <span v-if="currentCmd" class="text-[11px] text-zinc-500">
          <span class="text-zinc-400">{{ currentCmd.context }}</span> data: {{ currentCmd.hint }}
        </span>

        <span class="ml-auto text-[11px] text-zinc-600">sends over channel {{ channelLabel(sendTarget) }}</span>
      </div>
    </div>

    <!-- message log toolbar -->
    <div class="flex flex-wrap items-center gap-3 px-1">
      <span class="text-xs text-zinc-500">
        {{ filteredLog.length }}<span v-if="filteredLog.length !== log.length"> / {{ log.length }}</span> messages
      </span>
      <div class="flex overflow-hidden rounded border border-zinc-800 text-[10px]">
        <button
          v-for="opt in (['all', 'primary', 'secondary'] as const)"
          :key="opt"
          class="px-2 py-0.5"
          :class="channelFilter === opt ? 'bg-zinc-700 text-zinc-100' : 'text-zinc-500 hover:text-zinc-300'"
          @click="channelFilter = opt"
        >
          {{ opt === "all" ? "All" : opt === "primary" ? "Primary" : "Secondary" }}
        </button>
      </div>
      <button class="ml-auto text-xs text-zinc-500 hover:text-zinc-300" @click="clear">Clear</button>
    </div>

    <div class="flex flex-1 gap-3 overflow-hidden">
      <!-- virtualized fixed-height list -->
      <div
        v-bind="containerProps"
        class="flex-1 rounded border border-zinc-800 bg-zinc-950 font-mono text-xs"
      >
        <div v-if="log.length === 0" class="p-4 text-center text-zinc-600">No messages yet.</div>
        <div v-else-if="filteredLog.length === 0" class="p-4 text-center text-zinc-600">
          No messages on this channel.
        </div>
        <div v-bind="wrapperProps">
          <div
            v-for="{ index, data: entry } in virtualRows"
            :key="entry.id"
            class="flex cursor-pointer items-center gap-2 border-b border-zinc-900 px-2 hover:bg-zinc-900/50"
            :class="selected?.id === entry.id ? 'bg-zinc-800/70' : ''"
            :style="{ height: `${ROW_HEIGHT}px` }"
            @click="selected = entry"
            :data-index="index"
          >
            <span class="text-zinc-600">{{ entry.time }}</span>
            <span class="rounded border px-1 py-0.5 text-[10px]" :class="channelBadge(entry.channel)">
              {{ channelLabel(entry.channel) }}
            </span>
            <span class="rounded border px-1.5 py-0.5 text-[10px] uppercase" :class="badgeClass(entry.direction)">
              {{ entry.direction }}
            </span>
            <span class="truncate text-zinc-200">{{ entry.context }}</span>
          </div>
        </div>
      </div>

      <!-- detail pane -->
      <div class="flex w-1/2 flex-col rounded border border-zinc-800 bg-zinc-950">
        <div
          v-if="!selected"
          class="flex flex-1 items-center justify-center p-4 text-center text-xs text-zinc-600"
        >
          Select a message to inspect.
        </div>
        <template v-else>
          <div class="flex items-center gap-2 border-b border-zinc-800 px-3 py-2 text-xs">
            <span class="text-zinc-600">{{ selected.time }}</span>
            <span class="rounded border px-1 py-0.5 text-[10px]" :class="channelBadge(selected.channel)">
              {{ channelLabel(selected.channel) }}
            </span>
            <span class="rounded border px-1.5 py-0.5 text-[10px] uppercase" :class="badgeClass(selected.direction)">
              {{ selected.direction }}
            </span>
            <span class="text-zinc-200">{{ selected.context }}</span>
            <button class="ml-auto text-zinc-500 hover:text-zinc-300" @click="copy(pretty(selected.raw))">
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
    </div>
  </div>
</template>
