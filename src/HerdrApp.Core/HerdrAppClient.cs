using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace HerdrApp;

public sealed class HerdrAppException(string message) : Exception(message);

// Talks to the running Herdr app window over its control socket, the same one
// `herdr-app ls`, `status` and `do` use: one JSON line each way per connection.
public sealed class HerdrAppClient
{
    public static readonly string DefaultSocketPath =
        Environment.GetEnvironmentVariable("HERDR_APP_SOCKET_PATH")
        ?? Path.Combine(
            Environment.GetEnvironmentVariable("HERDR_APP_HOME")
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config/herdr-app"),
            "herdr-app.sock");

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(3);
    // Starting a terminal waits for the machine's core, which can be remote.
    private static readonly TimeSpan CreateTimeout = TimeSpan.FromSeconds(10);

    internal static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private readonly RecencyTracker recency = new();
    private readonly AppWindow window;
    private readonly SemaphoreSlim agentFocusGate = new(1, 1);
    private string? pendingAgentKey;

    public HerdrAppClient(string? socketPath = null, AppWindow? window = null)
    {
        SocketPath = socketPath ?? DefaultSocketPath;
        this.window = window ?? new AppWindow();
    }

    public string SocketPath { get; }
    public string? PendingAgentKey => Volatile.Read(ref pendingAgentKey);
    public event Action? AgentFocusChanged;

    public async Task<JsonElement> RequestAsync(object request, string what, TimeSpan? timeout = null)
    {
        using var cts = new CancellationTokenSource(timeout ?? RequestTimeout);
        using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        string? line;
        try
        {
            await socket.ConnectAsync(new UnixDomainSocketEndPoint(SocketPath), cts.Token);
            await using var stream = new NetworkStream(socket, ownsSocket: false);
            await stream.WriteAsync(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request) + "\n"), cts.Token);
            // The reader decodes as a stream, so a multi-byte character split
            // across reads survives.
            using var reader = new StreamReader(stream, Encoding.UTF8);
            line = await reader.ReadLineAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            throw new HerdrAppException($"herdr-app {what}: timed out");
        }
        if (line is null) throw new HerdrAppException($"herdr-app {what}: connection closed before a response arrived");

        JsonElement message;
        try
        {
            using var document = JsonDocument.Parse(line);
            message = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            throw new HerdrAppException($"herdr-app {what}: malformed response");
        }
        if (message.ValueKind == JsonValueKind.Object && message.TryGetProperty("error", out var error) && error.ValueKind != JsonValueKind.Null)
        {
            throw new HerdrAppException($"herdr-app {what}: {(error.ValueKind == JsonValueKind.String ? error.GetString() : error.GetRawText())}");
        }
        return message;
    }

    // Runs an app command, as `herdr-app do <name>` does.
    public Task<JsonElement> DoAsync(string name, object? args = null, TimeSpan? timeout = null) =>
        RequestAsync(new { @do = name, args = args ?? new { } }, name, timeout);

    internal Task<JsonElement> DoCreateAsync(string name, object? args = null) => DoAsync(name, args, CreateTimeout);

    public async Task<Snapshot> GetSnapshotAsync()
    {
        var lsTask = RequestAsync(new { ls = true }, "ls");
        var statusTask = RequestAsync(new { status = true }, "status");
        await Task.WhenAll(lsTask, statusTask);
        var ls = (await lsTask).Deserialize<RawLs>(Json) ?? throw new HerdrAppException("herdr-app ls: malformed response");
        var status = (await statusTask).Deserialize<RawStatus>(Json) ?? throw new HerdrAppException("herdr-app status: malformed response");
        return Build(ls, status, recency);
    }

    internal static Snapshot Build(RawLs ls, RawStatus status, RecencyTracker? recency = null)
    {
        var spaces = new List<Space>();
        var agents = new List<Agent>();
        foreach (var (rawSpace, spaceIndex) in ls.Spaces.Select((s, i) => (s, i + 1)))
        {
            var machine = string.IsNullOrEmpty(rawSpace.Machine) ? null : rawSpace.Machine;
            var spaceAgents = new List<Agent>();
            var tabs = new List<Tab>();
            foreach (var (rawTab, tabIndex) in rawSpace.Tabs.Select((t, i) => (t, i + 1)))
            {
                var panes = new List<Pane>();
                foreach (var terminal in rawTab.Terminals)
                {
                    if (string.IsNullOrEmpty(terminal.Pane)) continue;
                    panes.Add(new Pane { PaneId = terminal.Pane, TerminalId = terminal.Terminal, SpaceId = rawSpace.Id, TabId = rawTab.Id });
                    if (terminal.Status is null) continue;
                    spaceAgents.Add(new Agent
                    {
                        TerminalId = terminal.Terminal,
                        PaneId = terminal.Pane,
                        SpaceId = rawSpace.Id,
                        SpaceName = rawSpace.Name ?? "",
                        TabId = rawTab.Id,
                        Machine = machine,
                        Name = terminal.Name,
                        App = terminal.App,
                        AgentStatus = terminal.Status,
                        SubAgents = terminal.SubAgents,
                    });
                }
                tabs.Add(new Tab
                {
                    TabId = rawTab.Id, SpaceId = rawSpace.Id, Index = tabIndex, Name = rawTab.Name ?? "",
                    Active = rawTab.Active, Settled = rawTab.Settled, Panes = panes,
                });
            }
            spaces.Add(new Space
            {
                SpaceId = rawSpace.Id, Index = spaceIndex, Name = rawSpace.Name ?? "", Machine = machine,
                Active = rawSpace.Active, Tabs = tabs,
                AgentStatus = AgentPriority.MostUrgent(spaceAgents)?.AgentStatus ?? "none",
            });
            agents.AddRange(spaceAgents);
        }
        return new Snapshot
        {
            FocusedSpaceId = status.Space ?? spaces.FirstOrDefault(s => s.Active)?.SpaceId,
            FocusedTabId = status.Tab,
            FocusedPaneId = status.FocusedPane,
            Spaces = spaces,
            Agents = agents,
            Recent = (recency ?? new RecencyTracker()).Rank(agents),
        };
    }

    // Exposed so a tile can bring the window forward before other async work.
    public Task ActivateAppAsync() => window.ActivateAsync();

    public async Task FocusAgentAsync(string agentKey)
    {
        // Presses are dropped, not queued, while a navigation is in flight.
        if (!await agentFocusGate.WaitAsync(0))
        {
            Log.Info("Agent navigation in progress; additional press ignored");
            return;
        }
        try
        {
            SetPendingAgent(agentKey);
            _ = window.ActivateAsync();
            await DoAsync("agent.focus", new { terminal = agentKey });
        }
        finally { SetPendingAgent(null); agentFocusGate.Release(); }
    }

    private void SetPendingAgent(string? key)
    {
        if (Volatile.Read(ref pendingAgentKey) == key) return;
        Volatile.Write(ref pendingAgentKey, key);
        try { AgentFocusChanged?.Invoke(); }
        catch (Exception e) { Log.Warning($"Could not update pending agent display: {e.Message}"); }
    }

    // A kept sub-agent has a pane to focus; others are peeked at over the tab,
    // as the app's agents list does.
    public async Task FocusSubAgentAsync(SubAgent subAgent)
    {
        _ = window.ActivateAsync();
        if (subAgent.HasPane) await DoAsync("pane.focus", new { terminal = subAgent.Address });
        else await DoAsync("terminal.peek", new { terminal = subAgent.Address });
    }

    public async Task FocusPaneAsync(string paneId)
    {
        _ = window.ActivateAsync();
        await DoAsync("pane.focus", new { pane = paneId });
    }

    public async Task FocusSpaceAsync(string spaceId)
    {
        _ = window.ActivateAsync();
        await DoAsync("space.select", new { id = spaceId });
    }

    internal Task ClosePaneAsync(string paneId) => DoAsync("pane.close", new { pane = paneId });

    public static string? NextPaneInFocusedSpace(Snapshot snapshot, string spaceId)
    {
        if (snapshot.FocusedSpaceId != spaceId) return null;
        var space = snapshot.Spaces.FirstOrDefault(s => s.SpaceId == spaceId);
        if (space is null) return null;
        var tabId = snapshot.FocusedTabId ?? space.ActiveTabId;
        var tab = space.Tabs.FirstOrDefault(t => t.TabId == tabId);
        if (tab is null || tab.Panes.Count < 2) return null;
        var index = tab.Panes.ToList().FindIndex(p => p.PaneId == snapshot.FocusedPaneId);
        return tab.Panes[(index + 1) % tab.Panes.Count].PaneId;
    }
}
