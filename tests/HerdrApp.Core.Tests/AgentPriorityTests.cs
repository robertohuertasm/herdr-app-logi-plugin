namespace HerdrApp.Tests;

public class AgentPriorityTests
{
    [Theory]
    [InlineData("blocked", "done")]
    [InlineData("done", "working")]
    [InlineData("working", "idle")]
    [InlineData("idle", "unknown")]
    [InlineData("idle", "future-status")]
    public void HigherPriorityWinsEvenIfLessRecent(string urgent, string lessUrgent)
    {
        var recent = new Agent { TerminalId = "recent", AgentStatus = lessUrgent };
        var important = new Agent { TerminalId = "important", AgentStatus = urgent };
        Assert.Same(important, AgentPriority.MostUrgent([recent, important]));
    }

    [Fact]
    public void EqualPriorityPreservesRecencyAndEmptyListHasNoAgent()
    {
        var recent = new Agent { TerminalId = "recent", AgentStatus = "working" };
        var old = new Agent { TerminalId = "old", AgentStatus = "working" };
        Assert.Same(recent, AgentPriority.MostUrgent([recent, old]));
        Assert.Null(AgentPriority.MostUrgent([]));
    }
}
