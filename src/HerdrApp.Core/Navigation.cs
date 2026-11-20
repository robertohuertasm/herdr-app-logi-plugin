namespace HerdrApp;

public sealed record Shortcut(string Id, string Label, string Group);

public sealed class Navigation(HerdrAppClient client)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    public static readonly Shortcut[] All =
    [
        new("zoom", "Zoom pane", "Main"), new("cycle", "Cycle panes", "Main"),
        new("cycle-tabs", "Cycle tabs", "Main"),
        new("go-to", "Go to", "Main"),
        new("split-right", "Split right", "Layout"), new("split-down", "Split down", "Layout"),
        new("pane-prev", "Previous pane", "Layout"), new("pane-next", "Next pane", "Layout"),
        new("close-pane", "Close pane", "Layout"),
        new("tab-new", "New tab", "Tabs"), new("tab-prev", "Previous tab", "Tabs"), new("tab-next", "Next tab", "Tabs"),
        new("space-new", "New space", "Spaces"), new("space-prev", "Previous space", "Spaces"), new("space-next", "Next space", "Spaces"),
    ];

    // The app's own picker mod: "Go to a tab", bound to cmd-p by default.
    public const string GoToMod = "picker";

    public async Task ExecuteAsync(string id)
    {
        if (id == "close-pane") throw new InvalidOperationException("Close pane requires a long press and confirmation");
        if (!All.Any(a => a.Id == id)) throw new ArgumentException("Unknown shortcut", nameof(id));
        await this.gate.WaitAsync();
        try
        {
            _ = client.ActivateAppAsync();
            switch (id)
            {
                case "zoom": await client.DoAsync("pane.zoom"); break;
                case "go-to": await client.DoAsync("mod.open", new { name = GoToMod }); break;
                case "cycle-tabs" or "tab-next": await client.DoAsync("tab.next"); break;
                case "tab-prev": await client.DoAsync("tab.prev"); break;
                case "space-next": await client.DoAsync("space.next"); break;
                case "space-prev": await client.DoAsync("space.prev"); break;
                case "split-right" or "split-down":
                    await client.DoCreateAsync("pane.split", new { direction = id == "split-right" ? "right" : "down" });
                    break;
                case "tab-new": await client.DoCreateAsync("tab.new"); break;
                case "space-new":
                {
                    // Like the app's own key: follow the focused space's machine.
                    // Scripts otherwise default to this computer.
                    var s = await client.GetSnapshotAsync();
                    await client.DoCreateAsync("space.new", new { machine = s.FocusedSpace?.Machine ?? "" });
                    break;
                }
                case "cycle" or "pane-next" or "pane-prev":
                {
                    var s = await client.GetSnapshotAsync();
                    var tab = s.FocusedTab;
                    var target = tab is null ? null : Neighbor(tab.Panes.Select(p => p.PaneId).ToList(), s.FocusedPaneId, id == "pane-prev" ? -1 : 1);
                    if (target is not null)
                    {
                        await client.FocusPaneAsync(target);
                        Log.Info("Pane navigation: focused the next pane in the current tab");
                    }
                    else Log.Info("Pane navigation: current tab has no other pane to focus");
                    break;
                }
            }
        }
        finally { this.gate.Release(); }
    }

    public static string? Neighbor(IReadOnlyList<string> items, string? current, int direction)
    {
        var index = current is null ? -1 : items.ToList().IndexOf(current);
        return index < 0 || items.Count < 2 ? null : items[(index + direction + items.Count) % items.Count];
    }
}
