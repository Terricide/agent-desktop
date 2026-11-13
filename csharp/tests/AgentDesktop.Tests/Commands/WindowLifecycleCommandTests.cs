using AgentDesktop.Core.Actions;
using AgentDesktop.Core.Adapter;
using AgentDesktop.Core.Commands;
using AgentDesktop.Core.Context;
using AgentDesktop.Core.Response;
using AgentDesktop.Core.Systems;
using AgentDesktop.Core.Wait;
using Xunit;

namespace AgentDesktop.Tests.Commands;

/// <summary>The window lifecycle commands name the window the way every window-scoped command names one,
/// and report what the adapter read back rather than that a call returned. A write the adapter proved by
/// a placement re-read is delivered-verified with the scope it was measured against; a write the adapter
/// dispatched without a read-back is delivered-unverified. A window with no process instance token is
/// refused before the window is written to, because the report could not name which running copy of the
/// app the write reached.</summary>
public class WindowLifecycleCommandTests
{
    private sealed class StubAdapter : PlatformAdapter
    {
        public List<WindowInfo> Windows = [];

        public List<WindowInfo> ListWindows(WindowFilter filter, Deadline deadline) =>
            filter.App is null ? Windows : Windows.Where(w => w.App == filter.App).ToList();

        public WindowOp? Received { get; private set; }

        public OperationReport Report = new();

        public OperationReport WindowOperation(WindowInfo window, WindowOp op, InteractionLease lease)
        {
            Received = op;
            return Report;
        }
    }

    private static WindowInfo Window(string id, string title, string app, string? instance = "boot-1") =>
        new()
        {
            Id = id,
            Title = title,
            App = app,
            Pid = new ProcessId(42),
            ProcessInstance = instance,
            State = new WindowState { IsFocused = true, Visible = true },
        };

    private static StubAdapter AdapterWith(params WindowInfo[] windows) =>
        new() { Windows = windows.ToList() };

    private static OperationReport VerifiedReport(WindowInfo window, WindowOp op, WindowReadback readback) =>
        OperationReportBuild.WindowOperation(WindowTarget.ForWindow(window), op, readback);

    [Fact]
    public void MinimizeRefusesWhenNothingNamesTheWindow()
    {
        var adapter = AdapterWith(Window("w-1", "Editor", "Editor"));

        var response = new WindowLifecycleCommand(adapter).Run("minimize", null, null, null, new WindowOp.Minimize());

        Assert.False(response.Ok);
        Assert.Equal("INVALID_ARGS", response.Error!.Code);
        Assert.Null(adapter.Received);
    }

    [Fact]
    public void MinimizeCarriesTheMinimizeOperationToTheAdapter()
    {
        var window = Window("w-1", "Editor", "Editor");
        var adapter = AdapterWith(window);
        adapter.Report = VerifiedReport(window, new WindowOp.Minimize(), WindowReadback.State("window_minimized_state"));

        var response = new WindowLifecycleCommand(adapter).Run("minimize", "w-1", null, null, new WindowOp.Minimize());

        Assert.True(response.Ok);
        Assert.IsType<WindowOp.Minimize>(adapter.Received);
    }

    [Fact]
    public void MinimizeReportsTheVerbAloneAsTheAction()
    {
        var window = Window("w-1", "Editor", "Editor");
        var adapter = AdapterWith(window);
        adapter.Report = VerifiedReport(window, new WindowOp.Minimize(), WindowReadback.State("window_minimized_state"));

        var data = new WindowLifecycleCommand(adapter).Run("minimize", "w-1", null, null, new WindowOp.Minimize()).Data!;

        Assert.Equal("minimize", data["action"]!.GetValue<string>());
        Assert.Equal("delivered_verified", data["disposition"]!["delivery"]!.GetValue<string>());
    }

    [Fact]
    public void AWriteProvedByAPlacementReadBackCarriesItsStateScope()
    {
        var window = Window("w-1", "Editor", "Editor");
        var adapter = AdapterWith(window);
        adapter.Report = VerifiedReport(window, new WindowOp.Maximize(), WindowReadback.State("window_maximized_state"));

        var data = new WindowLifecycleCommand(adapter).Run("maximize", "w-1", null, null, new WindowOp.Maximize()).Data!;

        Assert.Equal("window_maximized_state", data["details"]!["verification_scope"]!.GetValue<string>());
        Assert.Null(data["details"]!["tolerance"]);
        Assert.True(data["steps"]![0]!["verified"]!.GetValue<bool>());
        Assert.Equal("semantic_api", data["steps"]![0]!["mechanism"]!.GetValue<string>());
    }

    [Fact]
    public void AGeometryWriteCarriesItsToleranceInTheReadBack()
    {
        var window = Window("w-1", "Editor", "Editor");
        var adapter = AdapterWith(window);
        adapter.Report = VerifiedReport(
            window, new WindowOp.Resize(800, 600), WindowReadback.Geometry("window_size", 8, "px"));

        var data = new WindowLifecycleCommand(adapter)
            .Run("resize-window", "w-1", null, null, new WindowOp.Resize(800, 600)).Data!;

        Assert.Equal("window_size", data["details"]!["verification_scope"]!.GetValue<string>());
        Assert.Equal(8.0, data["details"]!["tolerance"]!.GetValue<double>());
        Assert.Equal("px", data["details"]!["units"]!.GetValue<string>());
        Assert.Equal("resize-window", data["action"]!.GetValue<string>());
        Assert.Equal("window_resize", data["steps"]![0]!["label"]!.GetValue<string>());
    }

    [Fact]
    public void AWriteTheAdapterDispatchedWithoutAReadBackIsDeliveredUnverified()
    {
        var window = Window("w-1", "Editor", "Editor");
        var adapter = AdapterWith(window);
        adapter.Report = OperationReportBuild.WindowOperation(
            WindowTarget.ForWindow(window), new WindowOp.Move(10, 20), null);

        var data = new WindowLifecycleCommand(adapter)
            .Run("move-window", "w-1", null, null, new WindowOp.Move(10, 20)).Data!;

        Assert.Equal("delivered_unverified", data["disposition"]!["delivery"]!.GetValue<string>());
        Assert.Equal("unsafe", data["disposition"]!["retry"]!.GetValue<string>());
        Assert.Null(data["details"]);
        Assert.Null(data["steps"]![0]!["verified"]);
    }

    [Fact]
    public void TheReportNamesTheWindowAndTheProcessInstanceTheWriteReached()
    {
        var window = Window("w-1", "Editor", "Editor");
        var adapter = AdapterWith(window);
        adapter.Report = VerifiedReport(window, new WindowOp.Restore(), WindowReadback.State("window_minimized_state"));

        var target = new WindowLifecycleCommand(adapter)
            .Run("restore", "w-1", null, null, new WindowOp.Restore()).Data!["target"]!;

        Assert.Equal("window", target["kind"]!.GetValue<string>());
        Assert.Equal("w-1", target["id"]!.GetValue<string>());
        Assert.Equal("Editor", target["title"]!.GetValue<string>());
        Assert.Equal(42u, target["process"]!["pid"]!.GetValue<uint>());
        Assert.Equal("boot-1", target["process"]!["instance"]!.GetValue<string>());
    }

    [Fact]
    public void AWindowWithNoProcessInstanceTokenCannotBeNamedAsTheTarget()
    {
        var window = Window("w-1", "Editor", "Editor", null);

        var error = Assert.Throws<AdapterError>(() => WindowTarget.ForWindow(window));

        Assert.Equal("INTERNAL", error.Code.AsString());
        Assert.Equal(Delivery.NotDelivered, error.Disposition.Delivery);
    }

    [Fact]
    public void ATitleThatMatchesTwoWindowsIsRefusedNamingTheOperation()
    {
        var adapter = AdapterWith(
            Window("w-1", "Report - Notepad", "Notepad"),
            Window("w-2", "Report - Chrome", "Chrome"));

        var error = Assert.Throws<AdapterError>(
            () => new WindowLifecycleCommand(adapter).Run("resize-window", null, null, "Report", new WindowOp.Resize(800, 600)));

        Assert.Equal("AMBIGUOUS_TARGET", error.Code.AsString());
        Assert.Contains("resize target", error.Message);
        Assert.Null(adapter.Received);
    }

    [Fact]
    public void TheAppNamesTheCandidatesWhenNoWindowIdIsGiven()
    {
        var notes = Window("w-2", "Notes", "Notes");
        var adapter = AdapterWith(Window("w-1", "Editor", "Editor"), notes);
        adapter.Report = VerifiedReport(notes, new WindowOp.Minimize(), WindowReadback.State("window_minimized_state"));

        var response = new WindowLifecycleCommand(adapter).Run("minimize", null, "Notes", null, new WindowOp.Minimize());

        Assert.True(response.Ok);
        Assert.Equal("w-2", response.Data!["target"]!["id"]!.GetValue<string>());
    }

    [Fact]
    public void AWindowIdThePlatformDidNotListIsRefusedAsUnsent()
    {
        var adapter = AdapterWith(Window("w-1", "Editor", "Editor"));

        var response = new WindowLifecycleCommand(adapter).Run("restore", "w-9", null, null, new WindowOp.Restore());

        Assert.False(response.Ok);
        Assert.Equal("WINDOW_NOT_FOUND", response.Error!.Code);
        Assert.Equal(Delivery.NotDelivered, response.Error.Disposition.Delivery);
        Assert.Null(adapter.Received);
    }
}
