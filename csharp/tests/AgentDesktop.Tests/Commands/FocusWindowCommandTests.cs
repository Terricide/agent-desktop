using AgentDesktop.Core.Adapter;
using AgentDesktop.Core.Commands;
using AgentDesktop.Core.Context;
using AgentDesktop.Core.Response;
using AgentDesktop.Core.Systems;
using AgentDesktop.Core.Wait;
using Xunit;

namespace AgentDesktop.Tests.Commands;

/// <summary>focus-window names the window with an id, a title substring, or the app that owns the
/// candidates, and the window is narrowed the way every window-scoped command narrows them. A command
/// with no selector refuses before the application is read, and a title that matches two windows is
/// refused with the candidates rather than resolved by picking one.</summary>
public class FocusWindowCommandTests
{
    private sealed class StubAdapter : PlatformAdapter
    {
        public List<WindowInfo> Windows = [];

        public List<WindowInfo> ListWindows(WindowFilter filter, Deadline deadline) =>
            filter.App is null ? Windows : Windows.Where(w => w.App == filter.App).ToList();

        public WindowInfo? Focused { get; private set; }

        public void FocusWindow(WindowInfo window, InteractionLease lease) => Focused = window;
    }

    private static WindowInfo Window(string id, string title, string app, bool focused, bool visible = true) =>
        new()
        {
            Id = id,
            Title = title,
            App = app,
            Pid = new ProcessId(1),
            State = new WindowState { IsFocused = focused, Visible = visible },
        };

    [Fact]
    public void FocusWindowRefusesWhenNothingNamesTheWindow()
    {
        var adapter = new StubAdapter { Windows = [Window("w-1", "Editor", "Editor", true)] };

        var response = new FocusWindowCommand(adapter).Run("focus-window", null, null, null);

        Assert.False(response.Ok);
        Assert.Equal("INVALID_ARGS", response.Error!.Code);
        Assert.Null(adapter.Focused);
    }

    [Fact]
    public void FocusWindowActivatesTheWindowTheIdNames()
    {
        var adapter = new StubAdapter
        {
            Windows = [Window("w-1", "Editor", "Editor", true), Window("w-2", "Preview", "Editor", false)],
        };

        var response = new FocusWindowCommand(adapter).Run("focus-window", "w-2", null, null);

        Assert.True(response.Ok);
        Assert.Equal("w-2", adapter.Focused!.Id);
    }

    [Fact]
    public void FocusWindowReportsAWindowIdThePlatformDidNotList()
    {
        var adapter = new StubAdapter { Windows = [Window("w-1", "Editor", "Editor", true)] };

        var response = new FocusWindowCommand(adapter).Run("focus-window", "w-9", null, null);

        Assert.False(response.Ok);
        Assert.Equal("WINDOW_NOT_FOUND", response.Error!.Code);
        Assert.Equal(Delivery.NotDelivered, response.Error.Disposition.Delivery);
        Assert.Null(adapter.Focused);
    }

    [Fact]
    public void FocusWindowTakesTheAppAsItsCandidates()
    {
        var adapter = new StubAdapter
        {
            Windows = [Window("w-1", "Editor", "Editor", true), Window("w-2", "Notes", "Notes", true)],
        };

        var response = new FocusWindowCommand(adapter).Run("focus-window", null, "Notes", null);

        Assert.True(response.Ok);
        Assert.Equal("w-2", adapter.Focused!.Id);
    }

    [Fact]
    public void FocusWindowTakesTheFocusedWindowWhenATitleMatchesTwo()
    {
        var adapter = new StubAdapter
        {
            Windows = [Window("w-1", "Report - Notepad", "Notepad", false), Window("w-2", "Report - Chrome", "Chrome", true)],
        };

        var response = new FocusWindowCommand(adapter).Run("focus-window", null, null, "Report");

        Assert.True(response.Ok);
        Assert.Equal("w-2", adapter.Focused!.Id);
    }

    [Fact]
    public void FocusWindowRefusesATitleThatMatchesTwoUnfocusedWindows()
    {
        var adapter = new StubAdapter
        {
            Windows = [Window("w-1", "Report - Notepad", "Notepad", false), Window("w-2", "Report - Chrome", "Chrome", false)],
        };

        var error = Assert.Throws<AdapterError>(
            () => new FocusWindowCommand(adapter).Run("focus-window", null, null, "Report"));

        Assert.Equal(ErrorCode.AmbiguousTarget, error.Code);
        Assert.Null(adapter.Focused);
    }

    [Fact]
    public void FocusWindowNarrowsTwoMatchesToTheOneTheAppKeepsVisible()
    {
        var adapter = new StubAdapter
        {
            Windows = [Window("w-1", "Report - Notepad", "Notepad", false, false), Window("w-2", "Report - Chrome", "Chrome", false)],
        };

        var response = new FocusWindowCommand(adapter).Run("focus-window", null, null, "Report");

        Assert.True(response.Ok);
        Assert.Equal("w-2", adapter.Focused!.Id);
    }

    [Fact]
    public void FocusWindowReportsTheWindowItActivatedInTheInventoryShape()
    {
        var adapter = new StubAdapter { Windows = [Window("w-1", "Editor", "Editor", true)] };

        var response = new FocusWindowCommand(adapter).Run("focus-window", "w-1", null, null);
        var focused = response.Data!["focused"]!;

        Assert.Equal("w-1", focused["id"]!.GetValue<string>());
        Assert.Equal("Editor", focused["app_name"]!.GetValue<string>());
        Assert.True(focused["is_focused"]!.GetValue<bool>());
    }
}
