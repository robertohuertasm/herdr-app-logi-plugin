namespace HerdrApp.Tests;

public class ConfigTests
{
    [Theory]
    [InlineData("""{"activateApp":false}""", false, Config.DefaultBundleId)]
    [InlineData("""{"activateApp":true,"appBundleId":"dev.herdr.app.beta"}""", true, "dev.herdr.app.beta")]
    [InlineData("""{"appBundleId":""}""", true, Config.DefaultBundleId)]
    [InlineData("not json", true, Config.DefaultBundleId)]
    [InlineData("[]", true, Config.DefaultBundleId)]
    public void ReadsOptionsAndFallsBackToDefaults(string json, bool activate, string bundle)
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, json);
            Assert.Equal(new Config(activate, bundle), Config.Load(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void MissingFileMeansDefaults() =>
        Assert.Equal(new Config(), Config.Load(Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.json")));
}
