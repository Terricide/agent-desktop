using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace UiaAotProbe;

/// Instantiates the UIA3 COM client, walks one real window, reads the identity
/// properties, censuses pattern support, and calls one Invoke. Prints one bounded
/// JSON document; diagnostics go to stderr. Exit 0 means every step ran.
internal static class Program
{
    private static string ClientUsed = "none";

    private static int Main(string[] args)
    {
        var target = Argument(args, "--target");
        if (target is null)
        {
            Console.Error.WriteLine("--target is required");
            return 2;
        }

        var maxNodes = ArgumentInt(args, "--max-nodes", 2000);
        var invokeName = Argument(args, "--invoke");

        Uia.CoInitializeEx(IntPtr.Zero, 2);
        var client = CreateClient();
        if (client == IntPtr.Zero)
        {
            Console.Error.WriteLine("CoCreateInstance failed for both CUIAutomation8 and CUIAutomation");
            return 3;
        }

        var hwnd = ResolveWindow(target);
        if (args.Contains("--windows"))
        {
            var listing = new StringBuilder("{\"windows\":[");
            var firstItem = true;
            foreach (var process in Process.GetProcessesByName(target))
            {
                EnumWindows((win, _) =>
                {
                    if (GetWindowPid(win) != process.Id)
                    {
                        return true;
                    }

                    var title = new StringBuilder(512);
                    GetWindowText(win, title, 512);
                    var className = new StringBuilder(256);
                    GetClassName(win, className, 256);
                    var area = GetWindowRect(win, out var r) == 0 ? 0 : (long)(r.right - r.left) * (r.bottom - r.top);
                    if (!firstItem)
                    {
                        listing.Append(',');
                    }

                    firstItem = false;
                    listing.Append("{\"hwnd\":" + win.ToInt64());
                    listing.Append(",\"visible\":" + (IsWindowVisible(win) != 0 ? "true" : "false"));
                    listing.Append(",\"name\":\"" + title + "\"");
                    listing.Append(",\"class\":\"" + className + "\"");
                    listing.Append(",\"area\":" + area + "}");
                    return true;
                }, IntPtr.Zero);

                process.Dispose();
            }

            listing.Append("]}");
            Console.WriteLine(listing.ToString());
            return 0;
        }
        if (hwnd == IntPtr.Zero)
        {
            Console.Error.WriteLine($"no top-level window for target '{target}'");
            return 4;
        }

        var window = Uia.CallOutPointerWithArg(client, Uia.IuiAutomationElementFromHandle, hwnd);
        if (window == IntPtr.Zero)
        {
            Console.Error.WriteLine("ElementFromHandle returned null");
            return 5;
        }

        var walker = Uia.CallOutPointer(client, Uia.IuiAutomationGetRawViewWalker);
        var walkMode = Argument(args, "--walk-mode") ?? "both";
        var first = walkMode == "find_all" ? null : Census(walker, window, maxNodes);
        var firstAll = walkMode == "walker" ? null : CensusFindAll(client, window, maxNodes);

        var bounds = Uia.CallRectOut(window, Uia.ElementGetBoundingRectangle);
        var touchPoint = new Uia.UiaPoint
        {
            x = (int)((bounds.left + bounds.right) / 2),
            y = (int)((bounds.top + bounds.bottom) / 2),
        };
        var touched = Uia.CallPointOut(client, Uia.IuiAutomationElementFromPoint, touchPoint);
        var touchedName = touched != IntPtr.Zero ? Uia.CallStringOut(touched, Uia.ElementGetName) : "none";
        Uia.Release(touched);

        var settleMs = ArgumentInt(args, "--settle-ms", 1500);
        Thread.Sleep(settleMs);
        var settled = walkMode == "find_all" ? null : Census(walker, window, maxNodes);
        var settledAll = walkMode == "walker" ? null : CensusFindAll(client, window, maxNodes);

        var document = new Json();
        document.String("client", ClientUsed);
        document.String("target", target);
        document.Number("pid", ProcessIdOf(hwnd));
        document.Object("window", body => WindowRecord(window, body));
        document.String("touch", touchedName);
        document.Number("settle_ms", settleMs);
        if (first is not null)
        {
            document.Object("first_contact", walk => CensusRecord(first, walk));
        }

        if (firstAll is not null)
        {
            document.Object("first_contact_find_all", walk => CensusRecord(firstAll, walk));
        }

        if (settled is not null)
        {
            document.Object("settled", walk => CensusRecord(settled, walk));
        }

        if (settledAll is not null)
        {
            document.Object("settled_find_all", walk => CensusRecord(settledAll, walk));
        }

        if (invokeName is not null)
        {
            var invoke = Invoke(walker, window, invokeName);
            document.Object("invoke", body =>
            {
                body.String("name", invokeName);
                body.Bool("found", invoke.Found);
                body.Bool("invoked", invoke.Invoked);
                body.String("detail", invoke.Detail);
            });
        }

        Console.WriteLine(document.Build());
        Uia.Release(walker);
        Uia.Release(window);
        Uia.Release(client);
        return 0;
    }

    private static IntPtr CreateClient()
    {
        foreach (var clsid in new[] { Uia.CuiAutomation8, Uia.CuiAutomation })
        {
            var hr = Uia.CoCreateInstance(in clsid, IntPtr.Zero, 1, in Uia.IidIUIAutomation, out var pointer);
            if (hr == 0 && pointer != IntPtr.Zero)
            {
                ClientUsed = clsid == Uia.CuiAutomation8 ? "CUIAutomation8" : "CUIAutomation";
                return pointer;
            }
        }

        return IntPtr.Zero;
    }

    private static IntPtr ResolveWindow(string target)
    {
        var best = IntPtr.Zero;
        var bestArea = 0L;
        var bestHasTitle = false;

        foreach (var process in Process.GetProcessesByName(target))
        {
            EnumWindows((hwnd, _) =>
            {
                if (GetWindowPid(hwnd) != process.Id || IsWindowVisible(hwnd) == 0)
                {
                    return true;
                }

                if (GetWindowRect(hwnd, out var rect) == 0)
                {
                    return true;
                }

                var area = (long)(rect.right - rect.left) * (rect.bottom - rect.top);
                if (area <= 0)
                {
                    return true;
                }

                var hasTitle = GetWindowTextLength(hwnd) > 0;
                if ((hasTitle && !bestHasTitle) || (hasTitle == bestHasTitle && area > bestArea))
                {
                    best = hwnd;
                    bestArea = area;
                    bestHasTitle = hasTitle;
                }

                return true;
            }, IntPtr.Zero);

            process.Dispose();
        }

        return best != IntPtr.Zero ? best : Uia.GetForegroundWindow();
    }

    private static int ProcessIdOf(IntPtr hwnd)
    {
        var pid = 0;
        GetWindowThreadProcessId(hwnd, ref pid);
        return pid;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int maxLength);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int GetClassName(IntPtr hwnd, StringBuilder name, int maxLength);

    delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")]
    static extern int EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    static extern int IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll")]
    static extern int GetWindowThreadProcessId(IntPtr hwnd, ref int processId);

    [DllImport("user32.dll")]
    static extern int GetWindowRect(IntPtr hwnd, out NativeRect rect);

    [StructLayout(LayoutKind.Sequential)]
    struct NativeRect
    {
        public int left;

        public int top;

        public int right;

        public int bottom;
    }

    private static int GetWindowPid(IntPtr hwnd)
    {
        var pid = 0;
        GetWindowThreadProcessId(hwnd, ref pid);
        return pid;
    }

    [DllImport("user32.dll")]
    static extern int GetWindowTextLength(IntPtr hwnd);

    private static void WindowRecord(IntPtr element, Json body)
    {
        body.String("name", Uia.CallStringOut(element, Uia.ElementGetName));
        body.String("automation_id", Uia.CallStringOut(element, Uia.ElementGetAutomationId));
        body.Number("control_type", Uia.CallIntOut(element, Uia.ElementGetControlType));
        body.String("framework", Uia.CallStringOut(element, Uia.ElementGetFrameworkId));
        body.String("class_name", Uia.CallStringOut(element, Uia.ElementGetClassName));
        body.Bool("is_enabled", Uia.CallBoolOut(element, Uia.ElementGetIsEnabled));
        body.Bool("is_offscreen", Uia.CallBoolOut(element, Uia.ElementGetIsOffscreen));
        body.Bool("has_focus", Uia.CallBoolOut(element, Uia.ElementGetHasKeyboardFocus));
        body.Bool("focusable", Uia.CallBoolOut(element, Uia.ElementGetIsKeyboardFocusable));
    }

    private static void CensusRecord(CensusResult census, Json walk)
    {
        walk.Number("nodes_observed", census.Nodes);
        walk.Number("max_depth", census.Depth);
        walk.Bool("truncated", census.Truncated);
        walk.Object("control_types", types =>
        {
            foreach (var pair in census.ControlTypes)
            {
                types.Number(pair.Key.ToString(), pair.Value);
            }
        });
        walk.Object("patterns", patterns =>
        {
            foreach (var pair in census.Patterns)
            {
                patterns.Number(pair.Key, pair.Value);
            }
        });
    }

    private static CensusResult CensusFindAll(IntPtr client, IntPtr root, int maxNodes)
    {
        var condition = Uia.CallOutPointer(client, Uia.IuiAutomationCreateTrueCondition);
        var array = Uia.CallOutPointerWithIntAndPointer(root, Uia.ElementFindAll, 4, condition);
        Uia.Release(condition);
        var controlTypes = new Dictionary<int, int>();
        var patterns = new Dictionary<string, int>();
        if (array == IntPtr.Zero)
        {
            return new CensusResult(0, 0, false, controlTypes, patterns);
        }

        var length = Uia.CallIntOut(array, Uia.ElementArrayGetLength);
        var visited = Math.Min(length, maxNodes);
        for (var i = 0; i < visited; i++)
        {
            var element = Uia.CallOutPointerWithIntArg(array, Uia.ElementArrayGetElement, i);
            if (element == IntPtr.Zero)
            {
                continue;
            }

            var type = Uia.CallIntOut(element, Uia.ElementGetControlType);
            controlTypes[type] = controlTypes.TryGetValue(type, out var count) ? count + 1 : 1;
            foreach (var id in new[] { Uia.PatternInvoke, Uia.PatternToggle, Uia.PatternValue,
                Uia.PatternExpandCollapse, Uia.PatternSelectionItem, Uia.PatternScroll, Uia.PatternScrollItem })
            {
                var pattern = Uia.CallOutPointerWithIntArg(element, Uia.ElementGetCurrentPattern, id);
                if (pattern != IntPtr.Zero)
                {
                    var key = id.ToString();
                    patterns[key] = patterns.TryGetValue(key, out var hits) ? hits + 1 : 1;
                    Uia.Release(pattern);
                }
            }

            Uia.Release(element);
        }

        Uia.Release(array);
        return new CensusResult(length, 0, length > maxNodes, controlTypes, patterns);
    }

    private sealed record CensusResult(int Nodes, int Depth, bool Truncated,
        Dictionary<int, int> ControlTypes, Dictionary<string, int> Patterns);

    private static CensusResult Census(IntPtr walker, IntPtr root, int maxNodes)
    {
        var controlTypes = new Dictionary<int, int>();
        var patterns = new Dictionary<string, int>();
        var stack = new Stack<IntPtr>();
        var depths = new Stack<int>();
        stack.Push(root);
        depths.Push(0);
        var nodes = 0;
        var depth = 0;
        var truncated = false;

        while (stack.Count > 0)
        {
            var node = stack.Pop();
            var level = depths.Pop();
            if (level > depth)
            {
                depth = level;
            }

            nodes++;
            var type = Uia.CallIntOut(node, Uia.ElementGetControlType);
            controlTypes[type] = controlTypes.TryGetValue(type, out var count) ? count + 1 : 1;
            foreach (var id in new[] { Uia.PatternInvoke, Uia.PatternToggle, Uia.PatternValue,
                Uia.PatternExpandCollapse, Uia.PatternSelectionItem, Uia.PatternScroll, Uia.PatternScrollItem })
            {
                var pattern = Uia.CallOutPointerWithIntArg(node, Uia.ElementGetCurrentPattern, id);
                if (pattern != IntPtr.Zero)
                {
                    var key = id.ToString();
                    patterns[key] = patterns.TryGetValue(key, out var hits) ? hits + 1 : 1;
                    Uia.Release(pattern);
                }
            }

            if (nodes >= maxNodes)
            {
                truncated = true;
                break;
            }

            var child = Uia.CallOutPointerWithArg(walker, Uia.WalkerGetFirstChild, node);
            while (child != IntPtr.Zero)
            {
                stack.Push(child);
                depths.Push(level + 1);
                var next = Uia.CallOutPointerWithArg(walker, Uia.WalkerGetNextSibling, child);
                child = next;
            }

            if (node != root)
            {
                Uia.Release(node);
            }
        }

        return new CensusResult(nodes, depth, truncated, controlTypes, patterns);
    }

    private sealed record InvokeResult(bool Found, bool Invoked, string Detail);

    private static InvokeResult Invoke(IntPtr walker, IntPtr root, string name)
    {
        var stack = new Stack<IntPtr>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (string.Equals(Uia.CallStringOut(node, Uia.ElementGetName), name, StringComparison.OrdinalIgnoreCase))
            {
                var pattern = Uia.CallOutPointerWithIntArg(node, Uia.ElementGetCurrentPattern, Uia.PatternInvoke);
                if (pattern == IntPtr.Zero)
                {
                    return new InvokeResult(true, false, "no invoke pattern");
                }

                var hr = Uia.CallVoid(pattern, Uia.InvokePatternInvoke);
                Uia.Release(pattern);
                return new InvokeResult(true, hr == 0, "hr=" + hr.ToString("X"));
            }

            var child = Uia.CallOutPointerWithArg(walker, Uia.WalkerGetFirstChild, node);
            while (child != IntPtr.Zero)
            {
                stack.Push(child);
                var next = Uia.CallOutPointerWithArg(walker, Uia.WalkerGetNextSibling, child);
                child = next;
            }

            if (node != root)
            {
                Uia.Release(node);
            }
        }

        return new InvokeResult(false, false, "not found");
    }

    /// A bounded JSON writer. The probe has no serializer dependency, so the AOT
    /// binary stays small and the capture shape is fixed by this file.
    private sealed class Json
    {
        private readonly StringBuilder _body = new();

        public void String(string? key, string value) => Pair(key, "\"" + Escape(value) + "\"");

        public void Number(string? key, object value) => Pair(key, value.ToString() ?? "0");

        public void Bool(string key, bool value) => Pair(key, value ? "true" : "false");

        public void Object(string key, Action<Json> fill)
        {
            var child = new Json();
            fill(child);
            Pair(key, child.Build());
        }

        public void Array(string key, Action<Json> fill)
        {
            var child = new Json();
            fill(child);
            Pair(key, "[" + child._body.ToString().TrimStart(',') + "]");
        }

        private void Pair(string? key, string value)
        {
            _body.Append(',');
            if (key is not null)
            {
                _body.Append('"').Append(key).Append("\":");
            }

            _body.Append(value);
        }

        public string Build() => "{" + _body.ToString().TrimStart(',') + "}";

        private static string Escape(string text)
        {
            var builder = new StringBuilder();
            foreach (var character in text)
            {
                if (character == '"')
                {
                    builder.Append("\\\"");
                }
                else if (character == '\\')
                {
                    builder.Append("\\\\");
                }
                else if (character < 32)
                {
                    builder.Append('?');
                }
                else
                {
                    builder.Append(character);
                }
            }

            return builder.ToString();
        }
    }

    private static string? Argument(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == name)
            {
                return args[i + 1];
            }
        }

        return null;
    }

    private static int ArgumentInt(string[] args, string name, int fallback)
    {
        var raw = Argument(args, name);
        return int.TryParse(raw, out var value) ? value : fallback;
    }
}
