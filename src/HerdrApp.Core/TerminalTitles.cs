using System.Text.Json;

namespace HerdrApp;

// The app's `ls` has no terminal titles, yet an unnamed tab is best told apart
// by its agent's title ("Fix login"), as the app's own agents list does. Titles
// come from `hc get <address> --json`, only for agents that need one, refreshed
// in the background so polling never waits on a remote machine.
public sealed class TerminalTitles
{
    private static readonly TimeSpan MaxAge = TimeSpan.FromSeconds(5);
    private readonly Func<string, Task<string>> getTerminal;
    private readonly object gate = new();
    private Dictionary<string, string> titles = [];
    private Task? refresh;
    private DateTime refreshedAt = DateTime.MinValue;

    public TerminalTitles() : this(address => HcCli.RunAsync("get", address, "--json")) { }
    internal TerminalTitles(Func<string, Task<string>> getTerminal) => this.getTerminal = getTerminal;

    public IReadOnlyList<Agent> Apply(IReadOnlyList<Agent> agents)
    {
        var wanted = agents.Where(NeedsTitle).Select(a => a.Key).ToList();
        lock (gate)
        {
            if (wanted.Count > 0 && (refresh is null || refresh.IsCompleted) && DateTime.UtcNow - refreshedAt >= MaxAge)
            {
                refreshedAt = DateTime.UtcNow;
                refresh = Task.Run(() => RefreshAsync(wanted));
            }
            return agents.Select(a => titles.TryGetValue(a.Key, out var title) ? a with { Title = title } : a).ToList();
        }
    }

    internal async Task RefreshAsync(IReadOnlyList<string> addresses)
    {
        using var limit = new SemaphoreSlim(4);
        var found = await Task.WhenAll(addresses.Select(async address =>
        {
            await limit.WaitAsync();
            try
            {
                using var document = JsonDocument.Parse(await getTerminal(address));
                return document.RootElement.TryGetProperty("title", out var t) && t.ValueKind == JsonValueKind.String
                    ? (address, Clean(t.GetString())) : (address, null);
            }
            catch (Exception e)
            {
                Log.Info($"Could not read a terminal title: {e.Message}");
                return (address, (string?)null);
            }
            finally { limit.Release(); }
        }));
        lock (gate) titles = found.Where(f => f.Item2 is not null).ToDictionary(f => f.address, f => f.Item2!);
    }

    // Unnamed tabs go by their number; a tab named after its program says
    // nothing either. Those agents are told apart by their title.
    public static bool InformativeTabName(Agent agent) =>
        !string.IsNullOrWhiteSpace(agent.TabName) && !agent.TabName.All(char.IsAsciiDigit)
        && Normalize(agent.TabName) != Normalize(agent.App);

    private static bool NeedsTitle(Agent agent) => string.IsNullOrWhiteSpace(agent.Name) && !InformativeTabName(agent);

    private static string Normalize(string? text) =>
        new string((text ?? "").Where(char.IsAsciiLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    // Titles carry a status glyph in front and often " | <project>" after.
    internal static string? Clean(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return null;
        var start = 0;
        while (start < title.Length && !char.IsLetterOrDigit(title[start]) && title[start] is not ('~' or '/')) start++;
        var text = title[start..];
        var separator = text.LastIndexOf(" | ", StringComparison.Ordinal);
        if (separator > 0) text = text[..separator];
        text = text.Trim();
        // A path or user@host is where the shell is, not what the agent does.
        return text.Length == 0 || text[0] is '~' or '/' || text.Contains('@') ? null : text;
    }
}
