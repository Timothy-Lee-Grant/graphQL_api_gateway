import type { StockQuote } from '../lib/graphql'

type Props = { data: StockQuote | null; loading: boolean }

function formatVolume(v: number) {
  if (v >= 1_000_000) return `${(v / 1_000_000).toFixed(1)}M`
  if (v >= 1_000) return `${(v / 1_000).toFixed(0)}K`
  return v.toString()
}

export default function StockCard({ data, loading }: Props) {
  if (loading) return <div className="card skeleton" style={{ height: 140 }} />
  if (!data) return null

  const up = data.change >= 0

  return (
    <div className="card stock-card">
      <div className="stock-top">
        <div>
          <div className="stock-ticker">{data.ticker}</div>
          <div className="stock-name">{data.companyName}</div>
        </div>
        <div className="stock-price-wrap">
          <div className="stock-price">${data.price.toFixed(2)}</div>
          <div className={`stock-change ${up ? 'up' : 'down'}`}>
            {up ? '▲' : '▼'} {Math.abs(data.change).toFixed(2)} ({Math.abs(data.changePercent).toFixed(2)}%)
          </div>
        </div>
      </div>

      <div className="stock-stats">
        <div className="stat">
          <span className="stat-label">High</span>
          <span className="stat-value">${data.high.toFixed(2)}</span>
        </div>
        <div className="stat">
          <span className="stat-label">Low</span>
          <span className="stat-value">${data.low.toFixed(2)}</span>
        </div>
        <div className="stat">
          <span className="stat-label">Volume</span>
          <span className="stat-value">{formatVolume(data.volume)}</span>
        </div>
      </div>
    </div>
  )
}
