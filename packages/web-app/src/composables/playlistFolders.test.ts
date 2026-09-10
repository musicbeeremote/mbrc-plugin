import { describe, expect, it } from 'vitest'

import { browsePlaylists, playlistLabel, playlistSegments } from './playlistFolders'

const entry = (name: string) => ({ name, url: `C:/mb/${name}.mbp` })

// Every separator is a directory level, and MusicBee reports both kinds.
const LIBRARY = [
  entry(String.raw`tracks\subplaylst\Caraven`),
  entry(String.raw`tracks\subplaylst\Deeper`),
  entry(String.raw`tracks\Loose`),
  entry('Playlist'),
  entry('mixes/summer/Beach'),
]

describe('playlist segments', () => {
  it('splits on either separator', () => {
    expect(playlistSegments(String.raw`a\b\c`)).toStrictEqual(['a', 'b', 'c'])
    expect(playlistSegments('a/b/c')).toStrictEqual(['a', 'b', 'c'])
    expect(playlistSegments('Playlist')).toStrictEqual(['Playlist'])
  })
})

describe('browsing the playlist tree', () => {
  it('shows the top folders and the playlists at the root', () => {
    const root = browsePlaylists(LIBRARY, [])
    expect(root.folders).toStrictEqual(['tracks', 'mixes'])
    expect(root.playlists.map(playlistLabel)).toStrictEqual(['Playlist'])
  })

  // A folder holds both: the folders below it and the playlists directly in it.
  it('shows a folder that holds both a subfolder and a playlist', () => {
    const tracks = browsePlaylists(LIBRARY, ['tracks'])
    expect(tracks.folders).toStrictEqual(['subplaylst'])
    expect(tracks.playlists.map(playlistLabel)).toStrictEqual(['Loose'])
  })

  it('reaches the playlists at the bottom of a nested folder', () => {
    const nested = browsePlaylists(LIBRARY, ['tracks', 'subplaylst'])
    expect(nested.folders).toStrictEqual([])
    expect(nested.playlists.map(playlistLabel)).toStrictEqual(['Caraven', 'Deeper'])
  })

  // A folder appears once however many playlists sit under it.
  it('names a folder once no matter how many playlists it holds', () => {
    const many = browsePlaylists(
      [entry(String.raw`a\1`), entry(String.raw`a\2`), entry(String.raw`a\3`)],
      [],
    )
    expect(many.folders).toStrictEqual(['a'])
  })

  it('finds nothing in a folder that does not exist', () => {
    expect(browsePlaylists(LIBRARY, ['nope'])).toStrictEqual({ folders: [], playlists: [] })
  })
})
