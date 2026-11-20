using System.Diagnostics;
using System.Text.Json;

namespace HerdrApp;

// A terminal an agent started with `hc run`, shown under its parent. One with a
// pane (kept) is focused; one without is peeked at over the tab.
public sealed record SubAgent
{
    public string Address { get; init; } = "";
    public string Label { get; init; } = "";
    public string AgentStatus { get; init; } = HerdrApp.AgentStatus.Unknown;
    public bool HasPane { get; init; }
    public bool Remote { get; init; }
    public int SubAgentCount { get; init; }
}

// The app's `ls` lists only each terminal's direct sub-agent addresses. Their
// status, program and own sub-agents come from the core: `hc ls --json`, which
// covers every connected machine. It runs only while a sub-agent view asks.
public sealed class SubAgentTree
{
    private static readonly TimeSpan MaxAge = TimeSpan.FromSeconds(2);
    private readonly Func<Task<string>> listTerminals;
    private readonly object gate = new();
    private IReadOnlyList<CoreTerminal> terminals = [];
    private Task? refresh;
    private DateTime refreshedAt = DateTime.MinValue;

    public SubAgentTree() : this(() => HcCli.RunAsync("ls", "--json")) { }
    internal SubAgentTree(Func<Task<string>> listTerminals) => this.listTerminals = listTerminals;

    public event Action? Changed;

    // Returns what is known now and refreshes in the background when stale.
    public IReadOnlyList<SubAgent> Read(Agent parent, Snapshot snapshot)
    {
        lock (gate)
        {
            if ((refresh is null || refresh.IsCompleted) && DateTime.UtcNow - refreshedAt >= MaxAge)
            {
                refreshedAt = DateTime.UtcNow;
                refresh = Task.Run(RefreshAsync);
            }
            return Build(parent, snapshot, terminals);
        }
    }

    public async Task RefreshAsync()
    {
        try
        {
            var next = Parse(await listTerminals());
            lock (gate) terminals = next;
            Changed?.Invoke();
        }
        catch (Exception e) { Log.Warning($"Could not list sub-agents: {e.Message}"); }
    }

    internal static IReadOnlyList<CoreTerminal> Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("terminals", out var list) || list.ValueKind != JsonValueKind.Array) return [];
        string? Read(JsonElement e, string key) =>
            e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        return list.EnumerateArray()
            .Where(t => Read(t, "address") is not null && Read(t, "state") is null or "live")
            .Select(t => new CoreTerminal(Read(t, "address")!, Read(t, "parent"), Read(t, "status"), Read(t, "agent"), Read(t, "name")))
            .ToList();
    }

    // Depth first, like the app's agents list. Before the core has answered,
    // the direct sub-agents from `ls` are shown without a status.
    internal static IReadOnlyList<SubAgent> Build(Agent parent, Snapshot snapshot, IReadOnlyList<CoreTerminal> core)
    {
        var withPane = snapshot.Panes.Select(p => p.TerminalId).ToHashSet();
        var agents = snapshot.Agents.ToDictionary(a => a.Key);
        var byAddress = core.ToDictionary(t => t.Address);
        var result = new List<SubAgent>();
        var seen = new HashSet<string> { parent.Key };

        IEnumerable<string> Children(string address) => core.Where(t => ParentOf(t) == address).Select(t => t.Address);

        void Add(string address)
        {
            if (!seen.Add(address)) return;
            byAddress.TryGetValue(address, out var terminal);
            agents.TryGetValue(address, out var agent);
            var children = terminal is null ? new List<string>() : Children(address).ToList();
            result.Add(new SubAgent
            {
                Address = address,
                Label = FirstNonBlank(agent?.Name, terminal?.Name, terminal?.Program, agent?.App) ?? address,
                AgentStatus = agent?.AgentStatus ?? StatusOf(terminal?.Status),
                HasPane = withPane.Contains(address),
                Remote = parent.Machine is not null,
                SubAgentCount = children.Count,
            });
            foreach (var child in children) Add(child);
        }

        var direct = Children(parent.Key).ToList();
        foreach (var address in direct.Count > 0 ? direct : parent.SubAgents) Add(address);
        return result;
    }

    // Remote cores may report a bare parent ID; qualify it like the address.
    private static string? ParentOf(CoreTerminal t)
    {
        if (t.Parent is null || t.Parent.Contains('/')) return t.Parent;
        var slash = t.Address.LastIndexOf('/');
        return slash < 0 ? t.Parent : t.Address[..(slash + 1)] + t.Parent;
    }

    // As the app's agents list: an error needs you, and a sub-agent is never
    // "done" on its own: it reports to its parent, whose done is the news.
    private static string StatusOf(string? status) => status switch
    {
        null => AgentStatus.Unknown,
        "error" => AgentStatus.Blocked,
        AgentStatus.Done => AgentStatus.Idle,
        _ => status,
    };

    private static string? FirstNonBlank(params string?[] values) => values.FirstOrDefault(s => !string.IsNullOrWhiteSpace(s));
}

internal sealed record CoreTerminal(string Address, string? Parent, string? Status, string? Program, string? Name);

// herdr-core's CLI. The Plugin Service does not inherit a login shell's PATH.
public static class HcCli
{
    public static string Path()
    {
        var configured = Environment.GetEnvironmentVariable("HERDR_HC_PATH");
        if (!string.IsNullOrWhiteSpace(configured)) return configured;
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string[] candidates = [System.IO.Path.Combine(home, ".local/bin/hc"), "/opt/homebrew/bin/hc", "/usr/local/bin/hc"];
        return candidates.FirstOrDefault(File.Exists) ?? "hc";
    }

    public static async Task<string> RunAsync(params string[] args)
    {
        var start = new ProcessStartInfo(Path()) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("hc did not start");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var error = process.StandardError.ReadToEndAsync(timeout.Token);
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            process.Kill();
            throw new HerdrAppException("hc timed out");
        }
        if (process.ExitCode != 0) throw new HerdrAppException($"hc exited with {process.ExitCode}: {(await error).Trim()}");
        return await output;
    }
}
