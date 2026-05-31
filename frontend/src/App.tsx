import { useState, useEffect } from 'react'
import { useDashboard } from './hooks/useDashboard'
import WeatherCard from './components/WeatherCard'
import ForecastCard from './components/ForecastCard'
import NewsCard from './components/NewsCard'
import StockCard from './components/StockCard'
import QueryPanel from './components/QueryPanel'
import CacheBar from './components/CacheBar'
import './index.css'

const PRESETS = [
  { city: 'New York', ticker: 'AAPL', label: 'New York' },
  { city: 'London', ticker: 'MSFT', label: 'London' },
  { city: 'Tokyo', ticker: 'NVDA', label: 'Tokyo' },
  { city: 'Sydney', ticker: 'TSLA', label: 'Sydney' },
]

export default function App() {
  const [city, setCity] = useState('New York')
  const [ticker, setTicker] = useState('AAPL')
  const [inputCity, setInputCity] = useState('New York')
  const [inputTicker, setInputTicker] = useState('AAPL')
  const { data, loading, error, latencyMs, cacheHit, fetch } = useDashboard()

  useEffect(() => {
    fetch(city, ticker)
  }, [])

  const handleSearch = () => {
    const c = inputCity.trim() || 'New York'
    const t = inputTicker.trim() || 'AAPL'
    setCity(c)
    setTicker(t)
    fetch(c, t)
  }

  const handlePreset = (p: typeof PRESETS[0]) => {
    setInputCity(p.city)
    setInputTicker(p.ticker)
    setCity(p.city)
    setTicker(p.ticker)
    fetch(p.city, p.ticker)
  }

  return (
    <div className="app">
      <header className="header">
        <div className="header-inner">
          <div className="header-title">
            <span className="logo-badge">GQL</span>
            <div>
              <h1>GraphQL Gateway</h1>
              <p className="header-sub">Weather · News · Finance — unified in one query</p>
            </div>
          </div>

          <div className="search-bar">
            <div className="search-inputs">
              <div className="input-wrap">
                <label>City</label>
                <input
                  value={inputCity}
                  onChange={e => setInputCity(e.target.value)}
                  onKeyDown={e => e.key === 'Enter' && handleSearch()}
                  placeholder="e.g. Tokyo"
                />
              </div>
              <div className="input-wrap">
                <label>Ticker</label>
                <input
                  value={inputTicker}
                  onChange={e => setInputTicker(e.target.value.toUpperCase())}
                  onKeyDown={e => e.key === 'Enter' && handleSearch()}
                  placeholder="e.g. MSFT"
                  className="ticker-input"
                />
              </div>
              <button className="search-btn" onClick={handleSearch} disabled={loading}>
                {loading ? <span className="spinner" /> : 'Query'}
              </button>
            </div>

            <div className="presets">
              {PRESETS.map(p => (
                <button
                  key={p.label}
                  className={`preset-btn ${city === p.city ? 'active' : ''}`}
                  onClick={() => handlePreset(p)}
                >
                  {p.label}
                </button>
              ))}
            </div>
          </div>
        </div>
      </header>

      {latencyMs !== null && (
        <CacheBar
          latencyMs={latencyMs}
          cacheHit={cacheHit}
          stats={data?.cacheStats ?? null}
        />
      )}

      {error && (
        <div className="error-banner">
          <strong>Request failed:</strong> {error}
          <span className="error-hint">Make sure the .NET gateway is running on port 5000</span>
        </div>
      )}

      <main className="main-grid">
        <div className="col-left">
          <WeatherCard data={data?.weather ?? null} loading={loading} />
          <ForecastCard days={data?.forecast ?? []} loading={loading} />
          <StockCard data={data?.stockQuote ?? null} loading={loading} />
        </div>
        <div className="col-right">
          <NewsCard articles={data?.topHeadlines ?? []} loading={loading} city={city} />
        </div>
      </main>

      <QueryPanel city={city} ticker={ticker} latencyMs={latencyMs} cacheHit={cacheHit} />
    </div>
  )
}
