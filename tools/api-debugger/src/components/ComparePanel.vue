<script setup lang="ts">
import { computed, onMounted, ref } from "vue";
import { storeToRefs } from "pinia";
import { useSessionsStore } from "../stores/sessions";
import type { SchemaDiffKind } from "../lib/diff";

const store = useSessionsStore();
const { sessions, compareA, compareB, diff, diffError } = storeToRefs(store);
const { refresh, runCompare } = store;

// Local view filter: narrow the result lists to endpoints matching a context.
const filter = ref("");

const filtered = computed(() => {
  const d = diff.value;
  if (!d) return { changed: [], onlyA: [], onlyB: [] };
  const q = filter.value.trim().toLowerCase();
  if (!q) return d;
  const match = (context: string) => context.toLowerCase().includes(q);
  return {
    changed: d.changed.filter((c) => match(c.context)),
    onlyA: d.onlyA.filter((e) => match(e.context)),
    onlyB: d.onlyB.filter((e) => match(e.context)),
  };
});

function kindClass(kind: SchemaDiffKind): string {
  switch (kind) {
    case "missing":
      return "text-rose-400";
    case "extra":
      return "text-amber-400";
    default:
      return "text-sky-400";
  }
}

function kindLabel(kind: SchemaDiffKind): string {
  // Framed relative to A (baseline): a field gone in B, new in B, or retyped.
  switch (kind) {
    case "missing":
      return "removed in B";
    case "extra":
      return "added in B";
    default:
      return "type";
  }
}

function badgeClass(dir: string): string {
  return dir === "c2s"
    ? "bg-sky-500/15 text-sky-300 border-sky-500/30"
    : "bg-emerald-500/15 text-emerald-300 border-emerald-500/30";
}

function dirLabel(dir: string): string {
  return dir === "c2s" ? "C→S" : dir === "s2c" ? "S→C" : dir;
}

onMounted(() => {
  if (sessions.value.length === 0) refresh();
});
</script>

<template>
  <div class="flex h-full flex-col gap-3">
    <!-- picker -->
    <div class="flex flex-wrap items-center gap-3 rounded border border-zinc-800 bg-zinc-900/50 p-3">
      <label class="flex items-center gap-2 text-xs text-zinc-400">
        A
        <select
          v-model="compareA"
          class="w-56 rounded border border-zinc-800 bg-zinc-800 px-2 py-1.5 text-xs text-zinc-100 outline-none"
        >
          <option :value="null" disabled>Select a session…</option>
          <option v-for="s in sessions" :key="s.path" :value="s.path">{{ s.name }} ({{ s.frames }})</option>
        </select>
      </label>
      <span class="text-zinc-600">vs</span>
      <label class="flex items-center gap-2 text-xs text-zinc-400">
        B
        <select
          v-model="compareB"
          class="w-56 rounded border border-zinc-800 bg-zinc-800 px-2 py-1.5 text-xs text-zinc-100 outline-none"
        >
          <option :value="null" disabled>Select a session…</option>
          <option v-for="s in sessions" :key="s.path" :value="s.path">{{ s.name }} ({{ s.frames }})</option>
        </select>
      </label>

      <button
        class="rounded bg-sky-600 px-4 py-1.5 text-xs font-medium text-white hover:bg-sky-500 disabled:opacity-40"
        :disabled="!compareA || !compareB"
        @click="runCompare"
      >
        Compare
      </button>

      <button
        class="rounded bg-zinc-800 px-3 py-1.5 text-xs text-zinc-200 hover:bg-zinc-700"
        @click="refresh"
      >
        Refresh
      </button>

      <input
        v-if="diff"
        v-model="filter"
        placeholder="filter by context…"
        class="w-48 rounded bg-zinc-800 px-2 py-1.5 text-xs text-zinc-100 outline-none"
      />

      <span v-if="diffError" class="ml-auto text-xs text-rose-400">{{ diffError }}</span>
    </div>

    <!-- result -->
    <div v-if="!diff" class="flex flex-1 items-center justify-center text-sm text-zinc-500">
      Pick two sessions and Compare. Endpoints are matched by (direction, context) and their
      schemas diffed - ordering, seq and counts are ignored.
    </div>

    <template v-else>
      <!-- summary -->
      <div class="flex flex-wrap gap-4 rounded border border-zinc-800 bg-zinc-900/50 px-4 py-2 text-xs">
        <span class="text-zinc-400">
          <span class="font-medium text-zinc-100">{{ diff.matched }}</span> endpoints matched
        </span>
        <span :class="diff.changed.length ? 'text-sky-400' : 'text-zinc-500'">
          <span class="font-medium">{{ diff.changed.length }}</span> schema-changed
        </span>
        <span :class="diff.onlyA.length ? 'text-rose-400' : 'text-zinc-500'">
          <span class="font-medium">{{ diff.onlyA.length }}</span> only in A
        </span>
        <span :class="diff.onlyB.length ? 'text-amber-400' : 'text-zinc-500'">
          <span class="font-medium">{{ diff.onlyB.length }}</span> only in B
        </span>
        <span
          v-if="!diff.changed.length && !diff.onlyA.length && !diff.onlyB.length"
          class="ml-auto font-medium text-emerald-400"
        >
          ● Schemas match
        </span>
      </div>

      <div class="flex-1 overflow-auto rounded border border-zinc-800 bg-zinc-950 p-3 text-xs">
        <!-- schema-changed endpoints -->
        <section v-if="filtered.changed.length" class="mb-4">
          <h3 class="mb-2 text-[11px] font-semibold uppercase tracking-wide text-zinc-500">Schema changed</h3>
          <div
            v-for="c in filtered.changed"
            :key="c.key"
            class="mb-2 rounded border border-zinc-800 bg-zinc-900/40 p-2"
          >
            <div class="mb-1 flex items-center gap-2">
              <span class="rounded border px-1.5 py-0.5 text-[10px]" :class="badgeClass(c.dir)">{{ dirLabel(c.dir) }}</span>
              <span class="text-zinc-200">{{ c.context }}</span>
              <span class="text-[10px] text-zinc-600">A×{{ c.countA }} · B×{{ c.countB }}</span>
            </div>
            <ul class="ml-1 space-y-0.5 font-mono">
              <li v-for="(m, i) in c.fields" :key="i" class="flex flex-wrap items-baseline gap-2">
                <span class="text-zinc-400">{{ m.path }}</span>
                <span class="text-[10px] uppercase" :class="kindClass(m.kind)">{{ kindLabel(m.kind) }}</span>
                <span class="text-zinc-600">
                  <template v-if="m.a !== undefined">A: {{ m.a }}</template>
                  <template v-if="m.a !== undefined && m.b !== undefined"> → </template>
                  <template v-if="m.b !== undefined">B: {{ m.b }}</template>
                </span>
              </li>
            </ul>
          </div>
        </section>

        <!-- endpoints only on one side -->
        <section v-if="filtered.onlyA.length" class="mb-4">
          <h3 class="mb-2 text-[11px] font-semibold uppercase tracking-wide text-rose-400">Endpoints only in A</h3>
          <div v-for="e in filtered.onlyA" :key="e.key" class="flex items-center gap-2 py-0.5">
            <span class="rounded border px-1.5 py-0.5 text-[10px]" :class="badgeClass(e.dir)">{{ dirLabel(e.dir) }}</span>
            <span class="text-zinc-300">{{ e.context }}</span>
            <span class="text-[10px] text-zinc-600">×{{ e.count }}</span>
          </div>
        </section>

        <section v-if="filtered.onlyB.length">
          <h3 class="mb-2 text-[11px] font-semibold uppercase tracking-wide text-amber-400">Endpoints only in B</h3>
          <div v-for="e in filtered.onlyB" :key="e.key" class="flex items-center gap-2 py-0.5">
            <span class="rounded border px-1.5 py-0.5 text-[10px]" :class="badgeClass(e.dir)">{{ dirLabel(e.dir) }}</span>
            <span class="text-zinc-300">{{ e.context }}</span>
            <span class="text-[10px] text-zinc-600">×{{ e.count }}</span>
          </div>
        </section>
      </div>
    </template>
  </div>
</template>
