<script setup lang="ts">
import { computed, onMounted, ref } from "vue";
import { storeToRefs } from "pinia";
import { useClipboard, useVirtualList } from "@vueuse/core";
import { highlightJson } from "../lib/jsonHighlight";
import { pickSessionFile } from "../lib/api";
import { useSessionsStore } from "../stores/sessions";

const ROW_HEIGHT = 28;

const { copy, copied } = useClipboard();

const store = useSessionsStore();
const { sessions, loading, error, viewerPath, viewerName, viewerFrames, viewerSelected } =
  storeToRefs(store);
const { refresh, open, remove, saveProxy, saveDirect, importPath } = store;

const saveName = ref("session");
const importSrc = ref("");

/** Native file picker → fill the path field and import immediately. */
async function browseAndImport() {
  const path = await pickSessionFile();
  if (path) {
    importSrc.value = path;
    await importPath(path);
  }
}

const {
  list: virtualFrames,
  containerProps,
  wrapperProps,
} = useVirtualList(viewerFrames, { itemHeight: ROW_HEIGHT });

const selectedText = computed(() => {
  const f = viewerSelected.value;
  if (!f) return "";
  if (f.frame !== undefined) {
    try {
      return JSON.stringify(f.frame, null, 2);
    } catch {
      /* fall through to raw */
    }
  }
  return f.raw;
});

const selectedHtml = computed(() => highlightJson(selectedText.value));

function fmtBytes(n: number): string {
  if (n < 1024) return `${n} B`;
  if (n < 1024 * 1024) return `${(n / 1024).toFixed(1)} KB`;
  return `${(n / (1024 * 1024)).toFixed(1)} MB`;
}

function fmtDate(ms: number): string {
  return ms ? new Date(ms).toLocaleString() : "-";
}

function badgeClass(dir: string): string {
  return dir === "c2s"
    ? "bg-sky-500/15 text-sky-300 border-sky-500/30"
    : "bg-emerald-500/15 text-emerald-300 border-emerald-500/30";
}

function dirLabel(dir: string): string {
  return dir === "c2s" ? "C→S" : dir === "s2c" ? "S→C" : dir;
}

onMounted(refresh);
</script>

<template>
  <div class="flex h-full flex-col gap-3">
    <!-- toolbar -->
    <div class="flex flex-wrap items-center gap-2 rounded border border-zinc-800 bg-zinc-900/50 p-3">
      <button
        class="rounded bg-zinc-800 px-3 py-1.5 text-xs text-zinc-200 hover:bg-zinc-700"
        :disabled="loading"
        @click="refresh"
      >
        {{ loading ? "Refreshing…" : "Refresh" }}
      </button>

      <span class="mx-1 h-5 w-px bg-zinc-800" />

      <input
        v-model="saveName"
        placeholder="name"
        class="w-40 rounded bg-zinc-800 px-2 py-1.5 text-xs text-zinc-100 outline-none"
      />
      <button
        class="rounded bg-sky-600 px-3 py-1.5 text-xs font-medium text-white hover:bg-sky-500"
        @click="saveProxy(saveName)"
      >
        Save Proxy buffer
      </button>
      <button
        class="rounded bg-emerald-600 px-3 py-1.5 text-xs font-medium text-white hover:bg-emerald-500"
        @click="saveDirect(saveName)"
      >
        Save Direct buffer
      </button>

      <span class="mx-1 h-5 w-px bg-zinc-800" />

      <button
        class="rounded bg-zinc-800 px-3 py-1.5 text-xs text-zinc-200 hover:bg-zinc-700"
        @click="browseAndImport"
      >
        Browse…
      </button>
      <input
        v-model="importSrc"
        placeholder="or paste path to .jsonl"
        class="w-56 rounded bg-zinc-800 px-2 py-1.5 text-xs text-zinc-100 outline-none"
      />
      <button
        class="rounded bg-zinc-800 px-3 py-1.5 text-xs text-zinc-200 hover:bg-zinc-700 disabled:opacity-40"
        :disabled="!importSrc.trim()"
        @click="importPath(importSrc)"
      >
        Import
      </button>

      <span v-if="error" class="ml-auto max-w-[40%] truncate text-xs text-rose-400" :title="error">{{ error }}</span>
    </div>

    <div class="flex flex-1 gap-3 overflow-hidden">
      <!-- session list -->
      <div class="flex w-72 shrink-0 flex-col overflow-hidden rounded border border-zinc-800 bg-zinc-950">
        <div class="border-b border-zinc-800 px-3 py-2 text-xs text-zinc-500">
          {{ sessions.length }} sessions
        </div>
        <div class="flex-1 overflow-auto">
          <div v-if="sessions.length === 0" class="p-4 text-center text-xs text-zinc-600">
            No saved sessions yet. Save a buffer or import a .jsonl.
          </div>
          <div
            v-for="s in sessions"
            :key="s.path"
            class="group flex cursor-pointer items-center gap-2 border-b border-zinc-900 px-3 py-2 hover:bg-zinc-900/50"
            :class="viewerPath === s.path ? 'bg-zinc-800/70' : ''"
            @click="open(s)"
          >
            <div class="min-w-0 flex-1">
              <div class="truncate text-xs text-zinc-200">{{ s.name }}</div>
              <div class="text-[10px] text-zinc-500">
                {{ s.frames }} frames · {{ fmtBytes(s.bytes) }} · {{ fmtDate(s.modified_ms) }}
              </div>
            </div>
            <button
              class="shrink-0 text-zinc-600 opacity-0 hover:text-rose-400 group-hover:opacity-100"
              title="Delete"
              @click.stop="remove(s)"
            >
              &times;
            </button>
          </div>
        </div>
      </div>

      <!-- viewer -->
      <div class="flex flex-1 gap-3 overflow-hidden">
        <div
          v-bind="containerProps"
          class="flex-1 rounded border border-zinc-800 bg-zinc-950 font-mono text-xs"
        >
          <div v-if="!viewerPath" class="p-4 text-center text-zinc-600">
            Select a session to view its frames.
          </div>
          <div v-else-if="viewerFrames.length === 0" class="p-4 text-center text-zinc-600">
            {{ viewerName }} has no frames.
          </div>
          <div v-bind="wrapperProps">
            <div
              v-for="{ data: f, index } in virtualFrames"
              :key="index"
              class="flex cursor-pointer items-center gap-2 border-b border-zinc-900 px-2 hover:bg-zinc-900/50"
              :class="viewerSelected === f ? 'bg-zinc-800/70' : ''"
              :style="{ height: `${ROW_HEIGHT}px` }"
              @click="viewerSelected = f"
            >
              <span class="w-10 shrink-0 text-right text-zinc-600">{{ f.seq }}</span>
              <span class="shrink-0 rounded border px-1.5 py-0.5 text-[10px]" :class="badgeClass(f.dir)">
                {{ dirLabel(f.dir) }}
              </span>
              <span class="truncate text-zinc-200">{{ f.context }}</span>
              <span class="ml-auto shrink-0 text-[10px] text-zinc-600">c{{ f.conn_id }}</span>
            </div>
          </div>
        </div>

        <!-- detail -->
        <div class="flex w-1/2 flex-col rounded border border-zinc-800 bg-zinc-950">
          <div
            v-if="!viewerSelected"
            class="flex flex-1 items-center justify-center p-4 text-center text-xs text-zinc-600"
          >
            Select a frame to inspect.
          </div>
          <template v-else>
            <div class="flex items-center gap-2 border-b border-zinc-800 px-3 py-2 text-xs">
              <span class="text-zinc-600">#{{ viewerSelected.seq }}</span>
              <span class="rounded border px-1.5 py-0.5 text-[10px]" :class="badgeClass(viewerSelected.dir)">
                {{ dirLabel(viewerSelected.dir) }}
              </span>
              <span class="text-zinc-200">{{ viewerSelected.context }}</span>
              <button class="ml-auto text-zinc-500 hover:text-zinc-300" @click="copy(selectedText)">
                {{ copied ? "Copied" : "Copy" }}
              </button>
              <button class="text-zinc-500 hover:text-zinc-300" @click="viewerSelected = null">&times;</button>
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
</template>
