namespace AgentDesktop.Windows.Uia;

/// <summary>COM vtable slots for the UI Automation interfaces this adapter calls, and the
/// pattern ids it probes. Every number is the position or value the Windows SDK header
/// UIAutomationClient.h (10.0.26100.0) declares. probes/windows/uia-aot-probe confirmed the
/// current-property getters with a live read; each cached getter sits 32 slots after its
/// current getter. Nothing checks a slot against the method it names: a wrong number calls
/// a different method with the wrong arguments at runtime, so every number is taken from
/// the header, never derived from a count.</summary>
public static class Slots
{
    public const int PatternInvoke = 10000;

    public const int PatternSelection = 10001;

    public const int PatternValue = 10002;

    public const int PatternRangeValue = 10003;

    public const int PatternScroll = 10004;

    public const int PatternExpandCollapse = 10005;

    public const int PatternSelectionItem = 10010;

    public const int PatternToggle = 10015;

    public const int PatternScrollItem = 10017;

    public const int PatternLegacyIAccessible = 10018;

    public const int ClientCompareElements = 3;

    public const int ClientElementFromHandle = 6;

    public const int ClientElementFromPoint = 7;

    public const int ClientGetFocusedElement = 8;

    public const int ClientRawViewWalker = 16;

    public const int ClientCreateCacheRequest = 20;

    public const int ClientCreateTrueCondition = 21;

    /// <summary>IUIAutomation2 extends IUIAutomation, so its timeouts follow the 58 slots
    /// IUIAutomation declares.</summary>
    public const int Client2GetConnectionTimeout = 60;

    public const int Client2PutConnectionTimeout = 61;

    public const int Client2GetTransactionTimeout = 62;

    public const int Client2PutTransactionTimeout = 63;

    public const int WalkerGetParent = 3;

    public const int WalkerGetFirstChild = 4;

    public const int WalkerGetNextSibling = 6;

    public const int CacheRequestAddProperty = 3;

    public const int CacheRequestPutTreeScope = 7;

    public const int CacheRequestPutTreeFilter = 9;

    public const int ElementSetFocus = 3;

    public const int ElementFindAll = 6;

    public const int ElementFindAllBuildCache = 8;

    public const int ElementBuildUpdatedCache = 9;

    public const int ElementGetCurrentPropertyValue = 10;

    public const int ElementGetCachedPropertyValue = 12;

    public const int ElementGetCurrentPattern = 16;

    public const int ElementGetProcessId = 20;

    public const int ElementGetControlType = 21;

    public const int ElementGetName = 23;

    public const int ElementGetHasKeyboardFocus = 26;

    public const int ElementGetIsKeyboardFocusable = 27;

    public const int ElementGetIsEnabled = 28;

    public const int ElementGetAutomationId = 29;

    public const int ElementGetClassName = 30;

    public const int ElementGetIsOffscreen = 38;

    public const int ElementGetFrameworkId = 40;

    public const int ElementGetBoundingRectangle = 43;

    public const int ElementGetCachedControlType = 53;

    public const int ElementGetCachedName = 55;

    public const int ElementGetCachedHasKeyboardFocus = 58;

    public const int ElementGetCachedIsKeyboardFocusable = 59;

    public const int ElementGetCachedIsEnabled = 60;

    public const int ElementGetCachedAutomationId = 61;

    public const int ElementGetCachedClassName = 62;

    public const int ElementGetCurrentLabeledBy = 44;

    public const int ElementGetCachedLabeledBy = 76;

    public const int ElementGetCachedIsOffscreen = 70;

    public const int ElementGetCachedFrameworkId = 72;

    public const int ElementGetCachedBoundingRectangle = 75;

    public const int InvokePatternInvoke = 3;

    public const int LegacyIAccessiblePatternDoDefaultAction = 4;

    public const int ValuePatternSetValue = 3;

    public const int ValuePatternGetCurrentValue = 4;

    public const int ValuePatternGetCurrentIsReadOnly = 5;

    public const int RangeValuePatternSetValue = 3;

    public const int RangeValuePatternGetCurrentValue = 4;

    public const int RangeValuePatternGetCurrentIsReadOnly = 5;

    public const int TogglePatternToggle = 3;

    public const int TogglePatternGetCurrentToggleState = 4;

    public const int ExpandCollapsePatternExpand = 3;

    public const int ExpandCollapsePatternCollapse = 4;

    public const int ExpandCollapsePatternGetCurrentExpandCollapseState = 5;

    public const int SelectionItemPatternSelect = 3;

    public const int SelectionItemPatternGetCurrentIsSelected = 6;

    public const int SelectionItemPatternGetCurrentSelectionContainer = 7;

    public const int ScrollPatternScroll = 3;

    public const int ScrollPatternSetScrollPercent = 4;

    public const int ScrollPatternGetCurrentHorizontalScrollPercent = 5;

    public const int ScrollPatternGetCurrentVerticalScrollPercent = 6;

    public const int ScrollPatternGetCurrentHorizontalViewSize = 7;

    public const int ScrollPatternGetCurrentVerticalViewSize = 8;

    public const int ScrollPatternGetCurrentHorizontallyScrollable = 9;

    public const int ScrollPatternGetCurrentVerticallyScrollable = 10;

    public const int ScrollItemPatternScrollIntoView = 3;

    public const int ElementArrayLength = 3;

    public const int ElementArrayElement = 4;

    public const int TreeScopeElement = 1;

    public const int TreeScopeChildren = 2;

    public const int TreeScopeDescendants = 4;
}
