namespace Tabletop.Management.Content.Assets;

/// <summary>Deletes what an upload leaves behind when confirm never succeeds: staging objects, and asset and blob rows whose
/// bytes never arrived, once they are older than Age. A presigned PUT lives 5 minutes, so nothing that old is still in flight.</summary>
public sealed class UploadSweep(IAssetRepository assets, IBlobStore blobs)
{
    public static readonly TimeSpan Age = TimeSpan.FromHours(1);

    /// <summary>Staging objects deleted, and blob rows deleted (with their asset rows).</summary>
    public sealed record Result(int StagingObjects, int BlobRows);

    public async Task<Result> Run(DateTimeOffset now)
    {
        var cutoff = now - Age;
        var staged = await blobs.ListOlderThan(IBlobStore.StagingPrefix, cutoff);
        if (staged.Count > 0) await blobs.Delete(staged);
        var orphaned = await assets.DeleteAbandoned(cutoff);
        if (orphaned.Count > 0) await blobs.Delete(orphaned);
        return new Result(staged.Count, orphaned.Count);
    }
}

/// <summary>Runs UploadSweep every Interval; the first run is one Interval after start. A failed run is logged and the next tries again.</summary>
public sealed class UploadSweeper(IAssetRepository assets, IBlobStore blobs, ILogger<UploadSweeper> log) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var sweep = new UploadSweep(assets, blobs);
        using var timer = new PeriodicTimer(Interval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    var r = await sweep.Run(DateTimeOffset.UtcNow);
                    if (r.StagingObjects + r.BlobRows > 0) log.LogInformation("Upload sweep deleted {Objects} staging objects and {Rows} unconfirmed blob rows", r.StagingObjects, r.BlobRows);
                }
                catch (Exception e) when (e is not OperationCanceledException) { log.LogWarning(e, "Upload sweep failed"); }
            }
        }
        catch (OperationCanceledException) { }
    }
}
