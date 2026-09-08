using System.Text.Json;
using F1Dash.Core.Analysis;
using MongoDB.Bson;
using MongoDB.Driver;

namespace F1Dash.Server.Storage;

/// <summary>
/// The document index: analysis documents and race control messages.
///
/// These earn a document store rather than tables because their shape changes
/// between eras and the change is not a mistake. `analysis.json` gained an
/// overtake counter in 2026 and lost DRS; in a relational schema that is a
/// migration for every season boundary, here it is nothing at all.
///
/// The disk copy stays. Reads fall back to `analysis.json` when this is not
/// configured, so nothing depends on Mongo being up.
/// </summary>
public sealed class MongoStore : IStorageIndex
{
    private readonly ILogger _logger;
    private readonly IMongoDatabase? _database;

    public MongoStore(ILogger<MongoStore> logger)
    {
        _logger = logger;

        var url = Environment.GetEnvironmentVariable("MONGO_URL");
        if (string.IsNullOrWhiteSpace(url))
        {
            Available = false;
            return;
        }

        try
        {
            var settings = MongoClientSettings.FromConnectionString(url);
            // Without this the driver blocks for thirty seconds on every call
            // when Mongo is down, which would turn an optional index into a
            // hang on the session-end path.
            settings.ServerSelectionTimeout = TimeSpan.FromSeconds(5);

            _database = new MongoClient(settings).GetDatabase("apex");
            Available = true;
        }
        catch (MongoConfigurationException e)
        {
            _logger.LogWarning(e, "MONGO_URL is not usable; document indexing disabled.");
            Available = false;
        }
    }

    public string Name => "mongodb";
    public bool Available { get; private set; }

    public async Task InitialiseAsync(CancellationToken ct)
    {
        if (!Available || _database is null) return;

        try
        {
            await _database.RunCommandAsync<BsonDocument>(
                new BsonDocument("ping", 1), cancellationToken: ct).ConfigureAwait(false);

            var analysis = _database.GetCollection<BsonDocument>("sessions_analysis");
            await analysis.Indexes.CreateOneAsync(
                new CreateIndexModel<BsonDocument>(
                    Builders<BsonDocument>.IndexKeys.Ascending("key"),
                    new CreateIndexOptions { Unique = true }),
                cancellationToken: ct).ConfigureAwait(false);

            var control = _database.GetCollection<BsonDocument>("race_control");
            await control.Indexes.CreateOneAsync(
                new CreateIndexModel<BsonDocument>(
                    Builders<BsonDocument>.IndexKeys.Ascending("key").Ascending("lap")),
                cancellationToken: ct).ConfigureAwait(false);

            _logger.LogInformation("MongoDB collections ready");
        }
        catch (Exception e) when (e is MongoException or TimeoutException)
        {
            _logger.LogWarning(e, "MongoDB unreachable; document indexing disabled.");
            Available = false;
        }
    }

    public async Task IndexAsync(SessionKey key, SessionAnalysis analysis, CancellationToken ct)
    {
        if (!Available || _database is null) return;

        try
        {
            // Serialised through System.Text.Json first, so what lands in Mongo
            // is byte-for-byte the document the API already serves. Letting the
            // Mongo driver map the records directly would produce a second,
            // subtly different shape.
            var json = JsonSerializer.Serialize(analysis, JsonOptions);
            var document = BsonDocument.Parse(json);
            document["key"] = key.ToString();
            document["year"] = key.Year;
            document["indexedAt"] = DateTime.UtcNow;

            var collection = _database.GetCollection<BsonDocument>("sessions_analysis");

            // Replace, not insert: re-indexing is how a bad parse gets repaired.
            await collection.ReplaceOneAsync(
                Builders<BsonDocument>.Filter.Eq("key", key.ToString()),
                document,
                new ReplaceOptions { IsUpsert = true },
                ct).ConfigureAwait(false);

            _logger.LogInformation("Indexed {Key} into MongoDB", key);
        }
        catch (Exception e) when (e is MongoException or TimeoutException or JsonException)
        {
            _logger.LogWarning(e, "Could not index {Key} into MongoDB", key);
        }
    }

    /// <summary>The stored analysis for a session, or null when it is not indexed.</summary>
    public async Task<string?> ReadAnalysisAsync(SessionKey key, CancellationToken ct)
    {
        if (!Available || _database is null) return null;

        try
        {
            var collection = _database.GetCollection<BsonDocument>("sessions_analysis");
            var document = await collection
                .Find(Builders<BsonDocument>.Filter.Eq("key", key.ToString()))
                .FirstOrDefaultAsync(ct).ConfigureAwait(false);

            if (document is null) return null;

            // The bookkeeping fields are ours, not part of the contract.
            document.Remove("_id");
            document.Remove("key");
            document.Remove("year");
            document.Remove("indexedAt");

            return document.ToJson();
        }
        catch (Exception e) when (e is MongoException or TimeoutException)
        {
            _logger.LogWarning(e, "Could not read {Key} from MongoDB", key);
            return null;
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };
}
