namespace HerdrApp;

public static class FixedSlots
{
    public const int Count = 9;

    public static Agent? Recent(Snapshot snapshot, int slot) =>
        slot is >= 1 and <= Count && slot <= snapshot.Recent.Count ? snapshot.Recent[slot - 1] : null;

    public static Space? Space(Snapshot snapshot, int slot) =>
        slot is >= 1 and <= Count ? snapshot.Spaces.FirstOrDefault(s => s.Index == slot) : null;

    // The app reports no terminal title, so an unnamed agent goes by its space.
    public static string AgentLabel(Agent agent, int slot) =>
        FirstNonBlank(agent.Name, agent.SpaceName, agent.App) ?? $"Recent {slot}";

    public static string SpaceLabel(Space space, int slot) =>
        FirstNonBlank(space.Name) ?? $"Space {slot}";

    private static string? FirstNonBlank(params string?[] values) => values.FirstOrDefault(s => !string.IsNullOrWhiteSpace(s));
}

// Serializing presses ensures a second Space press sees the focus left by the first.
public sealed class FixedSlotActions(HerdrAppClient client)
{
    private readonly SemaphoreSlim gate = new(1, 1);

    public Task FocusRecentAsync(string agentKey) => client.FocusAgentAsync(agentKey);

    public async Task FocusSpaceAsync(int slot)
    {
        await this.gate.WaitAsync();
        try
        {
            var snapshot = await client.GetSnapshotAsync();
            var space = FixedSlots.Space(snapshot, slot);
            if (space is null) return;
            var paneId = HerdrAppClient.NextPaneInFocusedSpace(snapshot, space.SpaceId);
            if (paneId is not null) await client.FocusPaneAsync(paneId);
            else await client.FocusSpaceAsync(space.SpaceId);
        }
        finally { this.gate.Release(); }
    }
}
