using System.Text.Json;

namespace FatfMonitor.Api.Compliance;

public interface IFatfSnapshotStore
{
    Task<FatfSnapshot?> ReadLatestAsync(CancellationToken cancellationToken);
    Task SaveLatestAsync(FatfSnapshot snapshot, CancellationToken cancellationToken);
}

public sealed class FileFatfSnapshotStore(IConfiguration configuration) : IFatfSnapshotStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string snapshotPath = configuration["FatfMonitor:SnapshotPath"]
        ?? Path.Combine("data", "fatf-latest.json");

    public async Task<FatfSnapshot?> ReadLatestAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(snapshotPath))
        {
            return null;
        }

        await using var stream = File.OpenRead(snapshotPath);
        return await JsonSerializer.DeserializeAsync<FatfSnapshot>(stream, JsonOptions, cancellationToken);
    }

    public async Task SaveLatestAsync(FatfSnapshot snapshot, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(snapshotPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var stream = File.Create(snapshotPath);
        await JsonSerializer.SerializeAsync(stream, snapshot, JsonOptions, cancellationToken);
    }
}
