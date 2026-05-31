import type { NewsArticle } from '../lib/graphql'

type Props = { articles: NewsArticle[]; loading: boolean; city: string }

function timeAgo(iso: string | null) {
  if (!iso) return ''
  const diff = Date.now() - new Date(iso).getTime()
  const h = Math.floor(diff / 3_600_000)
  if (h < 1) return `${Math.floor(diff / 60_000)}m ago`
  if (h < 24) return `${h}h ago`
  return `${Math.floor(h / 24)}d ago`
}

export default function NewsCard({ articles, loading, city }: Props) {
  return (
    <div className="card news-card">
      <div className="card-label">Top stories — {city}</div>

      {loading ? (
        Array.from({ length: 5 }).map((_, i) => (
          <div key={i} className="news-skeleton" />
        ))
      ) : articles.length === 0 ? (
        <p className="empty">No articles found</p>
      ) : (
        <div className="news-list">
          {articles.map((a, i) => (
            <a
              key={i}
              href={a.url ?? '#'}
              target="_blank"
              rel="noopener noreferrer"
              className="news-item"
            >
              <div className="news-meta">
                {a.source && <span className="news-source">{a.source}</span>}
                {a.publishedAt && <span className="news-time">{timeAgo(a.publishedAt)}</span>}
              </div>
              <div className="news-title">{a.title}</div>
              {a.description && <div className="news-desc">{a.description}</div>}
            </a>
          ))}
        </div>
      )}
    </div>
  )
}
