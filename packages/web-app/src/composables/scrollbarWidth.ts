/**
 * How much width a scrollbar takes, which a heading outside the list must
 * still leave for.
 *
 * The rows sit inside a scrolling container and their headings do not, so
 * without this every column after the first is off by however wide the
 * scrollbar is - fifteen pixels on Windows, none where they overlay.
 *
 * Measured rather than assumed: it differs by platform, by browser and by the
 * reader's own settings.
 */

export function scrollbarWidth(): number {
  const probe = document.createElement('div')
  probe.style.cssText = 'position:absolute;top:-9999px;width:100px;height:100px;overflow:scroll'
  document.body.append(probe)
  const width = probe.offsetWidth - probe.clientWidth
  probe.remove()
  return width
}
