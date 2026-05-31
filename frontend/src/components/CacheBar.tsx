import type { CacheStats } from '../lib/graphql'

type Props = {
  latencyMs: number
  cacheHit: boolean
  stats: CacheStats | null
}

export default function CacheBar({ latencyMs, cacheHit, stats }: Props) {
  const hitRate = stats?.hitRate ?? 0

  return (
    <div className="cache-bar">
      <div className="cache-bar-inner">
        <div className="perf-pill">
          <span className="perf-dot" style={{ background: latencyMs < 200 ? '#22c55e' : latencyMs < 800 ? '#f59e0b' : '#ef4444' }} />
          <span className="perf-label">{latencyMs}ms</span>
        </div>

        <div className={`cache-pill ${cacheHit ? 'hit' : 'miss'}`}>
          {cacheHit ? '⚡ Cache hit' : '↻ Cache miss'}
        </div>

        {stats && (
          <>
            <div className="cache-stat">
              <span>Hits</span>
              <strong>{stats.hitCount}</strong>
            </div>
            <div className="cache-stat">
              <span>Misses</span>
              <strong>{stats.missCount}</strong>
            </div>
            <div className="cache-stat">
              <span>Hit rate</span>
              <strong style={{ color: hitRate > 60 ? '#22c55e' : hitRate > 30 ? '#f59e0b' : 'var(--text)' }}>
                {hitRate.toFixed(0)}%
              </strong>
            </div>
          </>
        )}

        <div className="bar-hint">
          Tip: query the same city twice to see cache acceleration
        </div>
      </div>
    </div>
  )
}
