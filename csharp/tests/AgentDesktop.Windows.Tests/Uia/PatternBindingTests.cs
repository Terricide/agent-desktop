using System.Runtime.InteropServices;
using AgentDesktop.Core.Response;
using AgentDesktop.Windows.Actions;
using AgentDesktop.Windows.Tree;
using AgentDesktop.Windows.Uia;
using Xunit;

namespace AgentDesktop.Windows.Tests;

/// <summary>Pattern binding for the write path, the C# shape of Rust
/// `get_pattern::<T>()`. The fakes are COM-shaped vtables in native memory, so every call
/// goes through the same slot read the live path uses: the pattern id the element is queried
/// with, the pointer it returns, the code a refused write carries, and the release on
/// disposal.</summary>
public sealed class PatternBindingTests
{
    /// <summary>A live pattern pointer the element returns is bound into the interface the
    /// call site asked for, and its method calls the slot the specification fixes.</summary>
    [Fact]
    void GetPatternBindsThePointerTheElementReturned()
    {
        using var pattern = new FakeCom(4);
        using var element = new FakeCom(17);
        FakeCom.LivePattern = pattern.Pointer;
        element.SetSlot(Slots.ElementGetCurrentPattern, FakeCom.LivePatternGetter);
        pattern.SetSlot(Slots.TogglePatternToggle, FakeCom.SucceedingMethod);

        var bound = new Element(element.Pointer).GetPattern<ITogglePattern>();
        bound.Toggle();

        Assert.True(bound is TogglePattern);
    }

    /// <summary>A pattern the element does not expose is sentinel 0, the code Rust feeds to
    /// `classify_write("get_pattern", label, &error)`, and the classifier reads it as absent
    /// so the chain falls through.</summary>
    [Fact]
    void GetPatternCarriesSentinelZeroWhenTheElementExposesNoPattern()
    {
        using var element = new FakeCom(17);
        element.SetSlot(Slots.ElementGetCurrentPattern, FakeCom.AbsentPatternGetter);

        var failure = RecordFailure(() => new Element(element.Pointer).GetPattern<ITogglePattern>());

        Assert.Equal(WriteFailureKind.Sentinel, failure.Kind);
        Assert.Equal(0, failure.Value);
        Assert.False(Mutation.ClassifyWrite("get_pattern", TogglePattern.Label, failure));
    }

    /// <summary>A write a disabled control refused reaches the classifier as its own
    /// HRESULT, the same code Rust hands `classify_write`, and lands as a delivered-no
    /// action failure.</summary>
    [Fact]
    void PatternMethodFailureCarriesItsHresultToTheClassifier()
    {
        using var pattern = new FakeCom(4);
        using var element = new FakeCom(17);
        FakeCom.LivePattern = pattern.Pointer;
        element.SetSlot(Slots.ElementGetCurrentPattern, FakeCom.LivePatternGetter);
        pattern.SetSlot(Slots.TogglePatternToggle, FakeCom.FailingMethod);

        var failure = RecordFailure(() => new Element(element.Pointer).GetPattern<ITogglePattern>().Toggle());

        Assert.Equal(WriteFailureKind.Hresult, failure.Kind);
        Assert.Equal(FakeCom.ElementNotEnabled, failure.Value);
        var error = Assert.Throws<AdapterError>(() =>
            Mutation.ClassifyWrite("Toggle", TogglePattern.Label, failure));
        Assert.Equal(ErrorCode.ActionFailed, error.Code);
    }

    /// <summary>A provider that died mid-write is read as a stale reference, the same
    /// reading Rust gives UIA_E_ELEMENTNOTAVAILABLE.</summary>
    [Fact]
    void ProviderDeathOnSelectIsAStaleReference()
    {
        using var pattern = new FakeCom(4);
        using var element = new FakeCom(17);
        FakeCom.LivePattern = pattern.Pointer;
        element.SetSlot(Slots.ElementGetCurrentPattern, FakeCom.LivePatternGetter);
        pattern.SetSlot(Slots.SelectionItemPatternSelect, FakeCom.ProviderDiedMethod);

        var failure = RecordFailure(() => new Element(element.Pointer).GetPattern<ISelectionItemPattern>().Select());

        var error = Assert.Throws<AdapterError>(() =>
            Mutation.ClassifyWrite("Select", SelectionItemPattern.Label, failure));
        Assert.Equal(ErrorCode.StaleRef, error.Code);
    }

    /// <summary>The bound pattern owns the pointer the element returned and releases it
    /// exactly once, the way Rust's pattern drop releases the interface.</summary>
    [Fact]
    void DisposalReleasesThePatternPointerOnce()
    {
        using var element = new FakeCom(17);
        FakeCom.LivePattern = IntPtr.Zero;
        FakeCom.ReleaseCount = 0;
        using var pattern = new FakeCom(4);
        FakeCom.LivePattern = pattern.Pointer;
        element.SetSlot(Slots.ElementGetCurrentPattern, FakeCom.LivePatternGetter);

        var bound = new Element(element.Pointer).GetPattern<ITogglePattern>();
        Assert.Equal(0, FakeCom.ReleaseCount);
        ((IDisposable)bound).Dispose();
        ((IDisposable)bound).Dispose();

        Assert.Equal(1, FakeCom.ReleaseCount);
    }

    /// <summary>The scroll surface the ladder gates on: both axis flags and both percents
    /// come from the slots the specification fixes for them.</summary>
    [Fact]
    void ScrollPatternReadsItsAxisFlagsAndPercentsThroughTheSlots()
    {
        using var pattern = new FakeCom(11);
        using var element = new FakeCom(17);
        FakeCom.LivePattern = pattern.Pointer;
        element.SetSlot(Slots.ElementGetCurrentPattern, FakeCom.LivePatternGetter);
        pattern.SetSlot(Slots.ScrollPatternGetCurrentVerticallyScrollable, FakeCom.VerticallyScrollableGetter);
        pattern.SetSlot(Slots.ScrollPatternGetCurrentHorizontallyScrollable, FakeCom.HorizontallyScrollableGetter);
        pattern.SetSlot(Slots.ScrollPatternGetCurrentVerticalScrollPercent, FakeCom.VerticalPercentGetter);
        pattern.SetSlot(Slots.ScrollPatternGetCurrentHorizontalScrollPercent, FakeCom.HorizontalPercentGetter);

        var scroll = new Element(element.Pointer).GetScrollPattern();

        Assert.True(scroll.IsVerticallyScrollable());
        Assert.False(scroll.IsHorizontallyScrollable());
        Assert.Equal(33.5, scroll.VerticalScrollPercent());
        Assert.Equal(44.5, scroll.HorizontalScrollPercent());
    }

    /// <summary>The Scroll call passes both amounts to the pattern slot, the pair the ladder
    /// maps from one direction.</summary>
    [Fact]
    void ScrollPatternPassesBothAmountsToItsSlot()
    {
        using var pattern = new FakeCom(11);
        using var element = new FakeCom(17);
        FakeCom.LivePattern = pattern.Pointer;
        element.SetSlot(Slots.ElementGetCurrentPattern, FakeCom.LivePatternGetter);
        pattern.SetSlot(Slots.ScrollPatternScroll, FakeCom.ScrollMethodPointer);

        var scroll = new Element(element.Pointer).GetScrollPattern();
        scroll.Scroll(Scroll.ScrollAmount.NoAmount, Scroll.ScrollAmount.SmallIncrement);

        Assert.Equal((int)Scroll.ScrollAmount.NoAmount, FakeCom.FirstArgument);
        Assert.Equal((int)Scroll.ScrollAmount.SmallIncrement, FakeCom.SecondArgument);
    }

    /// <summary>A zero element pointer is never called through: the read reports the
    /// sentinel the tree source gives a zero element, and no slot is invoked.</summary>
    [Fact]
    void ZeroElementPointerIsNeverCalledThrough()
    {
        FakeCom.ReleaseCount = 0;

        var failure = RecordFailure(() => new Element(IntPtr.Zero).GetPattern<ITogglePattern>());

        Assert.Equal(WriteFailureKind.Hresult, failure.Kind);
        Assert.Equal(UiaTreeSource.InvalidPointer, failure.Value);
        Assert.Equal(0, FakeCom.ReleaseCount);
    }

    /// <summary>The scroll ladder catches COMException, so the pattern failure a write
    /// raises is visible to that catch, and the select path's catch on UiaException sees
    /// the same object.</summary>
    [Fact]
    void PatternFailureIsVisibleToBothConsumerCatches()
    {
        var failure = new UiaException(WriteFailure.FromHresult(FakeCom.ElementNotEnabled), "Toggle", TogglePattern.Label);

        Assert.IsAssignableFrom<COMException>(failure);
        WriteFailure carried = failure;
        Assert.Equal(FakeCom.ElementNotEnabled, carried.Value);
    }

    /// <summary>An interface the adapter does not bind is refused before any element is
    /// read, so no pattern lookup runs on a type with no slot table.</summary>
    [Fact]
    void AnUnboundPatternInterfaceIsRefused()
    {
        using var element = new FakeCom(17);

        var error = Assert.Throws<AdapterError>(() =>
            new Element(element.Pointer).GetPattern<NotAPattern>());

        Assert.Equal(ErrorCode.ActionNotSupported, error.Code);
    }

    private interface NotAPattern
    {
        void Do();
    }

    private static WriteFailure RecordFailure(Action write)
    {
        try
        {
            write();
            throw new Xunit.Sdk.XunitException("the write was expected to fail");
        }
        catch (UiaException error)
        {
            return error.Failure;
        }
    }
}
