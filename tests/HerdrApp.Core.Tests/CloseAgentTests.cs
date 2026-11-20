namespace HerdrApp.Tests;

public class CloseAgentTests
{
    [Theory]
    [InlineData("p4", "codex", true)]
    [InlineData("p8", "codex", false)]
    [InlineData("p4", "claude", false)]
    public async Task CloseChecksTheAgentIsUnchangedBeforeClosing(string pane, string agentApp, bool expected)
    {
        using var app = new FakeApp { Ls = Fixtures.Ls, Status = Fixtures.Status };
        var close = new CloseAgent(app.Client());
        Assert.Equal(expected, await close.CloseIfStillPresentAsync(new("a2", pane, "s2", agentApp)));
        Assert.Equal(expected ? new[] { ("pane.close", """{"pane":"p4"}""") } : [], app.Commands);
    }

    [Fact]
    public async Task RemoteAgentsCloseThroughTheAppToo()
    {
        using var app = new FakeApp { Ls = Fixtures.Ls, Status = Fixtures.Status };
        Assert.True(await new CloseAgent(app.Client()).CloseIfStillPresentAsync(new("vps/t9", "vps/p5", "vps/s3", "pi")));
        Assert.Equal(new[] { ("pane.close", """{"pane":"vps/p5"}""") }, app.Commands);
    }
}
