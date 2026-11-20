using HerdrApp;

// Read-only: asks the running window for ls and status, never runs a command.
var client = new HerdrAppClient();
var snapshot = await client.GetSnapshotAsync();
Console.WriteLine($"Socket: {client.SocketPath}");
Console.WriteLine($"Spaces: {snapshot.Spaces.Count}; tabs: {snapshot.Spaces.Sum(s => s.Tabs.Count)}; panes: {snapshot.Panes.Count()}; agents: {snapshot.Agents.Count}");
foreach (var group in snapshot.Agents.GroupBy(a => a.Machine ?? "Local"))
    Console.WriteLine($"{group.Key}: {group.Count()}");
Console.WriteLine("States: " + string.Join(", ", snapshot.Agents.GroupBy(a => a.AgentStatus).Select(g => $"{g.Key}={g.Count()}")));
Console.WriteLine("Read-only query; no command sent.");
