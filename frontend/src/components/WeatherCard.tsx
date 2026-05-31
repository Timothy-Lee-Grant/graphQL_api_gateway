import type { WeatherData } from '../lib/graphql'

type Props = { data: WeatherData | null; loading: boolean }

export default function WeatherCard({ data, loading }: Props) {
  if (loading) return <div className="card skeleton" style={{ height: 180 }} />

  if (!data) return (
    <div className="card empty-card">
      <p>No weather data</p>
    </div>
  )

  return (
    <div className="card weather-card">
      <div className="weather-top">
        <div>
          <div className="city-name">{data.city}</div>
          <div className="condition-label">{data.condition}</div>
        </div>
        <div className="temp-display">
          <span className="weather-icon">{data.icon}</span>
          <span className="temp-value">{Math.round(data.temperature)}°</span>
        </div>
      </div>

      <div className="weather-stats">
        <div className="stat">
          <span className="stat-label">Feels like</span>
          <span className="stat-value">{Math.round(data.feelsLike)}°C</span>
        </div>
        <div className="stat">
          <span className="stat-label">Wind</span>
          <span className="stat-value">{Math.round(data.windSpeed)} mph</span>
        </div>
        <div className="stat">
          <span className="stat-label">Humidity</span>
          <span className="stat-value">{data.humidity}%</span>
        </div>
      </div>
    </div>
  )
}
