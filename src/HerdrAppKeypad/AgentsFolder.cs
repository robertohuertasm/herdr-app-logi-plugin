namespace Loupedeck.HerdrAppKeypad;

using HerdrApp;

public sealed class AgentsFolder : PluginDynamicFolder
{
    private string[] names = [];
    private ShortcutMenu shortcuts = null!;
    private bool showingShortcuts;
    // The agent whose sub-agents are shown, opened by holding its key.
    private string? subAgentsOf;
    private IReadOnlyList<SubAgent> shownSubAgents = [];
    private CloseTarget? armed;
    private DateTime armedUntil;
    private readonly AgentFolderGesture gesture = new();
    private string? cyclePressed;
    private string? lastFolderStatus;
    public AgentsFolder()
    {
        this.DisplayName = "Herdr App";
        this.Description = "Live agents in Herdr App";
        this.GroupName = "Herdr App";
    }
    private HerdrAppKeypadPlugin Owner => (HerdrAppKeypadPlugin)this.Plugin;
    public override bool Load()
    {
        this.shortcuts = new ShortcutMenu(this.Owner, this.Changed, this.ReturnToAgents);
        this.Owner.Board.Changed += this.Changed;
        return true;
    }
    public override bool Unload() { this.shortcuts.Reset(); this.Owner.Board.Changed -= this.Changed; return true; }
    public override bool Activate() { this.Reset(); this.names = []; this.ButtonActionNamesChanged(); return true; }
    public override bool Deactivate() { this.Reset(); return true; }
    public override PluginDynamicFolderNavigation GetNavigationArea(DeviceType deviceType) => PluginDynamicFolderNavigation.None;
    public override string GetButtonDisplayName(PluginImageSize imageSize) => "Agents";
    public override BitmapImage GetButtonImage(PluginImageSize imageSize) => KeyFaces.State(this.FolderStatus(), imageSize);

    private string FolderStatus() => this.Owner.Board.Online
        ? AgentPriority.MostUrgent(this.Owner.Board.Snapshot.Recent)?.AgentStatus ?? "none"
        : "offline";

    // Null once the agent is gone, which returns the folder to the agent list.
    private Agent? SubAgentParent() =>
        this.subAgentsOf is null ? null : this.Owner.Board.Snapshot.Agents.FirstOrDefault(a => a.Key == this.subAgentsOf);

    public override IEnumerable<string> GetButtonPressActionNames(DeviceType deviceType)
    {
        if (this.showingShortcuts)
        {
            foreach (var parameter in this.shortcuts.Parameters()) yield return this.CreateCommandName("shortcuts:" + parameter);
            yield break;
        }
        if (this.SubAgentParent() is { } parent)
        {
            this.shownSubAgents = this.Owner.SubAgents.Read(parent, this.Owner.Board.Snapshot);
            yield return this.CreateCommandName("sub-back");
            yield return this.CreateCommandName($"parent:{AgentFace(parent, 1)}:{parent.Key}");
            foreach (var sub in this.shownSubAgents)
            {
                var tag = sub.Label.GetHashCode(StringComparison.Ordinal).ToString("x8");
                var face = this.armed?.Key == sub.Address ? "confirm" : $"{sub.AgentStatus}-{sub.SubAgentCount}-{tag}";
                yield return this.CreateCommandName($"sub:{face}:{sub.Address}");
            }
            yield break;
        }
        yield return this.CreateCommandName("open-shortcuts");
        yield return this.CreateCommandName("action:cycle");
        foreach (var (a, index) in SubAgentTree.TopLevel(this.Owner.Board.Snapshot).Select((agent, index) => (agent, index)))
        {
            yield return this.CreateCommandName($"agent:{AgentFace(a, index + 1)}:{a.Key}");
        }
    }

    private string AgentFace(Agent agent, int slot)
    {
        if (this.armed?.Key == agent.Key) return "confirm";
        if (this.Owner.Client.PendingAgentKey == agent.Key) return "opening";
        var tag = FixedSlots.AgentLabel(agent, slot).GetHashCode(StringComparison.Ordinal);
        return $"{agent.AgentStatus}-{agent.SubAgents.Count}-{tag:x8}";
    }

    // Parameters are `<kind>:<face>:<key>`; the key may itself contain colons.
    private static string? KeyOf(string parameter, string kind)
    {
        if (!parameter.StartsWith(kind + ":", StringComparison.Ordinal)) return null;
        var colon = parameter.IndexOf(':', kind.Length + 1);
        return colon < 0 ? null : parameter[(colon + 1)..];
    }

    private Agent? Find(string parameter)
    {
        var key = KeyOf(parameter, "agent") ?? KeyOf(parameter, "parent") ?? KeyOf(parameter, "sub");
        return key is null ? null : this.Owner.Board.Snapshot.Agents.FirstOrDefault(a => a.Key == key);
    }

    private SubAgent? FindSub(string parameter) =>
        KeyOf(parameter, "sub") is { } address ? this.shownSubAgents.FirstOrDefault(s => s.Address == address) : null;

    private static bool IsConfirmation(string parameter) =>
        parameter.StartsWith("agent:confirm:", StringComparison.Ordinal)
        || parameter.StartsWith("parent:confirm:", StringComparison.Ordinal)
        || parameter.StartsWith("sub:confirm:", StringComparison.Ordinal);

    public override string GetCommandDisplayName(string actionParameter, PluginImageSize imageSize)
    {
        if (actionParameter.StartsWith("shortcuts:", StringComparison.Ordinal)) return this.shortcuts.GetCommandDisplayName(actionParameter[10..], imageSize);
        if (actionParameter == "open-shortcuts") return "Shortcuts";
        if (actionParameter == "action:cycle") return "Cycle panes";
        if (actionParameter == "action:cycle-tabs") return "Cycle tabs";
        if (actionParameter == "sub-back") return "Back";
        if (IsConfirmation(actionParameter)) return "Close?";
        if (actionParameter.StartsWith("sub:", StringComparison.Ordinal)) return this.FindSub(actionParameter)?.Label ?? " ";
        var agent = this.Find(actionParameter);
        if (agent is null) return " ";
        if (this.Owner.Client.PendingAgentKey == agent.Key) return "Opening...";
        var index = SubAgentTree.TopLevel(this.Owner.Board.Snapshot).ToList().FindIndex(a => a.Key == agent.Key);
        return FixedSlots.AgentLabel(agent, index + 1);
    }

    public override BitmapImage GetCommandImage(string actionParameter, PluginImageSize imageSize)
    {
        if (actionParameter.StartsWith("shortcuts:", StringComparison.Ordinal)) return this.shortcuts.GetCommandImage(actionParameter[10..], imageSize);
        if (actionParameter == "open-shortcuts") return KeyFaces.Icon("layout", "Shortcuts", imageSize);
        if (actionParameter == "sub-back") return KeyFaces.Icon("chevron-left", "Back", imageSize);
        if (IsConfirmation(actionParameter)) return KeyFaces.Confirm(imageSize);
        if (actionParameter.StartsWith("sub:", StringComparison.Ordinal))
        {
            var sub = this.FindSub(actionParameter);
            return KeyFaces.State(sub?.AgentStatus ?? "unknown", imageSize, sub?.Remote == true, sub?.SubAgentCount ?? 0);
        }
        if (actionParameter.StartsWith("agent:", StringComparison.Ordinal) || actionParameter.StartsWith("parent:", StringComparison.Ordinal))
        {
            var agent = this.Find(actionParameter);
            return KeyFaces.State(agent?.AgentStatus ?? "unknown", imageSize, agent?.Machine is not null, agent?.SubAgents.Count ?? 0);
        }
        if (actionParameter.StartsWith("action:", StringComparison.Ordinal))
            return KeyFaces.Shortcut(actionParameter[7..], this.GetCommandDisplayName(actionParameter, imageSize), imageSize);
        return KeyFaces.Glyph(" ", imageSize);
    }

    public override bool ProcessButtonEvent2(string actionParameter, DeviceButtonEvent2 buttonEvent)
    {
        if (actionParameter.StartsWith("shortcuts:", StringComparison.Ordinal)) return this.showingShortcuts && this.shortcuts.ProcessButtonEvent2(actionParameter[10..], buttonEvent);
        var holdable = actionParameter.StartsWith("agent:", StringComparison.Ordinal)
            || actionParameter.StartsWith("parent:", StringComparison.Ordinal)
            || actionParameter.StartsWith("sub:", StringComparison.Ordinal);
        if (!holdable)
        {
            // Everything else acts on release, and a hold does nothing.
            switch (buttonEvent.EventType)
            {
                case DeviceButtonEventType.Press: this.cyclePressed = actionParameter; return true;
                case DeviceButtonEventType.LongPress:
                case DeviceButtonEventType.RepeatPress: return true;
                case DeviceButtonEventType.Release:
                    if (this.cyclePressed == actionParameter) this.RunCommand(actionParameter);
                    this.cyclePressed = null;
                    return true;
                default: return base.ProcessButtonEvent2(actionParameter, buttonEvent);
            }
        }
        var gestureEvent = buttonEvent.EventType switch
        {
            DeviceButtonEventType.Press => ClosePaneButtonEvent.Press,
            DeviceButtonEventType.LongPress => ClosePaneButtonEvent.LongPress,
            DeviceButtonEventType.RepeatPress => ClosePaneButtonEvent.RepeatPress,
            DeviceButtonEventType.Release => ClosePaneButtonEvent.Release,
            _ => (ClosePaneButtonEvent?)null,
        };
        if (gestureEvent is null) return false;
        var agent = this.Find(actionParameter);
        var opensSubAgents = actionParameter.StartsWith("agent:", StringComparison.Ordinal) && agent?.SubAgents.Count > 0;
        var hasPane = agent is { TerminalId.Length: > 0, PaneId.Length: > 0 };
        var confirming = agent is not null && this.armed?.Key == agent.Key && IsConfirmation(actionParameter);
        switch (this.gesture.Process(gestureEvent.Value, opensSubAgents, hasPane, confirming))
        {
            case AgentFolderGestureAction.OpenSubAgents:
                this.armed = null;
                this.subAgentsOf = agent!.Key;
                this.Plugin.Log.Info("Agents folder opened sub-agents");
                _ = this.Owner.SubAgents.RefreshAsync();
                this.Changed();
                break;
            case AgentFolderGestureAction.ArmClose:
                this.Arm(agent!);
                break;
            case AgentFolderGestureAction.Tap:
            case AgentFolderGestureAction.ConfirmClose:
                this.RunCommand(actionParameter);
                break;
        }
        return true;
    }

    private void Arm(Agent agent)
    {
        var target = new CloseTarget(agent.Key, agent.PaneId, agent.SpaceId, agent.App);
        this.armed = target;
        this.armedUntil = DateTime.UtcNow.AddSeconds(3);
        this.Changed();
        _ = this.ExpireAsync(target);
    }

    public override void RunCommand(string actionParameter)
    {
        if (actionParameter.StartsWith("shortcuts:", StringComparison.Ordinal))
        {
            if (this.showingShortcuts) this.shortcuts.RunCommand(actionParameter[10..]);
        }
        else if (actionParameter == "open-shortcuts")
        {
            this.armed = null;
            this.shortcuts.Reset();
            this.showingShortcuts = true;
            this.Changed();
        }
        else if (actionParameter == "sub-back") this.ReturnToAgents();
        else if (actionParameter == "action:cycle")
        {
            var id = actionParameter[7..];
            this.Plugin.Log.Info($"{id} pressed in Agents folder");
            _ = this.ExecuteAsync(id);
        }
        else if (this.Find(actionParameter) is { } agent)
        {
            var target = this.armed;
            this.armed = null;
            var confirming = IsConfirmation(actionParameter);
            if (target is not null && target.Key == agent.Key && confirming && DateTime.UtcNow <= this.armedUntil)
            {
                _ = this.CloseAsync(target);
                if (this.subAgentsOf == target.Key) { this.ReturnToAgents(); return; }
            }
            else if (this.FindSub(actionParameter) is { } sub) _ = this.FocusSubAgentAsync(sub);
            else _ = this.FocusAsync(agent.Key);
            this.Changed();
        }
        else if (this.FindSub(actionParameter) is { } sub)
        {
            this.armed = null;
            _ = this.FocusSubAgentAsync(sub);
            this.Changed();
        }
    }

    private void Reset()
    {
        this.showingShortcuts = false;
        this.shortcuts.Reset();
        this.subAgentsOf = null;
        this.shownSubAgents = [];
        this.armed = null;
        this.cyclePressed = null;
        this.gesture.Cancel();
    }

    private void ReturnToAgents()
    {
        this.Reset();
        this.Changed();
    }
    private async Task ExpireAsync(CloseTarget target)
    {
        await Task.Delay(TimeSpan.FromSeconds(3));
        if (ReferenceEquals(this.armed, target)) { this.armed = null; this.Changed(); }
    }
    private async Task CloseAsync(CloseTarget target)
    {
        try
        {
            if (!await new CloseAgent(this.Owner.Client).CloseIfStillPresentAsync(target))
                this.Plugin.Log.Warning("Herdr agent changed; close cancelled");
        }
        catch (Exception e) { this.Plugin.Log.Error($"Could not close Herdr pane: {e.Message}"); }
    }
    private async Task ExecuteAsync(string id)
    {
        try { await this.Owner.Navigation.ExecuteAsync(id); }
        catch (Exception e) { this.Plugin.Log.Error($"Could not run Herdr shortcut: {e.Message}"); }
    }
    private async Task FocusAsync(string agentKey)
    {
        try { await this.Owner.Client.FocusAgentAsync(agentKey); }
        catch (Exception e) { this.Plugin.Log.Error($"Could not focus Herdr agent: {e.Message}"); }
    }
    private async Task FocusSubAgentAsync(SubAgent sub)
    {
        try { await this.Owner.Client.FocusSubAgentAsync(sub); }
        catch (Exception e) { this.Plugin.Log.Error($"Could not show Herdr sub-agent: {e.Message}"); }
    }
    private void Changed()
    {
        // The host caches the folder key independently of its contents.
        try
        {
            var status = this.FolderStatus();
            if (status != this.lastFolderStatus)
            {
                this.Plugin.OnActionImageChanged("#DynamicFolder", this.Name, true);
                this.lastFolderStatus = status;
            }
        }
        catch (Exception e) { this.Plugin.Log.Error(e, "Could not refresh Herdr folder status"); }

        try
        {
            if (this.subAgentsOf is not null && this.SubAgentParent() is null) this.subAgentsOf = null;
            var next = this.GetButtonPressActionNames(DeviceType.All).ToArray();
            if (!next.SequenceEqual(this.names)) { this.names = next; this.ButtonActionNamesChanged(); }
            foreach (var name in next) this.CommandImageChanged(name);
        }
        catch (Exception e) { this.Plugin.Log.Error(e, "Could not refresh Herdr agents"); }
    }
}
