/**
 * The URL is the app's state.
 *
 * Every place worth returning to has an address: which pane, where in the
 * library, which playlist folder. That is what makes the browser's own Back
 * button work, and what lets a drill-down survive a reload instead of dropping
 * back to the top of the library.
 *
 * History mode rather than hash: the server answers an unknown path with the
 * entry document (`web/assets.rs`), so a deep link loads the app and the router
 * takes it from there.
 */

import { createRouter, createWebHistory } from 'vue-router'
import type { RouteRecordRaw } from 'vue-router'

import LibraryView from '../views/LibraryView.vue'
import NowPlayingView from '../views/NowPlayingView.vue'
import PlaylistsView from '../views/PlaylistsView.vue'
import QueueView from '../views/QueueView.vue'
import RadioView from '../views/RadioView.vue'

import { RouteName } from './locations'

const routes: RouteRecordRaw[] = [
  { path: '/', redirect: '/playing' },
  { path: '/playing', name: RouteName.Playing, component: NowPlayingView },
  { path: '/queue', name: RouteName.Queue, component: QueueView },
  { path: '/library/:level?', name: RouteName.Library, component: LibraryView },
  { path: '/lists', name: RouteName.Playlists, component: PlaylistsView },
  { path: '/radio', name: RouteName.Radio, component: RadioView },
  { path: '/:rest(.*)*', redirect: '/playing' },
]

export const router = createRouter({ history: createWebHistory(), routes })
