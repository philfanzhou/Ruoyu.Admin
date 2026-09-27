import { watch, type Ref } from 'vue'
import { useRoute, useRouter, type LocationQuery, type LocationQueryValue } from 'vue-router'

/**
 * Parse a single-valued integer query parameter against a whitelist.
 * Missing, repeated, non-integer or non-whitelisted values yield undefined
 * (i.e. "no filter").
 */
export function parseEnumQuery(
  value: LocationQueryValue | LocationQueryValue[] | undefined,
  allowed: readonly number[],
): number | undefined {
  if (typeof value !== 'string' || !/^\d+$/.test(value)) return undefined
  const parsed = Number(value)
  return allowed.includes(parsed) ? parsed : undefined
}

/**
 * Two-way binding between one enum filter and one URL query key.
 *
 * The caller initialises `filter` with `parseEnumQuery(route.query[key], allowed)`
 * before its first load. After that:
 * - URL -> filter: when the query key changes while the page is alive (e.g. a
 *   sidebar link without query), the parsed value is applied and
 *   `onExternalChange` runs (reset to page 1 and reload). Values equal to the
 *   current filter are ignored, so our own `syncToUrl` never triggers a reload.
 * - filter -> URL: `syncToUrl()` replaces the key with the current filter
 *   (removing it when undefined) and keeps every other query key.
 */
export function useEnumQueryFilter(
  key: string,
  allowed: readonly number[],
  filter: Ref<number | undefined>,
  onExternalChange: () => void,
) {
  const route = useRoute()
  const router = useRouter()

  watch(
    () => route.query[key],
    (raw) => {
      const parsed = parseEnumQuery(raw, allowed)
      if (parsed === filter.value) return
      filter.value = parsed
      onExternalChange()
    },
  )

  function syncToUrl() {
    const next = filter.value === undefined ? undefined : String(filter.value)
    if (route.query[key] === next || (next === undefined && !(key in route.query))) return
    const query: LocationQuery = { ...route.query }
    if (next === undefined) delete query[key]
    else query[key] = next
    router.replace({ query })
  }

  return { syncToUrl }
}
