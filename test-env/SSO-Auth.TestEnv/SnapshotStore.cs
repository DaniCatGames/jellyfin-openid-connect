using System.Formats.Tar;
using ZstdSharp;

namespace Jellyfin.Plugin.SSO_Auth.TestEnv;

public sealed class SnapshotStore(EnvConfig config)
{
    /// <summary>
    /// Idempotent: extracts the snapshot for the pinned Jellyfin version into the config dir if and only if the
    /// config dir is empty or missing. Matches the bash up.sh check exactly.
    /// </summary>
    public async Task RestoreIfEmptyAsync(CancellationToken ct = default)
    {
        var snapshotPath = config.SnapshotPath;
        if (!File.Exists(snapshotPath))
        {
            throw new OrchestrationException(
                $"No snapshot for Jellyfin {config.JellyfinVersion} at {snapshotPath}.\n" +
                "Run scripts/snapshot-create.sh to produce one, or set JELLYFIN_VERSION to a version with an " +
                "existing snapshot in test-env/snapshots/.");
        }

        var configDir = config.JellyfinConfigDir;
        if (Directory.Exists(configDir) && Directory.EnumerateFileSystemEntries(configDir).Any())
        {
            Console.Out.WriteLine("[+] Config directory already populated — skipping snapshot restore.");
            return;
        }

        Console.Out.WriteLine($"[+] Config directory is empty — restoring snapshot {Path.GetFileName(snapshotPath)} ...");
        Directory.CreateDirectory(configDir);
        await ExtractAsync(snapshotPath, configDir, ct);
    }

    /// <summary>
    /// Force-restore (no idempotency check). Used by per-test reset in Plan 2.
    /// The caller is responsible for wiping the config dir first (e.g. via
    /// <see cref="ContainerStack.WipeConfigDirAsync"/>) before calling this, because
    /// the Jellyfin container runs as root and the config files are root-owned — the
    /// current process cannot delete them with <see cref="Directory.Delete"/>.
    /// </summary>
    public async Task ForceRestoreAsync(CancellationToken ct = default)
    {
        var snapshotPath = config.SnapshotPath;
        if (!File.Exists(snapshotPath))
        {
            throw new OrchestrationException($"No snapshot at {snapshotPath}.");
        }

        var configDir = config.JellyfinConfigDir;
        Directory.CreateDirectory(configDir);
        Console.Out.WriteLine($"[+] Force-restoring snapshot {Path.GetFileName(snapshotPath)} ...");
        await ExtractAsync(snapshotPath, configDir, ct);
    }

    private static async Task ExtractAsync(string snapshotPath, string targetDir, CancellationToken ct)
    {
        await using var fileStream = File.OpenRead(snapshotPath);
        await using var decompressed = new DecompressionStream(fileStream);
        await TarFile.ExtractToDirectoryAsync(decompressed, targetDir, overwriteFiles: true, ct);
    }
}
