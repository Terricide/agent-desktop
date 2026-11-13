# C# port conversion brief (shared contract for all conversion workers)

Repo root: `C:/Users/mreed/.t3/worktrees/agent-desktop/t3-11789549`
Rust source (the truth): `crates/windows/src/**` and `crates/core/src/**`
C# target: `csharp/src/**`

## What you are doing

You are converting an assigned slice of the Rust Windows adapter to C#. The C# port is a
faithful, idiomatic translation of the same product: same command names, same JSON envelope,
same error codes, same recovery/disposition objects, same platform behaviour.

**Do not redesign.** The Rust code is the specification. Translate it. If a Rust file's
behaviour depends on a Rust module you were not assigned, call the C# type that already
exists, or leave a clearly-marked seam (see "Seams" below).

## Hard rules (the repo enforces these; a violation fails the gate)

1. **No comments.** No `//`, no end-of-line comments, no `/* */`. Allowed: `///` XML doc on a
   public item when the name is not self-explanatory, and `///` file-level module docs. The
   Rust `//` comments carry rationale — convert that rationale into a `///` XML doc on the
   item it explains, never into a `//` line.
2. **400 lines max per hand-written `.cs` file.** Split by responsibility. If your slice
   exceeds it, split into more files with the Rust prefix naming kept (`window_ops.cs`,
   `window_op_readback.cs`).
3. **No `unwrap`/`expect`/panic equivalents outside tests.** No `!` null-forgiving on a
   genuinely absent value, no `NullReferenceException` path. Propagate with
   `throw new AdapterError(...)` (the C# analogue of `?`) or `AdapterError` returns.
4. **No delivery-plan references in code.** No `R15`, `R3`, `KTD<n>`, `U<n>`, `phase`,
   `sub-phase` in any `.cs` under `csharp/src/**`. Rust probe row ids such as `A15-7` are
   allowed and encouraged. Convert a Rust comment that names a plan id into a statement of
   what is true about the code.
5. **Naming.** Module files singular snake-case → C# PascalCase file name matching the type
   it holds. Types PascalCase, functions verb-first PascalCase, constants PascalCase,
   parameters camelCase. Crates `AgentDesktop.<Name>`.
6. **One domain type per file.** One `record`/`class`/`enum` per file when Rust has one.
7. **At most 7 fields per record, at most 5 parameters per function.** Use a config record.
8. **`pub` boundaries.** Only `lib`-level re-exports are `public`. Internal types are
   `internal`. No wildcard re-exports.
9. **Tests sit beside their code.** Rust `foo.rs` + `foo_tests.rs` → C# `Foo.cs` +
   `FooTests.cs` in the same folder, referenced from the test project
   `tests/AgentDesktop.Windows.Tests`. Convert the Rust `#[test]` cases; keep the test names.
10. **Error codes are the documented set**: `PERM_DENIED`, `ELEMENT_NOT_FOUND`,
    `APP_NOT_FOUND`, `ACTION_FAILED`, `ACTION_NOT_SUPPORTED`, `STALE_REF`,
    `AMBIGUOUS_TARGET`, `WINDOW_NOT_FOUND`, `PLATFORM_NOT_SUPPORTED`, `TIMEOUT`,
    `INVALID_ARGS`, `NOTIFICATION_NOT_FOUND`, `SNAPSHOT_NOT_FOUND`, `POLICY_DENIED`,
    `APP_UNRESPONSIVE`, `INTERNAL`. Use `ErrorCode` in `AgentDesktop.Core.Response`.
11. **App-agnostic.** No branches keyed by app name, bundle id, executable, window title, or
    a known app's layout. Use observed capabilities, roles, ownership, process identity.
    App-specific names belong in tests and verification scenarios only.

## Types you must use, not reinvent

Already ported in C# — read them, do not re-create them:

- `AgentDesktop.Core.Adapter` — `PlatformAdapter`, `NativeHandle`, `LiveElement`,
  `LiveIdentity`, `ObservationRoot`, `ObservationRequest`, `ObservedTree`,
  `InteractionLease`, `PermissionReport`, `PermissionState`
- `AgentDesktop.Core.Response` — `Envelope`, `EnvelopeResponse`, `AdapterError`, `ErrorCode`,
  `Recovery`, `RecoveryStrategy`, `Disposition`, `Delivery`, `Retry`
- `AgentDesktop.Core.Tree` — `AccessibilityNode`, `NodeProperties`, `NodeStates`, `Rect`
- `AgentDesktop.Core.Refs` — `RefMap`, `RefEntry`, `SnapshotId`
- `AgentDesktop.Core.Systems` — `AppInfo`, `WindowInfo`, `WindowFilter`, `WindowScope`,
  `ProcessIdentity`, `ProcessId`, `SurfaceInfo`
- `AgentDesktop.Core.Actions` — `ActionRequest`, `ActionResult`, `Action`
- `AgentDesktop.Core.Input` — `KeyCombo`, `MouseEvent`, `DragParams`, `PointerObservation`
- `AgentDesktop.Core.Wait` — `Deadline`
- `AgentDesktop.Windows.Uia` — `UiaClient`, `UiaCalls`, `UiaPropertyIds`, `UiaControlTypeIds`,
  `UiaKeyboardIds`, `UiaSlots`, `UiaVariant`
- `AgentDesktop.Windows.Tree` — `TreeAdapter`, `UiaTreeSource`, `Walk`, `WalkBudget`,
  `Element`, `NodeBuilder`, `RefAllocation`, `Resolve`, `Roles`, `WebWrapper`, `WindowRoot`
- `AgentDesktop.Windows.Actions` — `ActionsAdapter`, `Chain`, `KeyboardLeg`, `ScrollAction`,
  `SelectAction`, `ToggleAndDisclosure`, `ValueWrite`
- `AgentDesktop.Windows.System` — `WindowCensus`, `AppCensus`, `ProcessInventory`,
  `DpiAwareness`, `WindowEnum`, `Interop`

## How to add adapter methods

`WindowsAdapter` is `sealed partial`. Add a partial file named `Adapter.<Slice>.cs` in
`csharp/src/AgentDesktop.Windows/` and put your methods there. **Do not edit
`Adapter.cs`** — other workers are editing the same package in parallel.

If you need a field, declare it in your own partial file.

## Seams

When your slice calls a Rust module you were not assigned, and that module is not yet
ported, write the call against the C# name you expect, and if it does not compile, replace
it with:

```csharp
throw AdapterError.NotSupported("<command>");
```

and record the seam in your final report as `SEAM: <file>:<what is missing>`. Never invent
behaviour to fill a seam.

## Verification before you report

Run, from `csharp/`:

```
dotnet build src/AgentDesktop.Windows/AgentDesktop.Windows.csproj -v q --nologo
dotnet build src/AgentDesktop.Cli/AgentDesktop.Cli.csproj -v q --nologo
dotnet test tests/AgentDesktop.Windows.Tests/AgentDesktop.Windows.Tests.csproj -v q --nologo
```

All must pass with zero warnings. If your tests need real UI Automation, gate them so they
skip in CI (`[Fact(Skip = "requires a live UIA tree")]`) — the harness runs them separately.

## Report format

End with:
- Files created (paths)
- Rust files converted (paths)
- Rust files NOT converted and why
- SEAM lines
- Any Rust behaviour you could not express in C# and what you did instead
