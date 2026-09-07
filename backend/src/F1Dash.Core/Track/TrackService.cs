using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace F1Dash.Core.Track;

/// <summary>
/// Builds renderable circuit geometry from the MultiViewer circuits API.
///
/// MultiViewer is the right source because its coordinates are in the SAME
/// native F1 system as the live Position feed — car dots land on the outline
/// with no fitting, scaling or georeferencing (docs/10). Drawing the outline
/// from a driver's telemetry lap instead produces a wobbly line that differs
/// per driver.
///
/// Results are cached on disk and never expire: a circuit's geometry does not
/// change mid-season, and an upstream outage must not break the map.
/// </summary>
public sealed class TrackService(HttpClient http, string cacheDirectory)
{
    public const string DefaultBaseUrl = "https://api.multiviewer.app/api/v1";

    /// <summary>Padding around the outline, as a fraction of its extent.</summary>
    private const double Padding = 0.04;

    /// <summary>Track units are roughly 1/10 metre, so 260 is about 26 m.</summary>
    public const int TrackStrokeWidth = 260;

    public string BaseUrl { get; init; } = DefaultBaseUrl;

    public static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        // A polite agent that identifies the app, as MultiViewer asks.
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("f1-dash", "0.1"));
        return client;
    }

    public async Task<TrackGeometry?> GetAsync(int circuitKey, int year, CancellationToken ct = default)
    {
        var raw = await LoadRawAsync(circuitKey, year, ct).ConfigureAwait(false);
        return raw is null ? null : Build(raw, circuitKey, year);
    }

    /// <summary>Disk cache first; upstream only on a miss.</summary>
    private async Task<JsonObject?> LoadRawAsync(int circuitKey, int year, CancellationToken ct)
    {
        var path = Path.Combine(cacheDirectory, $"circuit-{circuitKey}-{year}.json");

        if (File.Exists(path))
        {
            try
            {
                return JsonNode.Parse(await File.ReadAllTextAsync(path, ct).ConfigureAwait(false)) as JsonObject;
            }
            catch (JsonException)
            {
                // A truncated cache file is worth replacing, not crashing on.
            }
        }

        try
        {
            var url = $"{BaseUrl}/circuits/{circuitKey}/{year}";
            var text = await http.GetStringAsync(url, ct).ConfigureAwait(false);

            if (JsonNode.Parse(text) is not JsonObject parsed) return null;

            Directory.CreateDirectory(cacheDirectory);
            await File.WriteAllTextAsync(path, text, ct).ConfigureAwait(false);
            return parsed;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException)
        {
            // Upstream is down and nothing is cached. The map hides; the rest of
            // the dashboard is unaffected.
            return null;
        }
    }

    private static TrackGeometry Build(JsonObject raw, int circuitKey, int year)
    {
        var rotation = (double?)raw["rotation"] ?? 0;
        var transform = new Transform(rotation);

        var points = ReadOutline(raw, transform);
        if (points.Count == 0)
        {
            return new TrackGeometry(
                circuitKey, (string?)raw["circuitName"] ?? "", year, rotation,
                "", new ViewBox(0, 0, 1, 1), [], [], new Point(0, 0));
        }

        var corners = ReadCorners(raw, transform);
        var marshalSectors = BuildMarshalSectors(raw, transform, points);

        return new TrackGeometry(
            CircuitKey: circuitKey,
            CircuitName: (string?)raw["circuitName"] ?? "",
            Year: year,
            Rotation: rotation,
            Path: BuildPath(points, closed: true),
            ViewBox: BuildViewBox(points),
            Corners: corners,
            MarshalSectors: marshalSectors,
            // The outline starts at the start/finish line.
            StartFinish: points[0]);
    }

    private static List<Point> ReadOutline(JsonObject raw, Transform transform)
    {
        var xs = raw["x"] as JsonArray;
        var ys = raw["y"] as JsonArray;
        var points = new List<Point>();
        if (xs is null || ys is null) return points;

        var count = Math.Min(xs.Count, ys.Count);
        for (var i = 0; i < count; i++)
        {
            var (x, y) = transform.Apply((double?)xs[i] ?? 0, (double?)ys[i] ?? 0);
            points.Add(new Point(x, y));
        }
        return points;
    }

    private static List<CornerMarker> ReadCorners(JsonObject raw, Transform transform)
    {
        var result = new List<CornerMarker>();
        if (raw["corners"] is not JsonArray corners) return result;

        foreach (var corner in corners.OfType<JsonObject>())
        {
            var position = corner["trackPosition"] as JsonObject;
            if (position is null) continue;

            var (x, y) = transform.Apply((double?)position["x"] ?? 0, (double?)position["y"] ?? 0);

            // Corner labels sit outside the apex, offset along the corner's own
            // angle so they do not overlap the track surface.
            var angle = ((double?)corner["angle"] ?? 0) * Math.PI / 180.0;
            const double LabelOffset = 700;
            var (lx, ly) = transform.Apply(
                ((double?)position["x"] ?? 0) + (Math.Cos(angle) * LabelOffset),
                ((double?)position["y"] ?? 0) + (Math.Sin(angle) * LabelOffset));

            result.Add(new CornerMarker((int?)corner["number"] ?? 0, x, y, lx, ly));
        }
        return result;
    }

    /// <summary>
    /// Each marshal sector owns the stretch of outline between its own start
    /// point and the next one's, so the map can colour a single flagged zone.
    /// </summary>
    private static List<MarshalSector> BuildMarshalSectors(
        JsonObject raw, Transform transform, List<Point> outline)
    {
        var result = new List<MarshalSector>();
        if (raw["marshalSectors"] is not JsonArray sectors || sectors.Count == 0) return result;

        var starts = new List<(int Number, int Index)>();
        foreach (var sector in sectors.OfType<JsonObject>())
        {
            var position = sector["trackPosition"] as JsonObject;
            if (position is null) continue;

            var (x, y) = transform.Apply((double?)position["x"] ?? 0, (double?)position["y"] ?? 0);
            starts.Add(((int?)sector["number"] ?? 0, NearestIndex(outline, x, y)));
        }

        starts.Sort((a, b) => a.Index.CompareTo(b.Index));

        for (var i = 0; i < starts.Count; i++)
        {
            var from = starts[i].Index;
            var to = starts[(i + 1) % starts.Count].Index;

            var slice = new List<Point>();
            // The final sector wraps past the start/finish line.
            for (var j = from; j != to; j = (j + 1) % outline.Count)
            {
                slice.Add(outline[j]);
                if (slice.Count > outline.Count) break;
            }
            slice.Add(outline[to]);

            result.Add(new MarshalSector(starts[i].Number, BuildPath(slice, closed: false)));
        }

        return result;
    }

    private static int NearestIndex(List<Point> points, double x, double y)
    {
        var best = 0;
        var bestDistance = double.MaxValue;

        for (var i = 0; i < points.Count; i++)
        {
            var dx = points[i].X - x;
            var dy = points[i].Y - y;
            var distance = (dx * dx) + (dy * dy);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = i;
            }
        }
        return best;
    }

    private static string BuildPath(IReadOnlyList<Point> points, bool closed)
    {
        if (points.Count == 0) return "";

        var parts = points.Select(p =>
            p.X.ToString("0", CultureInfo.InvariantCulture) + "," +
            p.Y.ToString("0", CultureInfo.InvariantCulture));

        return "M " + string.Join(" L ", parts) + (closed ? " Z" : "");
    }

    private static ViewBox BuildViewBox(IReadOnlyList<Point> points)
    {
        var minX = points.Min(p => p.X);
        var maxX = points.Max(p => p.X);
        var minY = points.Min(p => p.Y);
        var maxY = points.Max(p => p.Y);

        var width = Math.Max(1, maxX - minX);
        var height = Math.Max(1, maxY - minY);

        // Pad by the stroke width as well as a fraction, or the track edge is
        // clipped on circuits that run right to the bounding box.
        var padX = (width * Padding) + (TrackStrokeWidth / 2.0);
        var padY = (height * Padding) + (TrackStrokeWidth / 2.0);

        return new ViewBox(minX - padX, minY - padY, width + (2 * padX), height + (2 * padY));
    }
}
