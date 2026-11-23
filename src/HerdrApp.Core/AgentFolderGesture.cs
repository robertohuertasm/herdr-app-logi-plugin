namespace HerdrApp;

public enum AgentFolderGestureAction { None, Tap, OpenSubAgents, ArmClose, ConfirmClose }

// A parent's outer key opens its children on hold. Inside that detail, parent
// and pane-backed children use the same hold-then-confirm close gesture.
public sealed class AgentFolderGesture
{
    private readonly ClosePaneGesture close = new();

    public AgentFolderGestureAction Process(ClosePaneButtonEvent buttonEvent, bool opensSubAgents, bool hasPane, bool armed) =>
        this.close.Process(buttonEvent, armed) switch
        {
            ClosePaneGestureAction.Tap => AgentFolderGestureAction.Tap,
            ClosePaneGestureAction.Arm when opensSubAgents => AgentFolderGestureAction.OpenSubAgents,
            ClosePaneGestureAction.Arm when hasPane => AgentFolderGestureAction.ArmClose,
            ClosePaneGestureAction.Confirm when hasPane => AgentFolderGestureAction.ConfirmClose,
            _ => AgentFolderGestureAction.None,
        };

    public void Cancel() => this.close.Cancel();
}
