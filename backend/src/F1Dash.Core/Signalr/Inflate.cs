using System.Buffers;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace F1Dash.Core.Signalr;

/// <summary>
/// Decompresses the ".z" topics: base64, then <b>raw</b> DEFLATE with no zlib
/// header.
///
/// .NET's <see cref="DeflateStream"/> is raw DEFLATE, which is exactly what the
/// feed sends. Reaching for ZLibStream instead throws on the header — it is the
/// .NET equivalent of forgetting the negative window bits in Python, and it is
/// the classic first-day bug on this feed.
/// </summary>
public static class Inflate
{
    /// <summary>Decodes a base64 raw-DEFLATE payload into a JSON node.</summary>
    public static JsonNode? Decode(string base64)
    {
        var maxBytes = ((base64.Length * 3) / 4) + 4;
        var buffer = ArrayPool<byte>.Shared.Rent(maxBytes);
        try
        {
            if (!Convert.TryFromBase64String(base64, buffer, out var written))
            {
                throw new FormatException("Compressed topic payload was not valid base64.");
            }

            using var source = new MemoryStream(buffer, 0, written, writable: false);
            using var deflate = new DeflateStream(source, CompressionMode.Decompress);
            return JsonNode.Parse(deflate);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// The inverse, used by the simulator so the decompression path is genuinely
    /// exercised in development. If the simulator emitted already-decoded data,
    /// the ".z" handling would never run until race day (docs/14 §simulator, 6).
    /// </summary>
    public static string Encode(JsonNode? node)
    {
        using var destination = new MemoryStream();
        using (var deflate = new DeflateStream(destination, CompressionLevel.Optimal, leaveOpen: true))
        using (var writer = new Utf8JsonWriter(deflate))
        {
            node?.WriteTo(writer);
            writer.Flush();
        }
        return Convert.ToBase64String(destination.ToArray());
    }
}
