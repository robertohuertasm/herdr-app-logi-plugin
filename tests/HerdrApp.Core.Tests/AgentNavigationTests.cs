using System.Text.Json;

namespace HerdrApp.Tests;

public class AgentNavigationTests
{
    // The live failure had one local agent and six remote agents. Three remote
    // agents were kept children of a parent without a tab in this window.
    private static readonly string[] Addresses =
        ["local", "remote/redapl", "remote/orchestrator", "remote/10072", "remote/8086", "remote/10065", "remote/9750"];

    private static string SevenAgents => JsonSerializer.Serialize(new
    {
        spaces = new object[]
        {
            new { id = "local-space", tabs = new[] { new { id = "local-tab", terminals = new[]
                { new { terminal = Addresses[0], pane = "local-pane", status = "working" } } } } },
            new { id = "remote/space", machine = "remote", tabs = Addresses.Skip(1).Select(address => new
                { id = address + "-tab", terminals = new[] { new { terminal = address, pane = address + "-pane", status = "idle" } } }).ToArray() },
        },
    });

    private static string Focus(string address) => JsonSerializer.Serialize(new
    {
        space = address == "local" ? "local-space" : "remote/space",
        tab = address == "local" ? "local-tab" : address + "-tab",
        focused_pane = address == "local" ? "local-pane" : address + "-pane",
    });

    [Theory]
    [InlineData("agent-next", 1)]
    [InlineData("agent-prev", -1)]
    public async Task VisitsAllSevenAgentsIncludingKeptChildrenAndWraps(string shortcut, int direction)
    {
        using var app = new FakeApp { Ls = SevenAgents, Status = Focus(Addresses[0]) };
        app.OnDo = (name, args) =>
        {
            if (name == "agent.focus") app.Status = Focus(args.GetProperty("terminal").GetString()!);
            return "null";
        };
        var navigation = new Navigation(app.Client());

        for (var step = 1; step <= Addresses.Length + 1; step++)
            await navigation.ExecuteAsync(shortcut);

        var expected = Enumerable.Range(1, Addresses.Length + 1)
            .Select(step => Addresses[((step * direction) % Addresses.Length + Addresses.Length) % Addresses.Length])
            .Select(address => ("agent.focus", JsonSerializer.Serialize(new { terminal = address })));
        Assert.Equal(expected, app.Commands);
    }

    [Fact]
    public async Task FollowsManualFocusAndKeepsSpaceTabPaneOrderWhenStatusChanges()
    {
        using var app = new FakeApp { Ls = SevenAgents, Status = Focus("remote/10072") };
        var navigation = new Navigation(app.Client());
        await navigation.ExecuteAsync("agent-next");

        // An external click and status update must determine the next press,
        // rather than a cached cursor or the recent-state-change ranking.
        app.Status = Focus("remote/10065");
        app.Ls = SevenAgents.Replace("\"idle\"", "\"working\"", StringComparison.Ordinal);
        await navigation.ExecuteAsync("agent-next");
        Assert.Equal(new[]
        {
            ("agent.focus", """{"terminal":"remote/8086"}"""),
            ("agent.focus", """{"terminal":"remote/9750"}"""),
        }, app.Commands);
    }

    [Theory]
    [InlineData("agent-next", "a1")]
    [InlineData("agent-prev", "vps/t9")]
    public async Task FromAShellStartsAtTheFirstOrLastAgent(string shortcut, string expected)
    {
        using var app = new FakeApp { Ls = Fixtures.Ls, Status = """{"focused_pane":"p2"}""" };
        await new Navigation(app.Client()).ExecuteAsync(shortcut);
        Assert.Equal(new[] { ("agent.focus", JsonSerializer.Serialize(new { terminal = expected })) }, app.Commands);
    }

    [Theory]
    [InlineData("agent-next")]
    [InlineData("agent-prev")]
    public async Task EmptyListOrTheOnlyAgentAlreadyFocusedHasNothingToCycle(string shortcut)
    {
        using var app = new FakeApp();
        var navigation = new Navigation(app.Client());
        await navigation.ExecuteAsync(shortcut);
        app.Ls = """{"spaces":[{"tabs":[{"terminals":[{"terminal":"only","pane":"p1","status":"idle"}]}]}]}""";
        app.Status = """{"focused_pane":"p1"}""";
        await navigation.ExecuteAsync(shortcut);
        Assert.Empty(app.Commands);
    }

    [Fact]
    public async Task RefreshesTheAgentListAfterAnAgentDisappears()
    {
        using var app = new FakeApp { Ls = Fixtures.Ls, Status = Fixtures.Status };
        var navigation = new Navigation(app.Client());
        await navigation.ExecuteAsync("agent-next");
        app.Ls = Fixtures.Ls.Replace("\"status\":\"blocked\"", "\"status\":null", StringComparison.Ordinal);
        await navigation.ExecuteAsync("agent-next");
        Assert.Equal(new[]
        {
            ("agent.focus", """{"terminal":"a2"}"""),
            ("agent.focus", """{"terminal":"vps/t9"}"""),
        }, app.Commands);
    }
}
