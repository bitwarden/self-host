namespace Bit.SelfHost.Test;

public class AssemblyTests
{
    [Fact]
    public void AssemblyName_IsBwsh()
    {
        var name = typeof(Program).Assembly.GetName().Name;

        Assert.Equal("bwsh", name);
    }
}
