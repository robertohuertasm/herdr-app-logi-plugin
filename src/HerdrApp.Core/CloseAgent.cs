namespace HerdrApp;

public sealed record CloseTarget(string Key, string PaneId, string SpaceId, string? App);

public sealed class CloseAgent(HerdrAppClient client)
{
    // The app has no conditional close: check identity right before closing.
    public async Task<bool> CloseIfStillPresentAsync(CloseTarget target)
    {
        if (string.IsNullOrWhiteSpace(target.PaneId)) return false;
        var snapshot = await client.GetSnapshotAsync();
        var current = snapshot.Agents.FirstOrDefault(a => a.Key == target.Key);
        if (current is null || current.PaneId != target.PaneId || current.SpaceId != target.SpaceId || current.App != target.App) return false;
        await client.ClosePaneAsync(target.PaneId);
        return true;
    }
}
