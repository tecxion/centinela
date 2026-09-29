namespace Centinela.Platform.Tests;

public class SmokeTests
{
    [Fact]
    public void Platform_assembly_loads() =>
        Assert.Equal("Centinela.Platform.Tests", typeof(SmokeTests).Assembly.GetName().Name);
}
