import { useState, useCallback } from 'react'
import { client, CITY_DASHBOARD_QUERY, type DashboardData } from '../lib/graphql'

type QueryState = {
  data: DashboardData | null
  loading: boolean
  error: string | null
  latencyMs: number | null
  cacheHit: boolean
}

export function useDashboard() {
  const [state, setState] = useState<QueryState>({
    data: null,
    loading: false,
    error: null,
    latencyMs: null,
    cacheHit: false,
  })

  const [prevHitCount, setPrevHitCount] = useState(0)

  const fetch = useCallback(async (city: string, ticker?: string) => {
    setState(s => ({ ...s, loading: true, error: null }))
    const start = performance.now()

    try {
      const data = await client.request<DashboardData>(CITY_DASHBOARD_QUERY, {
        city,
        ticker: ticker || null,
        newsCount: 6,
      })

      const latencyMs = Math.round(performance.now() - start)
      const cacheHit = data.cacheStats?.hitCount > prevHitCount
      setPrevHitCount(data.cacheStats?.hitCount ?? 0)

      setState({ data, loading: false, error: null, latencyMs, cacheHit })
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'GraphQL request failed'
      setState(s => ({ ...s, loading: false, error: msg }))
    }
  }, [prevHitCount])

  return { ...state, fetch }
}
