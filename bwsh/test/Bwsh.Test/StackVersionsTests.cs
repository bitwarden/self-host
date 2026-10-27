namespace Bit.SelfHost.Test;

public class StackVersionsTests
{
    [Fact]
    public void Parse_ReadsAllVersions()
    {
        var json = """
            { "versions": { "coreVersion": "2026.10.0", "webVersion": "2026.10.1", "keyConnectorVersion": "2025.11.0" } }
            """;

        var versions = StackVersions.Parse(json);

        Assert.Equal(new StackVersions("2026.10.0", "2026.10.1", "2025.11.0"), versions);
    }

    [Theory]
    [InlineData("""{ "versions": { "webVersion": "2026.10.1", "keyConnectorVersion": "2025.11.0" } }""")]
    [InlineData("""{ "versions": { "coreVersion": "", "webVersion": "2026.10.1", "keyConnectorVersion": "2025.11.0" } }""")]
    [InlineData("""{ "versions": { "coreVersion": 2026, "webVersion": "2026.10.1", "keyConnectorVersion": "2025.11.0" } }""")]
    public void Parse_MissingOrInvalidVersion_Throws(string json)
    {
        Assert.Throws<FormatException>(() => StackVersions.Parse(json));
    }

    [Fact]
    public void Embedded_MatchesRepositoryVersionFile()
    {
        var repositoryFile = Path.Combine(AppContext.BaseDirectory, "../../../../../../version.json");

        Assert.Equal(StackVersions.Parse(File.ReadAllText(repositoryFile)), StackVersions.Embedded);
    }
}
