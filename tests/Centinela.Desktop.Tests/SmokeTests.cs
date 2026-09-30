namespace Centinela.Desktop.Tests;

public class SmokeTests
{
    [Fact]
    public void Test_assembly_runs() =>
        Assert.Equal("Centinela.Desktop.Tests", typeof(SmokeTests).Assembly.GetName().Name);
}
