using Bit.SelfHost.Commands;
using Bit.SelfHost.Engine;
using Xunit;

namespace Bit.SelfHost.Test;

public class MigrateAdoptTests
{
    private static IReadOnlyList<ServiceSpec> SampleTopology() =>
    [
        new()
        {
            Name = "mssql",
            ContainerName = "bitwarden-mssql",
            Image = "ghcr.io/bitwarden/mssql:2026.4.1",
            Binds =
            [
                ("bitwarden-mssql-data", "/var/opt/mssql/data"),
                ("/r/logs/mssql", "/etc/bitwarden/logs"),
                ("/r/mssql/backups", "/etc/bitwarden/mssql/backups"),
            ],
        },
        new() { Name = "nginx", ContainerName = "bitwarden-nginx", Image = "ghcr.io/bitwarden/nginx:2026.4.1", Ports = [(80, 8080), (443, 8443)] },
        new() { Name = "api", ContainerName = "bitwarden-api", Image = "ghcr.io/bitwarden/api:2026.4.1" },
    ];

    [Fact]
    public void Adopt_repoints_mssql_data_to_the_existing_host_bind_and_keeps_the_rest()
    {
        var adopted = MigrateCommand.AdoptStandard(SampleTopology(), "/r");

        var mssql = adopted.Single(s => s.Name == "mssql");
        Assert.Contains(("/r/mssql/data", "/var/opt/mssql/data"), mssql.Binds);
        Assert.DoesNotContain(("bitwarden-mssql-data", "/var/opt/mssql/data"), mssql.Binds);
        // logs + backups binds preserved
        Assert.Contains(("/r/logs/mssql", "/etc/bitwarden/logs"), mssql.Binds);
        Assert.Contains(("/r/mssql/backups", "/etc/bitwarden/mssql/backups"), mssql.Binds);
    }

    [Fact]
    public void Adopt_does_not_touch_other_services()
    {
        var adopted = MigrateCommand.AdoptStandard(SampleTopology(), "/r");
        var api = adopted.Single(s => s.Name == "api");
        Assert.Equal("ghcr.io/bitwarden/api:2026.4.1", api.Image);
    }
}
