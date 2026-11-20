namespace HerdrApp;

// Statuses are kept as strings: one the app gained after this plugin was written
// must reach the tiles, which show it as unknown, rather than fail to parse.
public static class AgentStatus
{
    public const string Idle = "idle";
    public const string Working = "working";
    public const string Blocked = "blocked";
    public const string Done = "done";
    public const string Unknown = "unknown";
}

public sealed record Pane
{
    public string PaneId { get; init; } = "";
    public string TerminalId { get; init; } = "";
    public string SpaceId { get; init; } = "";
    public string TabId { get; init; } = "";
}

public sealed record Tab
{
    public string TabId { get; init; } = "";
    public string SpaceId { get; init; } = "";
    // Position in its space, from 1.
    public int Index { get; init; }
    public string Name { get; init; } = "";
    public bool Active { get; init; }
    public bool Settled { get; init; }
    public IReadOnlyList<Pane> Panes { get; init; } = [];
}

public sealed record Space
{
    public string SpaceId { get; init; } = "";
    // Position in the app's spaces list, from 1.
    public int Index { get; init; }
    public string Name { get; init; } = "";
    // Null for this computer.
    public string? Machine { get; init; }
    public bool Active { get; init; }
    // Most urgent status of its agents, or "none" when it has none.
    public string AgentStatus { get; init; } = "none";
    public IReadOnlyList<Tab> Tabs { get; init; } = [];
    public string? ActiveTabId => Tabs.FirstOrDefault(t => t.Active)?.TabId;
}

// A terminal whose program reports a status. The terminal address is its key:
// remote ones carry a `<machine>/` prefix, so it is unique across machines.
public sealed record Agent
{
    public string Key => TerminalId;
    public string TerminalId { get; init; } = "";
    public string PaneId { get; init; } = "";
    public string SpaceId { get; init; } = "";
    public string SpaceName { get; init; } = "";
    public string TabId { get; init; } = "";
    public string? Machine { get; init; }
    public string? Name { get; init; }
    public string? App { get; init; }
    public string AgentStatus { get; init; } = HerdrApp.AgentStatus.Unknown;
}

public sealed record Snapshot
{
    public string? FocusedSpaceId { get; init; }
    public string? FocusedTabId { get; init; }
    public string? FocusedPaneId { get; init; }
    public IReadOnlyList<Space> Spaces { get; init; } = [];
    public IReadOnlyList<Agent> Agents { get; init; } = [];
    public IReadOnlyList<Agent> Recent { get; init; } = [];

    public IEnumerable<Pane> Panes => Spaces.SelectMany(s => s.Tabs).SelectMany(t => t.Panes);
    public Space? FocusedSpace => Spaces.FirstOrDefault(s => s.SpaceId == FocusedSpaceId);
    public Tab? FocusedTab => Spaces.SelectMany(s => s.Tabs).FirstOrDefault(t => t.TabId == FocusedTabId);

    public static readonly Snapshot Empty = new();
}

// `{"ls":true}` and `{"status":true}` as the app sends them.
internal sealed record RawLs
{
    public List<RawSpace> Spaces { get; init; } = [];
}

internal sealed record RawSpace
{
    public string Id { get; init; } = "";
    public string? Name { get; init; }
    public string? Machine { get; init; }
    public bool Active { get; init; }
    public List<RawTab> Tabs { get; init; } = [];
}

internal sealed record RawTab
{
    public string Id { get; init; } = "";
    public string? Name { get; init; }
    public bool Active { get; init; }
    public bool Settled { get; init; }
    public List<RawTerminal> Terminals { get; init; } = [];
}

internal sealed record RawTerminal
{
    public string Terminal { get; init; } = "";
    public string? Pane { get; init; }
    public string? Status { get; init; }
    public string? App { get; init; }
    public string? Name { get; init; }
}

internal sealed record RawStatus
{
    public string? Space { get; init; }
    public string? Tab { get; init; }
    public string? FocusedPane { get; init; }
}
