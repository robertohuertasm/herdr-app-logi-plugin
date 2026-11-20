namespace HerdrApp;

// Each button owns its own confirmation. Confirmation is consumed before any I/O.
public sealed class ClosePaneConfirmation(HerdrAppClient client, TimeProvider? clock = null)
{
    private readonly object sync = new();
    private readonly TimeProvider clock = clock ?? TimeProvider.System;
    private Pane? target;
    private long armedAt;
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(3);

    public bool IsArmed
    {
        get { lock (this.sync) return this.target is not null && this.clock.GetElapsedTime(this.armedAt) < Window; }
    }

    public bool Arm(Snapshot snapshot)
    {
        var pane = snapshot.Panes.FirstOrDefault(p => p.PaneId == snapshot.FocusedPaneId);
        if (pane is null || string.IsNullOrWhiteSpace(pane.PaneId) || string.IsNullOrWhiteSpace(pane.TerminalId) ||
            string.IsNullOrWhiteSpace(pane.SpaceId) || string.IsNullOrWhiteSpace(pane.TabId)) return false;
        lock (this.sync)
        {
            this.target = pane;
            this.armedAt = this.clock.GetTimestamp();
        }
        return true;
    }

    public void Cancel() { lock (this.sync) this.target = null; }

    public bool Expire()
    {
        lock (this.sync)
        {
            if (this.target is null || this.clock.GetElapsedTime(this.armedAt) < Window) return false;
            this.target = null;
            return true;
        }
    }

    public async Task<bool> ConfirmAsync()
    {
        Pane? selected;
        lock (this.sync)
        {
            selected = this.target is not null && this.clock.GetElapsedTime(this.armedAt) < Window ? this.target : null;
            this.target = null;
        }
        if (selected is null) return false;
        var current = await client.GetSnapshotAsync();
        if (current.FocusedPaneId != selected.PaneId || !current.Panes.Contains(selected)) return false;
        await client.ClosePaneAsync(selected.PaneId);
        return true;
    }
}
