using System.Net.Sockets;
using System.Text;
using System.Text.Json;

// The tests swap the static Log sinks, so they must not run concurrently.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace HerdrApp.Tests;

// Collects what the code under test logs, and restores the console sinks after.
sealed class LogCapture : IDisposable
{
    public List<string> Infos { get; } = [];
    public List<string> Warnings { get; } = [];
    public List<string> Errors { get; } = [];

    public LogCapture()
    {
        Log.Info = Infos.Add;
        Log.Warning = Warnings.Add;
        Log.Error = Errors.Add;
    }

    public void Dispose()
    {
        Log.Info = Console.WriteLine;
        Log.Warning = Console.Error.WriteLine;
        Log.Error = Console.Error.WriteLine;
    }
}

// A stand-in Herdr app window on a scratch socket. It answers `ls` and
// `status` from the properties and records every `do` command.
sealed class FakeApp : IDisposable
{
    private readonly Socket listener = new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
    private readonly CancellationTokenSource stop = new();
    private readonly List<(string Name, string Args)> commands = [];

    public FakeApp()
    {
        Directory = System.IO.Directory.CreateTempSubdirectory("herdr-app-logi-").FullName;
        SocketPath = Path.Combine(Directory, "a.sock");
        listener.Bind(new UnixDomainSocketEndPoint(SocketPath));
        listener.Listen();
        _ = AcceptAsync();
    }

    public string Directory { get; }
    public string SocketPath { get; }
    public string Ls { get; set; } = """{"spaces":[],"recent":[]}""";
    public string Status { get; set; } = """{"errors":[]}""";
    // Gets each command and its args; returns the reply line.
    public Func<string, JsonElement, string> OnDo { get; set; } = (_, _) => "null";

    public IReadOnlyList<(string Name, string Args)> Commands
    {
        get { lock (commands) return commands.ToList(); }
    }

    public IReadOnlyList<string> Names => Commands.Select(c => c.Name).ToList();

    public HerdrAppClient Client() => new(SocketPath, new NoWindow());

    private async Task AcceptAsync()
    {
        while (!stop.IsCancellationRequested)
        {
            Socket connection;
            try { connection = await listener.AcceptAsync(stop.Token); }
            catch (Exception) { return; }
            _ = ServeAsync(connection);
        }
    }

    private async Task ServeAsync(Socket connection)
    {
        using (connection)
        {
            var buffer = new List<byte>();
            var chunk = new byte[4096];
            while (true)
            {
                var read = await connection.ReceiveAsync(chunk);
                if (read == 0) return;
                buffer.AddRange(chunk.AsSpan(0, read));
                var end = buffer.IndexOf((byte)'\n');
                if (end == -1) continue;
                var reply = Answer(Encoding.UTF8.GetString(buffer.GetRange(0, end).ToArray()));
                await connection.SendAsync(Encoding.UTF8.GetBytes(reply + "\n"));
                connection.Shutdown(SocketShutdown.Both);
                return;
            }
        }
    }

    private string Answer(string line)
    {
        using var request = JsonDocument.Parse(line);
        var root = request.RootElement;
        // The app answers on one line; fixtures are written over several.
        if (root.TryGetProperty("ls", out _)) return Compact(Ls);
        if (root.TryGetProperty("status", out _)) return Compact(Status);
        var name = root.GetProperty("do").GetString()!;
        var args = root.GetProperty("args");
        lock (commands) commands.Add((name, args.GetRawText()));
        return OnDo(name, args);
    }

    private static string Compact(string json)
    {
        using var document = JsonDocument.Parse(json);
        return JsonSerializer.Serialize(document.RootElement);
    }

    public void Dispose()
    {
        stop.Cancel();
        listener.Dispose();
        System.IO.Directory.Delete(Directory, recursive: true);
    }
}

sealed class NoWindow : AppWindow
{
    protected override Task RunAsync(string command, params string[] args) => Task.CompletedTask;
}

static class Fixtures
{
    // Two local spaces and one remote: s1 has a tab with an agent and a shell,
    // and a second tab; s2 has a blocked agent; s3 lives on machine "vps".
    public const string Ls = """
        {"spaces":[
          {"id":"s1","name":"api","folder":"/code/api","machine":null,"active":true,"tabs":[
            {"id":"t1","name":"1","active":true,"settled":false,"terminals":[
              {"terminal":"a1","pane":"p1","status":"working","app":"claude","name":null,"sub_agents":[]},
              {"terminal":"sh1","pane":"p2","status":null,"app":null,"name":null,"sub_agents":[]}]},
            {"id":"t2","name":"2","active":false,"settled":false,"terminals":[
              {"terminal":"sh2","pane":"p3","status":null,"app":null,"name":null,"sub_agents":[]}]}]},
          {"id":"s2","name":"web","folder":"","machine":null,"active":false,"tabs":[
            {"id":"t3","name":"1","active":true,"settled":false,"terminals":[
              {"terminal":"a2","pane":"p4","status":"blocked","app":"codex","name":"Fix login","sub_agents":[]}]}]},
          {"id":"vps/s3","name":"","folder":"","machine":"vps","active":false,"tabs":[
            {"id":"vps/t4","name":"1","active":true,"settled":false,"terminals":[
              {"terminal":"vps/t9","pane":"vps/p5","status":"idle","app":"pi","name":null,"sub_agents":[]}]}]}],
         "recent":[]}
        """;

    public const string Status = """{"space":"s1","tab":"t1","focused_pane":"p1","agent":"a1","overlays":[],"errors":[]}""";
}
