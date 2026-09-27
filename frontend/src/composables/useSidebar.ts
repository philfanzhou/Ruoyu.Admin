import { onBeforeUnmount, ref } from 'vue'

const STORAGE_KEY = 'adminSidebarCollapsed'
const NARROW_QUERY = '(max-width: 1023px)'

function readPreference(): boolean | null {
  try {
    const value = localStorage.getItem(STORAGE_KEY)
    return value === null ? null : value === '1'
  } catch {
    return null
  }
}

function writePreference(collapsed: boolean) {
  try {
    localStorage.setItem(STORAGE_KEY, collapsed ? '1' : '0')
  } catch {
    // Storage unavailable (e.g. privacy mode): keep the in-memory state only.
  }
}

/**
 * Sidebar collapse state.
 * - Narrow viewports (< 1024px) always start collapsed to an icon rail;
 *   toggling there is temporary and not persisted.
 * - Wider viewports use the persisted user preference, expanded by default.
 * - Crossing the breakpoint re-applies the rule for the new width.
 */
export function useSidebar() {
  const mql = window.matchMedia(NARROW_QUERY)
  const resolve = (narrow: boolean) => (narrow ? true : readPreference() ?? false)

  const collapsed = ref(resolve(mql.matches))

  const onChange = (event: MediaQueryListEvent) => {
    collapsed.value = resolve(event.matches)
  }
  mql.addEventListener('change', onChange)
  onBeforeUnmount(() => mql.removeEventListener('change', onChange))

  const toggle = () => {
    collapsed.value = !collapsed.value
    if (!mql.matches) writePreference(collapsed.value)
  }

  return { collapsed, toggle }
}
