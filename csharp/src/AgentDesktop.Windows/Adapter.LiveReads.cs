using AgentDesktop.Core.Adapter;
using AgentDesktop.Core.Locator;
using AgentDesktop.Core.Tree;
using AgentDesktop.Core.Wait;
using AgentDesktop.Windows.Tree;
using AgentDesktop.Windows.Uia;

namespace AgentDesktop.Windows;

/// <summary>The single-element live reads, all five projections of one shared read. A resolved
/// handle is corroborated inside the shared read, so a provider whose process died during the
/// read never answers with a bundle claiming completeness.</summary>
public sealed partial class WindowsAdapter
{
    public string? GetLiveValue(NativeHandle handle, Deadline deadline) =>
        LiveRead.ReadElement(handle, deadline).ToValue();

    public ElementState? GetLiveState(NativeHandle handle, Deadline deadline) =>
        LiveRead.ReadElement(handle, deadline).ToState();

    public List<string>? GetLiveActions(NativeHandle handle, Deadline deadline) =>
        LiveRead.ReadElement(handle, deadline).ToActions();

    public LiveElement GetLiveElement(NativeHandle handle, Deadline deadline) =>
        LiveRead.ReadElement(handle, deadline).ToElement();

    public Rect? GetElementBounds(NativeHandle handle, Deadline deadline) =>
        LiveRead.ReadElement(handle, deadline).ToBounds();
}
