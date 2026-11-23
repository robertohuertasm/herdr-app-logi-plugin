namespace HerdrApp.Tests;

public class AgentHierarchyTests
{
    private static Snapshot List(params Agent[] agents) => new()
    {
        Agents = agents,
        Recent = agents.Reverse().ToArray(),
    };

    [Fact]
    public void FolderShowsParentsAndIndependentAgentsWhileRecentKeepsEveryAgent()
    {
        var snapshot = List(
            new Agent { TerminalId = "local" },
            new Agent { TerminalId = "remote/orchestrator", SubAgents = ["remote/10072", "remote/8086", "remote/10065"] },
            new Agent { TerminalId = "remote/10072" },
            new Agent { TerminalId = "remote/8086" },
            new Agent { TerminalId = "remote/10065" },
            new Agent { TerminalId = "remote/9750" });

        Assert.Equal(new[] { "remote/9750", "remote/orchestrator", "local" },
            SubAgentTree.TopLevel(snapshot).Select(a => a.Key));
        Assert.Equal(6, snapshot.Agents.Count);
        Assert.Equal(new[] { "remote/9750", "remote/10065", "remote/8086", "remote/10072", "remote/orchestrator", "local" },
            Enumerable.Range(1, 6).Select(slot => FixedSlots.Recent(snapshot, slot)!.Key));
    }

    [Fact]
    public void ChildrenWithoutAParentInTheWindowStayAccessibleAtTopLevel()
    {
        var snapshot = List(new Agent { TerminalId = "child" });
        Assert.Equal(snapshot.Recent, SubAgentTree.TopLevel(snapshot));
    }

    [Fact]
    public void DescendantsAppearOnlyUnderTheTopmostVisibleParent()
    {
        var snapshot = List(
            new Agent { TerminalId = "parent", SubAgents = ["child", "unkept"] },
            new Agent { TerminalId = "child", SubAgents = ["grandchild"] },
            new Agent { TerminalId = "grandchild" });

        Assert.Equal("parent", Assert.Single(SubAgentTree.TopLevel(snapshot)).Key);
    }

    [Fact]
    public void RemovingTheParentMakesItsChildAFolderRoot()
    {
        var child = new Agent { TerminalId = "child", SubAgents = ["grandchild"] };
        var grandchild = new Agent { TerminalId = "grandchild" };
        var before = List(new Agent { TerminalId = "parent", SubAgents = ["child"] }, child, grandchild);
        Assert.Equal("parent", Assert.Single(SubAgentTree.TopLevel(before)).Key);
        Assert.Equal("child", Assert.Single(SubAgentTree.TopLevel(List(child, grandchild))).Key);
    }

    [Fact]
    public void MachineQualifiedIdsDoNotHideAnUnrelatedLocalAgent()
    {
        var snapshot = List(
            new Agent { TerminalId = "t2" },
            new Agent { TerminalId = "remote/t1", SubAgents = ["remote/t2"] },
            new Agent { TerminalId = "remote/t2" });

        Assert.Equal(new[] { "remote/t1", "t2" }, SubAgentTree.TopLevel(snapshot).Select(a => a.Key));
    }

    [Fact]
    public void MalformedCyclesKeepAnEntryPointAlongsideOrdinaryRoots()
    {
        var snapshot = List(
            new Agent { TerminalId = "ordinary" },
            new Agent { TerminalId = "a", SubAgents = ["b"] },
            new Agent { TerminalId = "b", SubAgents = ["a"] });

        Assert.Equal(new[] { "a", "ordinary" }, SubAgentTree.TopLevel(snapshot).Select(a => a.Key));
    }
}
