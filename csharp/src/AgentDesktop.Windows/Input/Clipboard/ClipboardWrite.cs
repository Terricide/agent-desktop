using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using AgentDesktop.Core.Input;
using AgentDesktop.Core.Response;
using AgentDesktop.Core.Wait;

namespace AgentDesktop.Windows.Input.Clipboard;

/// <summary>
/// Clipboard write transaction: EmptyClipboard, multi-format publish, then an ownership re-check.
/// Marshalling runs before the clipboard is opened, so a failure while encoding never leaves the
/// clipboard emptied. InteractionLease is not mutual exclusion on Windows; the ownership re-check
/// is the load-bearing delivery guarantee.
/// </summary>
internal static partial class ClipboardWrite
{
    private static volatile bool _forceOwnershipLoss;

    /// <summary>
    /// Publishes <paramref name="content"/> as the clipboard's only contents. Every failure after
    /// EmptyClipboard is reported as delivered-unverified, because the previous contents are gone.
    /// </summary>
    internal static void SetClipboardContent(ClipboardContent content, Deadline deadline)
    {
        EnsureRemainingNotDelivered(deadline);
        List<(uint Format, byte[] Bytes)> prepared;
        try
        {
            prepared = PrepareFormats(content);
        }
        catch (AdapterError error)
        {
            throw NotDelivered(error);
        }

        using var session = ClipboardSession.OpenForWrite(deadline);
        var owner = ClipboardSession.OwnerHwnd();
        if (!EmptyClipboard())
        {
            throw EmptyFailure("EmptyClipboard failed before any clipboard format was published");
        }

        var sequenceAfterEmpty = GetClipboardSequenceNumber();
        foreach (var (format, bytes) in prepared)
        {
            PublishFormat(format, bytes, deadline);
        }

        ConfirmWriteOwnership(owner, sequenceAfterEmpty);
    }

    /// <summary>Empties the clipboard so it holds no format. Refusal before EmptyClipboard is not delivered.</summary>
    internal static void ClearClipboard(Deadline deadline)
    {
        EnsureRemainingNotDelivered(deadline);
        using var session = ClipboardSession.OpenForWrite(deadline);
        if (!EmptyClipboard())
        {
            throw EmptyFailure("EmptyClipboard failed");
        }
    }

    /// <summary>Test hook that makes the next ownership re-check report a lost clipboard.</summary>
    internal static void ForceOwnershipLossForTest(bool force)
    {
        _forceOwnershipLoss = force;
    }

    /// <summary>
    /// Encodes every representation before the clipboard is opened. Image and file-list publication are
    /// seams: the C# ImageBuffer and file-URL content are not ported yet, so they refuse with
    /// PLATFORM_NOT_SUPPORTED rather than publishing a partial clipboard.
    /// </summary>
    private static List<(uint Format, byte[] Bytes)> PrepareFormats(ClipboardContent content)
    {
        if (content.Format != ClipboardFormat.Text)
        {
            throw AdapterError.NotSupported("set_clipboard_content");
        }

        var text = content.Text ?? throw AdapterError.InvalidArgs("clipboard text content is missing");
        return [(ClipboardFormats.CfUnicodeText, ClipboardText.EncodeUtf16Text(text))];
    }

    private static void PublishFormat(uint format, byte[] bytes, Deadline deadline)
    {
        EnsureRemainingUnverified(deadline);
        using var guard = AllocateUnverified(bytes);
        guard.SetClipboardData(format);
    }

    private static ClipboardGuard AllocateUnverified(byte[] bytes)
    {
        try
        {
            return ClipboardGuard.FromBytes(bytes);
        }
        catch (AdapterError error)
        {
            throw error.WithDisposition(DeliverySemantics.DeliveredUnverified());
        }
    }

    private static void ConfirmWriteOwnership(IntPtr owner, uint sequenceAfterEmpty)
    {
        if (_forceOwnershipLoss)
        {
            throw UnverifiedDelivery("test injected ownership loss after EmptyClipboard");
        }

        var currentOwner = GetClipboardOwner();
        var sequence = GetClipboardSequenceNumber();
        if (currentOwner == owner && sequence >= sequenceAfterEmpty)
        {
            return;
        }

        throw UnverifiedDelivery(FormattableString.Invariant(
            $"GetClipboardOwner=0x{currentOwner.ToInt64():X} expected=0x{owner.ToInt64():X} sequence={sequence} sequence_after_empty={sequenceAfterEmpty}"));
    }

    private static void EnsureRemainingNotDelivered(Deadline deadline)
    {
        try
        {
            deadline.EnsureRemaining();
        }
        catch (AdapterError error)
        {
            throw NotDelivered(error);
        }
    }

    private static void EnsureRemainingUnverified(Deadline deadline)
    {
        try
        {
            deadline.EnsureRemaining();
        }
        catch (AdapterError error)
        {
            throw error.WithDisposition(DeliverySemantics.DeliveredUnverified());
        }
    }

    private static AdapterError EmptyFailure(string message)
    {
        return new AdapterError(ErrorCode.ActionFailed, message)
            .WithPlatformDetail(FormattableString.Invariant($"GetLastError={Marshal.GetLastWin32Error()}"))
            .WithDisposition(DeliverySemantics.NotDelivered());
    }

    private static AdapterError UnverifiedDelivery(string reason)
    {
        return new AdapterError(ErrorCode.AppUnresponsive, "Clipboard write delivery could not be verified")
            .WithPlatformDetail(reason)
            .WithSuggestion("Inspect the clipboard state before deciding whether to repeat the write")
            .WithDisposition(DeliverySemantics.DeliveredUnverified());
    }

    private static AdapterError NotDelivered(AdapterError error)
    {
        return error.WithDisposition(DeliverySemantics.NotDelivered());
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EmptyClipboard();

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial uint GetClipboardSequenceNumber();

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial IntPtr GetClipboardOwner();
}
