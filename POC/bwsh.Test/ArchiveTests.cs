using Bit.SelfHost.Setup;
using Xunit;

namespace Bit.SelfHost.Test;

public class ArchiveTests
{
    [Fact]
    public void Pack_then_Unpack_keeps_data_and_drops_excluded_subtrees()
    {
        var src = Directory.CreateTempSubdirectory().FullName;
        Write(src, "config.yml", "url: https://x");
        Write(src, "env/global.override.env", "globalSettings__installation__id=11111111-1111-1111-1111-111111111111");
        Write(src, "core/attachments/file.bin", "attachment");
        Write(src, "mssql/backups/vault_FULL_20260602.BAK", "backup-bytes");
        Write(src, "logs/api/log.txt", "noisy");                 // excluded
        Write(src, "mssql/data/vault.mdf", "raw-db-files");       // excluded

        var archive = Path.Combine(Directory.CreateTempSubdirectory().FullName, "b.tar.gz");
        Archive.Pack(src, archive, "deployment: Standard\n", ["logs", "mssql/data"]);

        Assert.True(File.Exists(archive));
        Assert.True(new FileInfo(archive).Length > 0);

        var dst = Directory.CreateTempSubdirectory().FullName;
        Archive.Unpack(archive, dst);

        Assert.True(File.Exists(Path.Combine(dst, "config.yml")));
        Assert.True(File.Exists(Path.Combine(dst, "env/global.override.env")));
        Assert.True(File.Exists(Path.Combine(dst, "core/attachments/file.bin")));
        Assert.True(File.Exists(Path.Combine(dst, "mssql/backups/vault_FULL_20260602.BAK")));
        Assert.False(File.Exists(Path.Combine(dst, "logs/api/log.txt")));   // excluded subtree
        Assert.False(File.Exists(Path.Combine(dst, "mssql/data/vault.mdf"))); // excluded subtree
        Assert.False(File.Exists(Path.Combine(dst, "manifest.txt")));        // manifest isn't extracted as data
        Assert.Equal("attachment", File.ReadAllText(Path.Combine(dst, "core/attachments/file.bin")));
    }

    private static void Write(string root, string rel, string content)
    {
        var path = Path.Combine(root, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}
