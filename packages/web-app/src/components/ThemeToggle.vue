<script setup lang="ts">
/**
 * Light, dark, or whatever the system says, cycled by one button.
 *
 * It belongs to the app rather than to any view, so it lives where the app's
 * own furniture is: the foot of the nav rail where there is one, the end of the
 * tab bar where there is not.
 */
import IconThemeAuto from '~icons/lucide/monitor'
import IconThemeDark from '~icons/lucide/moon'
import IconThemeLight from '~icons/lucide/sun'

import { Theme, useTheme } from '../composables/useTheme'

const { variant } = defineProps<{ variant: 'rail' | 'bar' }>()

const { theme, cycle } = useTheme()

const ICON = {
  [Theme.Auto]: IconThemeAuto,
  [Theme.Light]: IconThemeLight,
  [Theme.Dark]: IconThemeDark,
}
</script>

<template>
  <button
    class="text-outline transition-colors hover:text-ink"
    :class="
      variant === 'rail'
        ? 'mt-auto flex items-center gap-3 rounded-control px-3 py-2 text-left text-sm'
        : 'flex w-16 shrink-0 flex-col items-center gap-1 py-2 text-2xs'
    "
    :aria-label="$t(`common.theme.action.${theme}`)"
    :title="$t(`common.theme.action.${theme}`)"
    @click="cycle"
  >
    <component :is="ICON[theme]" class="size-5 shrink-0" />
    {{ $t(`common.theme.label.${theme}`) }}
  </button>
</template>
