# Windows CLI parity: Rust `agent-desktop.exe` vs the C# port

Worktree `t3-11789549`. Rust is the reference: `target/debug/agent-desktop.exe`.
C# is the port: `csharp/src/AgentDesktop.Cli/bin/Debug/net8.0/agent-desktop.exe`.
The C# CLI must answer the same argv with the same envelope, the same tree, the
same refs, and the same error codes.

## Harness

- `compare.py <args...>`: runs both binaries with the same argv, compares the
  envelope, the data keys, and the flattened tree. The C# CLI needs `--app` where
  Rust accepts `--window-id` alone; `compare.py` adds it.
- `multi.py`: sweeps the live top-level windows over `default`, `--compact`,
  `--interactive-only`, `--skeleton`, and prints node counts, ref counts, the
  first differing rows, and the shape flags (`node-count`, `ref-count`, `complete`).
  It dumps the first differing pair to `p_r.json` / `p_c.json`.
- Both binaries write UTF-8. Compare files as UTF-8: reading them as cp1252 turns
  every non-ASCII glyph into a false diff.

## Findings so far

1. **Ref allocation was role-only in C#. Fixed** (commit `feat(refs): allocate refs
   from the capture census`). Rust allocates a ref only when the capture can prove
   which element the node is: the census counts every element in the window sharing
   a captured identity, a node whose role qualifies is skipped when a proven-unique
   frame covers its subtree, a frame is given a ref when the census proves it, and a
   ref action refuses a ref whose proof is not unique with `AMBIGUOUS_TARGET`. The C#
   port now has `CaptureCensus`, `SkeletonAnchors`, `FullPredicate`, `ElementToken`,
   and `InteractiveRoles.QualifiesForRef(node)`, and `ResolveElementStrict` refuses
   non-unique proof. 73 tests pass.
2. **Notepad parity is byte-identical** for `snapshot --window-id <id> --app Notepad.exe`
   (68 nodes, ref_count 28, 0 diffs) and for `--compact` / `--interactive-only`.
3. **Dense windows truncate in Rust and complete in C#** (Edge, T3 Code). Rust's
   default-mode tree is smaller and reports `complete: false` with
   `subtree_truncated: true`; C# reads the same window inside the same 5 s deadline
   and reports `complete: true`. Both satisfy the contract (a budget-starved snapshot
   returns a partial tree, not a failure), so this is a read-cost difference, not a
   contract gap: Rust reads more attributes per node. C# being faster is not a defect.
4. **`--skeleton` was not applied**: the CLI parses the flag, `ObservationRequest` clamps the
   logical depth to 3, and the Windows adapter built its walk budget from the raw tree options,
   so the clamp never reached the walk. Fixed: `Tree/Adapter.cs` builds the budget from the
   validated request. `snapshot --skeleton` on Notepad now gives 39 nodes at depth 3 in both.
5. **Ref eligibility was over-restricted in C#. Fixed**: Rust gives a ref to every node whose
   role qualifies, plus each skeleton anchor, and the census only records `unique_at_capture` on
   the entry. The port suppressed refs for proven nodes, so skeleton ref_count was 20 against
   Rust's 21. Default Notepad is byte-identical (68 nodes, 28 refs, 0 diffs).
6. **Open: `children_count` at a logical-depth boundary, and the child enumeration path.** At
   depth 3 on Notepad, Rust reports `children_count` on 14 nodes; C# reports it on 2. The two that
   agree are `tablist` (2) and `menu` (3), whose children are control-view elements (`tab`,
   `listitem`). Every node where C# reports no count is a `menubutton` or `switch`, and the child
   the C# walk sees when depth allows it (`--max-depth 4`) is a `statictext` carrying a glyph
   (`\uea19`, `\ue8dd`). Rust's boundary read is a `first_child`/`next_sibling` walk on the raw
   view, so it counts that child; the C# live source counts with
   `IUIAutomationElement::FindAll(TreeScope_Children, TrueCondition)`, which walks the control
   view and does not see a content-view-only child. The count-only path is not the suspect: the
   two paths differ only in the cache request, and the same source reports the child when depth
   allows it, so the enumeration scope is what differs.

   The fix is app-agnostic and platform-wide: the live UIA source must enumerate children through
   the raw-view `IUIAutomationTreeWalker` (`WalkerGetFirstChild` slot 4, `WalkerGetNextSibling`
   slot 6, already in `Uia/Slots.cs`), the way the Rust adapter does, and the boundary count must
   come from that enumeration completing. A probe through PowerShell cannot measure this here:
   `AutomationElement` from a shell in this session sees no Notepad window, so the measurement has
   to go through the shipped binaries, which do see it.

## Harness trap

The C# CLI targets `net8.0-windows`, so its binary is
`csharp/src/AgentDesktop.Cli/bin/Debug/net8.0-windows/agent-desktop.exe`. An older
`bin/Debug/net8.0` copy is stale and does not get refreshed by `dotnet build`. Always run the
`net8.0-windows` path; running the stale copy hides fixes and looks like a code defect.

## Closed: the children_count gap (commit e8ab96e)

The live walk counted children with `FindAll(TreeScope_Children, TrueCondition)`, which walks the
**control view**. A node whose only children are content-view elements — a glyph carried by a
`statictext` under a button, which is what Notepad's toolbar and status bar are made of — reports
no children there, so the snapshot said a button had an empty subtree. `UiaTreeSource` now
implements `FirstChild`/`NextSibling` on the raw-view `IUIAutomationTreeWalker` (slot 4, slot 6),
and `Walk` classifies every step: exhaustion ends the list and leaves the subtree complete, any
other fault marks it incomplete. A batching source builds the read set onto each walker element
(`ElementBuildUpdatedCache`, slot 9) so the per-node read set rides the cache as it did under
`FindAllBuildCache`.

Notepad, same window, Rust vs C#: 68 nodes / 28 refs at default depth, 56 / 24 at `--max-depth 4`,
39 / 21 at 3, 14 / 6 at 2 — identical in both, **0 diffs** at every depth. The 14 `children_count`
rows that were the last Notepad gap are gone. 287 tests pass.

## Open: dense windows diverge (Explorer, Edge)

Sweep on live Explorer and Edge, `parity/multi.txt`:

- Explorer default: Rust 19 nodes / 3 refs, `complete: false`; C# 324 / 252, `complete: true`.
- Explorer `--max-depth 3`: 21 nodes both, refs 11 vs 14, 14 diffs.
- Edge default: Rust 22 / 9; C# 594 / 299. `--max-depth 3`: 121 vs 229, refs 76 vs 112.

Two separate causes, one measured, one not:

1. **Rust times out; C# completes.** On Explorer Rust returns `complete: false` with
   `nodes_observed: 19` and truncation markers at `Folder Layout Pane`, `Control Host`,
   `Navigation Pane`, `treeitem Desktop (OneDrive)`. C# finished 324 nodes inside the same
   deadline. C# is faster on the raw-view walk; Rust is the slower side here. The parity
   question is what Rust's budget is, not what the tree is. Rust's root reports 2 children and
   C# 4: a deadline-cut sibling list gives an unknowable count, which is the shape Rust's
   truncation marker carries.
2. **Skeleton anchors.** C# gives `group 'Explorer Pane'` (role `group`, no advertised actions,
   `native_id` automation_id `main`) the ref `@e1`; Rust gives it none. `SnapshotCommand.QualifiesForRef`
   admits a node when `context.Anchors.Contains(PathKey(path))` on top of
   `InteractiveRoles.QualifiesForRef`, so a depth-boundary node with identity evidence becomes an
   anchor in C#. Rust offers a boundary as an anchor only under its own narrower rule. C#
   over-anchors on dense windows. Fix the anchor set in `SnapshotCommand` against Rust's
   `crates/windows/src/tree/walk/walker_enumerate.rs` boundary offer, not by widening
   `InteractiveRoles`.

Do not read the Explorer/Edge node-count gap as one defect. Split it: budget first, anchors second.

## Next slices, in order

1. Wire `--skeleton` into the C# `snapshot` command: prune to the skeleton anchors the
   plan names, matching Rust's node count and ref count on a dense window.
2. Match the boundary budgets: `max_raw_depth` (Rust default 50), the logical-depth
   ceiling, and `BOUNDARY_COUNT_BUDGET` for a boundary node's child read, so a dense
   window truncates at the same place in both.
3. `list-windows` / `list-apps` field parity on live windows (bounds, states, minimized,
   title), which the sweep depends on.
4. Error-code parity for the failure paths: dead pid, window id of another app, stale
   ref, ambiguous ref, `WINDOW_NOT_FOUND`, `POLICY_DENIED`.
5. Batch, `wait`, `trace`, `session` namespaces, and the FFI header contract.
