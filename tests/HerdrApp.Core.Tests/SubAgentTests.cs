namespace HerdrApp.Tests;

public class SubAgentTests
{
    // a1 started s1 (with its own child s2) and s3; s3 was kept as a pane.
    private const string Core = """
        {"terminals":[
          {"address":"a1","agent":"claude-code","state":"live","status":"working"},
          {"address":"s1","agent":"codex","parent":"a1","state":"live","status":"done"},
          {"address":"s2","agent":"sleep","parent":"s1","state":"live"},
          {"address":"s3","agent":"pi","parent":"a1","state":"live","status":"error","name":"tests"},
          {"address":"old","agent":"pi","parent":"a1","state":"finished"},
          {"address":"vps/t2","agent":"codex","parent":"t1","state":"live","status":"working"}]}
        """;

    private static readonly Snapshot Snapshot = new()
    {
        Spaces = [new Space { SpaceId = "s", Tabs = [new Tab { TabId = "t", Panes = [new Pane { PaneId = "p9", TerminalId = "s3" }] }] }],
        Agents = [new Agent { TerminalId = "a1", SubAgents = ["s1", "s3"] }],
    };

    [Fact]
    public void TreeIsDepthFirstWithStatusesLabelsAndCounts()
    {
        var subs = SubAgentTree.Build(Snapshot.Agents[0], Snapshot, SubAgentTree.Parse(Core));
        Assert.Equal(new[] { "s1", "s2", "s3" }, subs.Select(s => s.Address));
        // done is the parent's news; error needs you; no report is unknown.
        Assert.Equal(new[] { "idle", "unknown", "blocked" }, subs.Select(s => s.AgentStatus));
        Assert.Equal(new[] { "codex", "sleep", "tests" }, subs.Select(s => s.Label));
        Assert.Equal(new[] { 1, 0, 0 }, subs.Select(s => s.SubAgentCount));
        Assert.Equal(new[] { false, false, true }, subs.Select(s => s.HasPane));
    }

    [Fact]
    public void EachSubAgentsBadgeCoversItsDescendants()
    {
        var subs = SubAgentTree.Build(Snapshot.Agents[0], Snapshot, SubAgentTree.Parse(Core));
        // s1's only child s2 has no status yet; the others have no children.
        Assert.Equal(new string?[] { "unknown", null, null }, subs.Select(s => s.SubAgentStatus));
    }

    [Fact]
    public async Task ParentBadgeTakesTheMostUrgentStatusInTheFamily()
    {
        // The parent works; a kept child finished unseen, so the badge is blue.
        const string core = """
            {"terminals":[
              {"address":"a1","agent":"claude-code","state":"live","status":"working"},
              {"address":"k1","agent":"codex","parent":"a1","state":"live","status":"done"},
              {"address":"k2","agent":"codex","parent":"a1","state":"live","status":"working"}]}
            """;
        var parent = new Agent { TerminalId = "a1", AgentStatus = "working", SubAgents = ["k1", "k2"] };
        var snapshot = new HerdrApp.Snapshot
        {
            Spaces = [new Space { SpaceId = "s", Tabs = [new Tab { TabId = "t", Panes = [new Pane { PaneId = "p1", TerminalId = "k1" }] }] }],
            Agents = [parent, new Agent { TerminalId = "k1", PaneId = "p1", AgentStatus = "done" }],
        };
        var tree = new SubAgentTree(() => Task.FromResult(core));
        await tree.RefreshAsync();
        Assert.Equal("done", tree.BadgeStatus(parent, snapshot));
        Assert.Null(tree.BadgeStatus(new Agent { TerminalId = "lonely" }, snapshot));
    }

    [Fact]
    public void BeforeTheCoreAnswersTheDirectSubAgentsFromLsAreShown()
    {
        var subs = SubAgentTree.Build(Snapshot.Agents[0], Snapshot, []);
        Assert.Equal(new[] { "s1", "s3" }, subs.Select(s => s.Address));
        Assert.All(subs, s => Assert.Equal("unknown", s.AgentStatus));
    }

    [Fact]
    public void RemoteBareParentIsQualifiedWithItsMachine()
    {
        var parent = new Agent { TerminalId = "vps/t1", Machine = "vps", SubAgents = ["vps/t2"] };
        var subs = SubAgentTree.Build(parent, HerdrApp.Snapshot.Empty, SubAgentTree.Parse(Core));
        var sub = Assert.Single(subs);
        Assert.Equal(("vps/t2", "working", true), (sub.Address, sub.AgentStatus, sub.Remote));
    }

    [Fact]
    public async Task ReadRefreshesInTheBackgroundAndNotifies()
    {
        var calls = 0;
        var tree = new SubAgentTree(() => { Interlocked.Increment(ref calls); return Task.FromResult(Core); });
        var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        tree.Changed += () => changed.TrySetResult();
        Assert.All(tree.Read(Snapshot.Agents[0], Snapshot), s => Assert.Equal("unknown", s.AgentStatus));
        await changed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(3, tree.Read(Snapshot.Agents[0], Snapshot).Count);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task LsCarriesDirectSubAgentsAndPeekIsUsedWithoutAPane()
    {
        using var app = new FakeApp
        {
            Ls = """{"spaces":[{"id":"s","name":"x","machine":null,"active":true,"tabs":[{"id":"t","name":"1","active":true,"settled":false,"terminals":[{"terminal":"a1","pane":"p1","status":"working","app":"claude","name":null,"sub_agents":["s1","s3"]}]}]}]}""",
        };
        var client = app.Client();
        var snapshot = await client.GetSnapshotAsync();
        Assert.Equal(new[] { "s1", "s3" }, snapshot.Agents[0].SubAgents);
        await client.FocusSubAgentAsync(new SubAgent { Address = "s1" });
        await client.FocusSubAgentAsync(new SubAgent { Address = "s3", HasPane = true });
        Assert.Equal(new[] { ("terminal.peek", """{"terminal":"s1"}"""), ("pane.focus", """{"terminal":"s3"}""") }, app.Commands);
    }
}
