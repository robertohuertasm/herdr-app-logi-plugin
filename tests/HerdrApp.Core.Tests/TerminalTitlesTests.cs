namespace HerdrApp.Tests;

public class TerminalTitlesTests
{
    [Theory]
    [InlineData("✳ RP PR", "RP PR")]
    [InlineData("test | dd-source", "test")]
    [InlineData("First | second | project", "First | second")]
    [InlineData("~/code/api", null)]
    [InlineData("bits@workspace", null)]
    [InlineData("  ", null)]
    public void TitlesLoseTheirGlyphProjectAndShellLocations(string title, string? expected) =>
        Assert.Equal(expected, TerminalTitles.Clean(title));

    [Theory]
    [InlineData("PR 9750 - inventory", "codex", true)]
    [InlineData("1", "codex", false)]
    [InlineData("Claude Code", "claude-code", false)]
    [InlineData("Codex", "codex", false)]
    [InlineData("", "codex", false)]
    public void TabNamesThatOnlyNumberOrNameTheProgramTellNothing(string tab, string app, bool expected) =>
        Assert.Equal(expected, TerminalTitles.InformativeTabName(new Agent { TabName = tab, App = app }));

    [Fact]
    public async Task OnlyAgentsWithoutAUsefulNameOrTabAreLookedUp()
    {
        var asked = new List<string>();
        var titles = new TerminalTitles(address =>
        {
            lock (asked) asked.Add(address);
            return Task.FromResult("""{"title":"✳ RP PR"}""");
        });
        Agent[] agents =
        [
            new() { TerminalId = "t1", TabName = "1", App = "claude-code" },
            new() { TerminalId = "t2", TabName = "PR 9750", App = "codex" },
            new() { TerminalId = "t3", TabName = "1", Name = "named", App = "pi" },
        ];
        await titles.RefreshAsync(["t1"]);
        var result = titles.Apply(agents);
        Assert.Equal(new[] { "RP PR", null, null }, result.Select(a => a.Title));
        // Apply may also start its own refresh; it too asks only for t1.
        await Task.Delay(50);
        lock (asked) Assert.Equal(new[] { "t1" }, asked.Distinct());
    }

    [Fact]
    public async Task SnapshotLabelsUseTheTitleOnceKnown()
    {
        using var app = new FakeApp { Ls = Fixtures.Ls, Status = Fixtures.Status };
        app.Titles["a1"] = "✳ Fix login | api";
        var client = app.Client();
        var first = await client.GetSnapshotAsync();
        Assert.Equal("api", FixedSlots.AgentLabel(first.Agents[0], 1));
        // The first poll starts the lookup; a later one carries the title.
        for (var i = 0; i < 100 && (await client.GetSnapshotAsync()).Agents[0].Title is null; i++) await Task.Delay(20);
        var next = await client.GetSnapshotAsync();
        Assert.Equal("Fix login", FixedSlots.AgentLabel(next.Agents[0], 1));
        Assert.Equal("Fix login", next.Recent.Single(a => a.Key == "a1").Title);
    }
}
