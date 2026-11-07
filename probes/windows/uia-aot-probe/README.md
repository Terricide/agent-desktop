# A19-1 — UIA in a Native AOT binary: P/Invoke + vtable slots

**Question:** can UIA be read in a Native AOT binary, where `System.Runtime.InteropServices`
COM interop is unsupported, and at what cost?

**Verdict: YES, and the cost is not in the AOT route.** The shipped Rust adapter's UIA
reads are reproduced in a published AOT binary with `DllImport` + function pointers,
zero `Activator.CreateInstance`, zero `Marshal.GetActiveObject`, zero `AllowSystemShim`.
The probe is a spike, not a product surface: it is not wired into the binary, and no
product command depends on it.

## What was built

`UiaAotProbe.csproj` — `net8.0`, `win-x64`, `PublishAot=true`, the framework
`csharp/Directory.Build.props` sets for the port. The published exe is 1.7 MB. It runs
`--target <app>`, resolves that app's largest visible titled top-level window, and
reports in one bounded JSON document:

- window identity: name, AutomationId, control type, framework, class name, IsEnabled,
  IsOffscreen, HasKeyboardFocus, IsKeyboardFocusable.
- a RawViewWalker census of the window subtree, and a `FindAll(TreeScope_Descendants)`
  census, each at first contact and again after `--settle-ms`.
- `--walk-mode walker|find_all` to isolate each read path when measuring cost.
- `--windows` to list every window of the target pid with class, title, visibility, area.
- `--invoke <name>` finds a named descendant and executes Invoke through the function
  pointer to `IUIAutomationInvokePattern` at element slot 16.

`Uia.cs` carries the runtime contract. Every slot number is taken from the
`COBJMACROS` block of `UIAutomationClient.h` in the 10.0.26100 SDK, counted in order
after `QueryInterface`/`AddRef`/`Release`:

| Interface | Slot | Method |
| --- | --- | --- |
| `IUIAutomation` | 6 | `ElementFromHandle` |
| `IUIAutomation` | 7 | `ElementFromPoint` |
| `IUIAutomation` | 14 | `get_RawViewWalker` |
| `IUIAutomation` | 21 | `CreateTrueCondition` |
| `IUIAutomationElement` | 3 | `SetFocus` |
| `IUIAutomationElement` | 5 | `FindFirst` |
| `IUIAutomationElement` | 6 | `FindAll` |
| `IUIAutomationElement` | 16 | `GetCurrentPattern` |
| `IUIAutomationElement` | 17 | `GetControlType` |
| `IUIAutomationElement` | 22 | `GetCurrentAcceleratorKey` |
| `IUIAutomationElement` | 23 | `GetCurrentAccessKey` |
| `IUIAutomationElement` | 34 | `GetHasKeyboardFocus` |
| `IUIAutomationElement` | 35 | `GetIsKeyboardFocusable` |
| `IUIAutomationElement` | 36 | `GetIsEnabled` |
| `IUIAutomationElement` | 37 | `GetCurrentHasKeyboardFocus` |
| `IUIAutomationElement` | 38 | `GetCurrentIsKeyboardFocusable` |
| `IUIAutomationElement` | 39 | `GetCurrentIsOffscreen` |
| `IUIAutomationElement` | 43 | `GetBoundingRectangle` |
| `IUIAutomationElement` | 44 | `GetCurrentFrameworkId` |
| `IUIAutomationElement` | 45 | `GetCurrentItemType` |
| `IUIAutomationElementArray` | 3 | `get_Length` |
| `IUIAutomationElementArray` | 4 | `GetElement` |

Three calls are made through function pointers the AOT compiler accepts directly:
`delegate* unmanaged<IntPtr, IntPtr, int>` (one pointer arg),
`delegate* unmanaged<IntPtr, int, IntPtr*, int>` (int arg, out pointer), and
`delegate* unmanaged<IntPtr, int, IntPtr, IntPtr*, int>` (FindAll: scope, condition, out array).

**What AOT rejected, precisely:** a `delegate* unmanaged<IntPtr, int, IntPtr, int>` used
as if FindAll had three args and an int return — the compiler rejects a function pointer
whose parameter count does not match the call site. That is the AOT contract: a
function pointer is typed by exact signature, and a mismatched signature fails at
compile time, not at runtime. No UIA method in the set needed a struct passed by value
except `ElementFromPoint`, which takes `UiaPoint` (two ints, one register) and is accepted.

`[return: MarshalAs(UnmanagedType.BStr)]` and `DllImport` string marshalling are
supported by AOT, so `GetCurrentName` and friends are read as BSTR with
`Marshal.PtrToStringBSTR` + `Marshal.FreeBSTR`.

`CoCreateInstance` for `CUIAutomation8` (and `CUIAutomation` as fallback) is called
through a P/Invoke to `ole32.dll` with a local GUID. `CoInitializeEx(nil, 2)` —
apartment-threaded — is required first.

## Cost, min of seven runs, warm-up discarded, `--settle-ms 0`

The probe reports raw UIA reads. The shipped adapter's `snapshot` does the same reads
plus ref allocation, identity capture, and JSON serialization of the tree. Both run
against the same live app on the same machine.

| App | probe, `FindAll(Descendants)` | shipped `agent-desktop snapshot` |
| --- | --- | --- |
| Notepad | 546 ms | 1596 ms |
| Slack | 2447 ms | 2217 ms |
| msedge | 1330 ms | 1191 ms |
| Code | 274 ms | 3354 ms |

The AOT route is not slower. Code's row is not a like-for-like: the adapter settles and
returns 2344 nodes, the probe's single pass at settle 0 sees the pre-activation shell,
so the adapter's time includes work the probe did not do.

The cost is in the read algorithm, not in the route. A RawViewWalker census of Slack
(one `GetFirstChild` + one `GetNextSibling` + 7 `GetCurrentPattern` calls per node)
measured 3111 ms min against 2447 ms for the same tree read with one
`FindAll(TreeScope_Descendants, CreateTrueCondition)` and one property pass. The
walker path is the expensive one, and the shipped adapter already uses the FindAll path.

## Activation reproduces in the AOT process

Slack: first contact 19 walker nodes / 13 FindAll nodes; after a 1500 ms settle, 928 /
540. That reproduces A1-5 (`11-electron-activation.ps1`) in an AOT process with no flag
and no `--force-renderer-accessibility`: the first UIA client to touch a renderer
activates accessibility for that process, and a single read taken at first contact
understates the tree by roughly 40x. A point touch (`ElementFromPoint`) inside the window
is what activates it; a top-level `ElementFromHandle` read alone does not.

msedge was already activated by other clients: 452 nodes at first contact, unchanged
after settle.

## Build

```
dotnet publish -c Release -r win-x64                      # needs VC++ 14.44+ for AOT
.\UiaAotProbe.exe --target Slack --settle-ms 1500
```

The published exe is checked in as `UiaAotProbe.exe` (1.7 MB). The published output
reproduces it at `bin/Release/net8.0/win-x64/publish/UiaAotProbe.exe`.
