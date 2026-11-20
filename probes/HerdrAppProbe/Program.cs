using HerdrApp;

// Read-only: asks the running window for ls and status, never runs a command.
var client = new HerdrAppClient();
var snapshot = await client.GetSnapshotAsync();
Console.WriteLine($"Socket: {client.SocketPath}");
Console.WriteLine($"Spaces: {snapshot.Spaces.Count}; tabs: {snapshot.Spaces.Sum(s => s.Tabs.Count)}; panes: {snapshot.Panes.Count()}; agents: {snapshot.Agents.Count}");
foreach (var group in snapshot.Agents.GroupBy(a => a.Machine ?? "Local"))
    Console.WriteLine($"{group.Key}: {group.Count()}");
Console.WriteLine("States: " + string.Join(", ", snapshot.Agents.GroupBy(a => a.AgentStatus).Select(g => $"{g.Key}={g.Count()}")));
if (args is ["--labels"])
{
    // Titles arrive in the background; poll until the lookup has answered.
    for (var i = 0; i < 30; i++)
    {
        snapshot = await client.GetSnapshotAsync();
        if (snapshot.Agents.All(a => a.Title is not null || !string.IsNullOrWhiteSpace(a.Name) || TerminalTitles.InformativeTabName(a))) break;
        await Task.Delay(200);
    }
    foreach (var (agent, index) in snapshot.Recent.Select((a, i) => (a, i + 1)))
        Console.WriteLine($"{index}. {FixedSlots.AgentLabel(agent, index)}  ({agent.Key})");
}
if (args is ["--sub-agents"])
{
    var tree = new SubAgentTree();
    await tree.RefreshAsync();
    foreach (var agent in snapshot.Agents)
        Console.WriteLine($"{agent.Key}: ls lists {agent.SubAgents.Count}; tree has {tree.Read(agent, snapshot).Count}");
}
Console.WriteLine("Read-only query; no command sent.");
