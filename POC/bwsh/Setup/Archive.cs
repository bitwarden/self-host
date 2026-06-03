using System.Formats.Tar;
using System.IO.Compression;
using System.Text;

namespace Bit.SelfHost.Setup;

/// <summary>
/// Plain .tar.gz pack/unpack over the in-box System.Formats.Tar (no external `tar`), used by
/// `backup`/`restore`. Pack archives everything under the data dir EXCEPT the excluded subtrees
/// (logs, the raw mssql/data — the .bak is the DB snapshot) plus a manifest.txt; this captures
/// both standard (mssql/backups) and lite (vault.db, attachments) layouts. Unpack extracts under a root.
/// </summary>
public static class Archive
{
    public static void Pack(string root, string outPath, string manifest, IReadOnlyList<string> excludeRelative)
    {
        var rootFull = Path.GetFullPath(root);
        var outFull = Path.GetFullPath(outPath);
        Directory.CreateDirectory(Path.GetDirectoryName(outFull)!);
        var excludes = excludeRelative.Select(e => e.Replace('\\', '/').TrimEnd('/')).ToArray();

        using var fs = File.Create(outFull);
        using var gz = new GZipStream(fs, CompressionLevel.Optimal);
        using var tar = new TarWriter(gz, TarEntryFormat.Pax);

        tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "manifest.txt")
        {
            DataStream = new MemoryStream(Encoding.UTF8.GetBytes(manifest)),
        });

        foreach (var file in Directory.EnumerateFiles(rootFull, "*", SearchOption.AllDirectories))
        {
            if (Path.GetFullPath(file) == outFull) continue; // never archive the archive itself
            var rel = Path.GetRelativePath(rootFull, file).Replace('\\', '/');
            if (excludes.Any(ex => rel == ex || rel.StartsWith(ex + "/", StringComparison.Ordinal))) continue;
            tar.WriteEntry(file, rel);
        }
    }

    public static void Unpack(string archivePath, string root)
    {
        var rootFull = Path.GetFullPath(root);
        Directory.CreateDirectory(rootFull);

        using var fs = File.OpenRead(archivePath);
        using var gz = new GZipStream(fs, CompressionMode.Decompress);
        using var tar = new TarReader(gz);

        while (tar.GetNextEntry() is { } entry)
        {
            if (entry.Name is "manifest.txt") continue;

            var dest = Path.GetFullPath(Path.Combine(rootFull, entry.Name));
            if (!dest.StartsWith(rootFull, StringComparison.Ordinal))
                throw new InvalidOperationException($"Refusing to extract entry outside the target root: {entry.Name}");

            if (entry.EntryType is TarEntryType.Directory)
            {
                Directory.CreateDirectory(dest);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            entry.ExtractToFile(dest, overwrite: true);
        }
    }
}
