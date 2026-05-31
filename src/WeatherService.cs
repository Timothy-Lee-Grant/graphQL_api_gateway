using System.Text.Json;
using GraphQLGateway.Types;

namespace GraphQLGateway.Services;

public interface IWeatherService
{
    Task<WeatherData?> GetCurrentAsync(string city, CancellationToken ct = default);
    Task<IEnumerable<WeatherForecastDay>> GetForecastAsync(string city, int days = 5, CancellationToken ct = default);
}

public class WeatherService(HttpClient http, ILogger<WeatherService> logger) : IWeatherService
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private async Task<(double lat, double lon, string resolvedCity)?> GeocodeAsync(string city, CancellationToken ct)
    {
        try
        {
            var url = $"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(city)}&count=1&language=en&format=json";
            var resp = await http.GetStringAsync(url, ct);
            using var doc = JsonDocument.Parse(resp);
            var results = doc.RootElement.GetProperty("results");
            if (results.GetArrayLength() == 0) return null;
            var first = results[0];
            return (
                first.GetProperty("latitude").GetDouble(),
                first.GetProperty("longitude").GetDouble(),
                first.GetProperty("name").GetString() ?? city
            );
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Geocoding failed for {City}", city);
            return null;
        }
    }

    public async Task<WeatherData?> GetCurrentAsync(string city, CancellationToken ct = default)
    {
        try
        {
            var geo = await GeocodeAsync(city, ct);
            if (geo is null) return null;
            var (lat, lon, resolvedCity) = geo.Value;

            var url = $"https://api.open-meteo.com/v1/forecast?latitude={lat}&longitude={lon}" +
                      "&current=temperature_2m,apparent_temperature,wind_speed_10m,relative_humidity_2m,weather_code" +
                      "&wind_speed_unit=mph&temperature_unit=celsius&timezone=auto";

            var resp = await http.GetStringAsync(url, ct);
            using var doc = JsonDocument.Parse(resp);
            var cur = doc.RootElement.GetProperty("current");

            var code = cur.GetProperty("weather_code").GetInt32();
            return new WeatherData(
                City: resolvedCity,
                Temperature: cur.GetProperty("temperature_2m").GetDouble(),
                FeelsLike: cur.GetProperty("apparent_temperature").GetDouble(),
                WindSpeed: cur.GetProperty("wind_speed_10m").GetDouble(),
                Humidity: cur.GetProperty("relative_humidity_2m").GetInt32(),
                Condition: WmoDescription(code),
                Icon: WmoIcon(code),
                Latitude: lat,
                Longitude: lon
            );
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to fetch weather for {City}", city);
            return null;
        }
    }

    public async Task<IEnumerable<WeatherForecastDay>> GetForecastAsync(string city, int days = 5, CancellationToken ct = default)
    {
        try
        {
            var geo = await GeocodeAsync(city, ct);
            if (geo is null) return [];
            var (lat, lon, _) = geo.Value;

            var url = $"https://api.open-meteo.com/v1/forecast?latitude={lat}&longitude={lon}" +
                      $"&daily=temperature_2m_max,temperature_2m_min,weather_code&forecast_days={days}" +
                      "&temperature_unit=celsius&timezone=auto";

            var resp = await http.GetStringAsync(url, ct);
            using var doc = JsonDocument.Parse(resp);
            var daily = doc.RootElement.GetProperty("daily");

            var dates = daily.GetProperty("time").EnumerateArray().ToList();
            var maxTemps = daily.GetProperty("temperature_2m_max").EnumerateArray().ToList();
            var minTemps = daily.GetProperty("temperature_2m_min").EnumerateArray().ToList();
            var codes = daily.GetProperty("weather_code").EnumerateArray().ToList();

            return dates.Select((d, i) => new WeatherForecastDay(
                Date: d.GetString() ?? "",
                MaxTemp: maxTemps[i].GetDouble(),
                MinTemp: minTemps[i].GetDouble(),
                Condition: WmoDescription(codes[i].GetInt32())
            )).ToList();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to fetch forecast for {City}", city);
            return [];
        }
    }

    private static string WmoDescription(int code) => code switch
    {
        0 => "Clear sky",
        1 => "Mainly clear",
        2 => "Partly cloudy",
        3 => "Overcast",
        45 or 48 => "Foggy",
        51 or 53 or 55 => "Drizzle",
        61 or 63 or 65 => "Rain",
        71 or 73 or 75 => "Snow",
        80 or 81 or 82 => "Rain showers",
        95 => "Thunderstorm",
        96 or 99 => "Thunderstorm with hail",
        _ => "Unknown"
    };

    private static string WmoIcon(int code) => code switch
    {
        0 => "☀️",
        1 or 2 => "⛅",
        3 => "☁️",
        45 or 48 => "🌫️",
        51 or 53 or 55 or 61 or 63 or 65 or 80 or 81 or 82 => "🌧️",
        71 or 73 or 75 => "❄️",
        95 or 96 or 99 => "⛈️",
        _ => "🌡️"
    };
}
