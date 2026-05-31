import { BarChart, Bar, XAxis, YAxis, Tooltip, ResponsiveContainer, Cell } from 'recharts'
import type { ForecastDay } from '../lib/graphql'

type Props = { days: ForecastDay[]; loading: boolean }

function shortDate(iso: string) {
  const d = new Date(iso)
  return d.toLocaleDateString('en-US', { weekday: 'short' })
}

export default function ForecastCard({ days, loading }: Props) {
  if (loading) return <div className="card skeleton" style={{ height: 160 }} />
  if (!days.length) return null

  const data = days.map(d => ({
    day: shortDate(d.date),
    max: Math.round(d.maxTemp),
    min: Math.round(d.minTemp),
    condition: d.condition,
  }))

  return (
    <div className="card forecast-card">
      <div className="card-label">5-day forecast</div>
      <ResponsiveContainer width="100%" height={100}>
        <BarChart data={data} barSize={18} barGap={4}>
          <XAxis dataKey="day" tick={{ fontSize: 11, fill: 'var(--text-muted)' }} axisLine={false} tickLine={false} />
          <YAxis hide domain={['dataMin - 5', 'dataMax + 5']} />
          <Tooltip
            cursor={{ fill: 'var(--surface-2)' }}
            contentStyle={{ background: 'var(--surface)', border: '1px solid var(--border)', borderRadius: 8, fontSize: 12 }}
            formatter={(val: number) => [`${val}°C`]}
          />
          <Bar dataKey="max" radius={[4, 4, 0, 0]}>
            {data.map((_, i) => (
              <Cell key={i} fill="var(--accent)" opacity={0.85} />
            ))}
          </Bar>
          <Bar dataKey="min" radius={[4, 4, 0, 0]}>
            {data.map((_, i) => (
              <Cell key={i} fill="var(--accent-muted)" opacity={0.6} />
            ))}
          </Bar>
        </BarChart>
      </ResponsiveContainer>
      <div className="forecast-legend">
        <span><span className="dot dot-high" />High</span>
        <span><span className="dot dot-low" />Low</span>
      </div>
    </div>
  )
}
