import { useState } from 'react'

type Props = {
  city: string
  ticker: string
  latencyMs: number | null
  cacheHit: boolean
}

export default function QueryPanel({ city, ticker, latencyMs, cacheHit }: Props) {
  const [open, setOpen] = useState(false)

  const query = `query CityDashboard {
  weather(city: "${city}") {
    city temperature feelsLike
    windSpeed humidity condition icon
  }
  forecast(city: "${city}", days: 5) {
    date maxTemp minTemp condition
  }
  topHeadlines(query: "${city}", pageSize: 6) {
    title description url source publishedAt
  }
  stockQuote(ticker: "${ticker}") {
    ticker companyName price
    change changePercent high low volume
  }
  cacheStats {
    totalKeys hitCount missCount hitRate
  }
}`

  return (
    <div className="query-panel">
      <button className="query-toggle" onClick={() => setOpen(o => !o)}>
        <span className="gql-badge">GraphQL</span>
        <span>View the query sent to the gateway</span>
        <span className="toggle-arrow">{open ? '▲' : '▼'}</span>
      </button>

      {open && (
        <div className="query-body">
          <div className="query-meta">
            {latencyMs !== null && (
              <span className="qmeta-item">
                Response: <strong>{latencyMs}ms</strong>
              </span>
            )}
            <span className={`qmeta-item ${cacheHit ? 'hit' : 'miss'}`}>
              {cacheHit ? '⚡ Served from cache' : '↻ Fetched from upstream APIs'}
            </span>
            <span className="qmeta-item">
              Endpoint: <code>POST /graphql</code>
            </span>
          </div>
          <pre className="query-code"><code>{query}</code></pre>
          <p className="query-note">
            This single query fans out in parallel to Open-Meteo, NewsAPI, and Alpha Vantage — and returns everything in one round trip.
          </p>
        </div>
      )}
    </div>
  )
}
