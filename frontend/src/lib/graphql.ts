import { GraphQLClient, gql } from 'graphql-request'

export const client = new GraphQLClient('/graphql')

export const CITY_DASHBOARD_QUERY = gql`
  query CityDashboard($city: String!, $ticker: String, $newsCount: Int) {
    weather(city: $city) {
      city
      temperature
      feelsLike
      windSpeed
      humidity
      condition
      icon
    }
    forecast(city: $city, days: 5) {
      date
      maxTemp
      minTemp
      condition
    }
    topHeadlines(query: $city, pageSize: $newsCount) {
      title
      description
      url
      source
      publishedAt
    }
    stockQuote(ticker: $ticker) {
      ticker
      companyName
      price
      change
      changePercent
      high
      low
      volume
      lastUpdated
    }
    cacheStats {
      totalKeys
      hitCount
      missCount
      hitRate
    }
  }
`

export const STOCK_QUERY = gql`
  query StockQuote($ticker: String!) {
    stockQuote(ticker: $ticker) {
      ticker
      companyName
      price
      change
      changePercent
      high
      low
      volume
      lastUpdated
    }
  }
`

export const NEWS_SEARCH_QUERY = gql`
  query SearchNews($query: String!, $pageSize: Int) {
    searchNews(query: $query, pageSize: $pageSize) {
      title
      description
      url
      source
      publishedAt
    }
  }
`

export type WeatherData = {
  city: string
  temperature: number
  feelsLike: number
  windSpeed: number
  humidity: number
  condition: string
  icon: string
}

export type ForecastDay = {
  date: string
  maxTemp: number
  minTemp: number
  condition: string
}

export type NewsArticle = {
  title: string
  description: string | null
  url: string | null
  source: string | null
  publishedAt: string | null
}

export type StockQuote = {
  ticker: string
  companyName: string
  price: number
  change: number
  changePercent: number
  high: number
  low: number
  volume: number
  lastUpdated: string
}

export type CacheStats = {
  totalKeys: number
  hitCount: number
  missCount: number
  hitRate: number
}

export type DashboardData = {
  weather: WeatherData | null
  forecast: ForecastDay[]
  topHeadlines: NewsArticle[]
  stockQuote: StockQuote | null
  cacheStats: CacheStats
}
