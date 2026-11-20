namespace HerdrApp.Tests;

public class SnapshotTests
{
    [Fact]
    public async Task OnlyTerminalsThatReportAStatusAreAgents()
    {
        using var app = new FakeApp { Ls = Fixtures.Ls, Status = Fixtures.Status };
        var snapshot = await app.Client().GetSnapshotAsync();
        Assert.Equal(new[] { "a1", "a2", "vps/t9" }, snapshot.Agents.Select(a => a.Key));
        Assert.Equal(5, snapshot.Panes.Count());
        var remote = snapshot.Agents[2];
        Assert.Equal("vps", remote.Machine);
        Assert.Equal("vps/p5", remote.PaneId);
        Assert.Null(snapshot.Agents[0].Machine);
    }

    [Fact]
    public async Task SpacesCarryPositionFocusAndMostUrgentStatus()
    {
        using var app = new FakeApp { Ls = Fixtures.Ls, Status = Fixtures.Status };
        var snapshot = await app.Client().GetSnapshotAsync();
        Assert.Equal(new[] { 1, 2, 3 }, snapshot.Spaces.Select(s => s.Index));
        Assert.Equal(new[] { "working", "blocked", "idle" }, snapshot.Spaces.Select(s => s.AgentStatus));
        Assert.Equal("s1", snapshot.FocusedSpace?.SpaceId);
        Assert.Equal("t1", snapshot.FocusedTab?.TabId);
        Assert.Equal("p1", snapshot.FocusedPaneId);
        Assert.Equal("t1", snapshot.Spaces[0].ActiveTabId);
    }

    [Fact]
    public async Task SpaceWithoutAgentsHasNoStatus()
    {
        using var app = new FakeApp
        {
            Ls = """{"spaces":[{"id":"s1","name":"x","machine":null,"active":true,"tabs":[{"id":"t1","name":"1","active":true,"settled":false,"terminals":[{"terminal":"t","pane":"p","status":null}]}]}]}""",
        };
        var snapshot = await app.Client().GetSnapshotAsync();
        Assert.Equal("none", snapshot.Spaces[0].AgentStatus);
        Assert.Empty(snapshot.Agents);
    }

    [Fact]
    public async Task RecentPutsTheLatestStatusChangeFirst()
    {
        using var app = new FakeApp { Ls = Fixtures.Ls, Status = Fixtures.Status };
        var client = app.Client();
        Assert.Equal(new[] { "a1", "a2", "vps/t9" }, (await client.GetSnapshotAsync()).Recent.Select(a => a.Key));
        app.Ls = Fixtures.Ls.Replace("\"status\":\"idle\"", "\"status\":\"done\"");
        Assert.Equal(new[] { "vps/t9", "a1", "a2" }, (await client.GetSnapshotAsync()).Recent.Select(a => a.Key));
    }

    [Fact]
    public async Task AppErrorsBecomeExceptions()
    {
        using var app = new FakeApp { OnDo = (_, _) => """{"error":"no such pane"}""" };
        var error = await Assert.ThrowsAsync<HerdrAppException>(() => app.Client().FocusPaneAsync("p9"));
        Assert.Contains("no such pane", error.Message);
    }

    [Fact]
    public async Task MissingWindowIsReportedAsAnError()
    {
        var path = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.sock");
        await Assert.ThrowsAnyAsync<Exception>(() => new HerdrAppClient(path, new NoWindow()).GetSnapshotAsync());
    }

    [Fact]
    public async Task AgentFocusUsesTheTerminalAddress()
    {
        using var app = new FakeApp();
        await app.Client().FocusAgentAsync("vps/t9");
        Assert.Equal(new[] { ("agent.focus", """{"terminal":"vps/t9"}""") }, app.Commands);
    }

    [Fact]
    public async Task OverlappingAgentPressesAreDropped()
    {
        using var app = new FakeApp();
        var release = new ManualResetEventSlim();
        app.OnDo = (_, _) => { release.Wait(TimeSpan.FromSeconds(5)); return "null"; };
        var client = app.Client();
        using var log = new LogCapture();
        var first = client.FocusAgentAsync("a1");
        while (client.PendingAgentKey is null) await Task.Delay(5);
        await client.FocusAgentAsync("a2");
        release.Set();
        await first;
        Assert.Null(client.PendingAgentKey);
        Assert.Equal(new[] { "agent.focus" }, app.Names);
        Assert.Contains(log.Infos, line => line.Contains("ignored"));
    }
}
