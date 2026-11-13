using System;
using System.Threading;
using AgentDesktop.Core.Response;
using AgentDesktop.Core.Wait;

namespace AgentDesktop.Windows.System.Capture;

/// <summary>Capture backend seam: support predicate, modern entry, and precedence.
/// <para>
/// Production prefers Modern (<c>Windows.Graphics.Capture</c>) when <see cref="ModernIsSupported"/> is true, and
/// falls back to Legacy on unsupported or modern failure. Tests force unavailable or fail-after-available through
/// <see cref="CaptureBackendHooks"/>.
/// </para>
/// </summary>
internal static class CaptureBackend
{
    private static readonly TimeSpan LegacyDeadlineFloor = TimeSpan.FromMilliseconds(200);

    /// <summary>Whether the modern capture backend can serve this session.</summary>
    internal static bool ModernIsSupported()
    {
        if (CaptureBackendHooks.ForceUnsupported)
        {
            return false;
        }

        if (CaptureBackendHooks.ForceFailAfterAvailable)
        {
            return true;
        }

        return ModernAvailability();
    }

    /// <summary>Modern capture entry. Delegates to the WGC backend.</summary>
    internal static byte[] CaptureModern(CaptureSubject subject, Deadline deadline)
    {
        deadline.EnsureRemaining();
        if (CaptureBackendHooks.ForceFailAfterAvailable)
        {
            if (CaptureBackendHooks.ConsumeModernSlice)
            {
                BurnDeadline(deadline);
            }

            throw new AdapterError(
                ErrorCode.ActionFailed,
                "modern capture forced failure after reporting available");
        }

        if (subject.Kind == CaptureSubjectKind.Window)
        {
            return ModernWindowCapture(subject.Handle, subject.ScaleFactor, deadline);
        }

        return ModernDisplayCapture(subject.Index, deadline);
    }

    internal static byte[] CaptureLegacy(CaptureSubject subject, Deadline deadline)
    {
        if (subject.Kind == CaptureSubjectKind.Window)
        {
            return LegacyWindowCapture(subject.Handle, subject.ScaleFactor, deadline);
        }

        return LegacyDisplayCapture(subject.Index, deadline);
    }

    /// <summary>Prefers Modern when supported; falls back to Legacy on unsupported or failure.</summary>
    internal static byte[] CaptureWithPrecedence(CaptureSubject subject, Deadline deadline)
    {
        deadline.EnsureRemaining();
        AdapterError? modernFailure = null;
        if (ModernIsSupported())
        {
            var modernDeadline = ModernDeadlineSlice(deadline);
            try
            {
                return CaptureModern(subject, modernDeadline);
            }
            catch (AdapterError error)
            {
                modernFailure = error;
            }
        }

        deadline.EnsureRemaining();
        try
        {
            return CaptureLegacy(subject, deadline);
        }
        catch (AdapterError legacyError)
        {
            throw AttachModernFailure(legacyError, modernFailure);
        }
    }

    /// <summary>Names the Modern attempt in the error the caller actually receives.
    /// <para>
    /// When Legacy succeeds the Modern failure is a routing detail and belongs in the debug log only. When both fail,
    /// reporting Legacy's error alone describes the fallback and hides the first refusal, and the two rarely fail for
    /// the same reason, so an operator handed only the second one is told the less useful half of what went wrong.
    /// </para>
    /// </summary>
    internal static AdapterError AttachModernFailure(AdapterError legacyError, AdapterError? modernFailure)
    {
        if (modernFailure is null)
        {
            return legacyError;
        }

        var detail = legacyError.PlatformDetail is { } existing
            ? $"{existing}; modern capture first failed: {modernFailure.Message}"
            : $"modern capture first failed: {modernFailure.Message}";
        return legacyError.WithPlatformDetail(detail);
    }

    private static Deadline ModernDeadlineSlice(Deadline deadline)
    {
        if (CaptureBackendHooks.DisableDeadlineSlice)
        {
            return deadline;
        }

        var remaining = TimeSpan.FromMilliseconds(deadline.RemainingMs());
        if (remaining <= LegacyDeadlineFloor)
        {
            return CapDeadline(deadline, TimeSpan.Zero);
        }

        return CapDeadline(deadline, remaining - LegacyDeadlineFloor);
    }

    private static void BurnDeadline(Deadline deadline)
    {
        while (!deadline.IsExpired())
        {
            var remaining = deadline.RemainingMs();
            if (remaining <= 0)
            {
                break;
            }

            Thread.Sleep(TimeSpan.FromMilliseconds(Math.Min(5, remaining)));
        }
    }

    /// <summary>Whether the WGC backend can serve this session. The activation surface is not ported, so the
    /// backend reports itself unsupported. The predicate never refuses: the availability probe reads it to
    /// decide the screen recording state, and a refusal there would turn an unavailable backend into an error
    /// that fails a read-only permission report.</summary>
    private static bool ModernAvailability() => false;

    /// <summary>SEAM: <c>system::capture_modern::capture_window</c> is not ported. It captures one window handle at
    /// a scale through the WGC backend.</summary>
    private static byte[] ModernWindowCapture(IntPtr handle, double scale, Deadline deadline) =>
        throw AdapterError.NotSupported("screenshot");

    /// <summary>SEAM: <c>system::capture_modern::capture_display</c> is not ported. It captures one display index
    /// through the WGC backend.</summary>
    private static byte[] ModernDisplayCapture(int index, Deadline deadline) =>
        throw AdapterError.NotSupported("screenshot");

    /// <summary>SEAM: <c>system::capture_window::capture_window</c> and <c>native_call::capture</c> are not ported.
    /// The Legacy window capture runs on the native-call worker under the deadline.</summary>
    private static byte[] LegacyWindowCapture(IntPtr handle, double scale, Deadline deadline) =>
        throw AdapterError.NotSupported("screenshot");

    /// <summary>SEAM: <c>system::capture_display::capture_display_at</c> is not ported.</summary>
    private static byte[] LegacyDisplayCapture(int index, Deadline deadline) =>
        throw AdapterError.NotSupported("screenshot");

    /// <summary>SEAM: <c>Deadline::capped</c> has no C# counterpart on <see cref="Deadline"/>. It returns a deadline
    /// whose expiry is capped at the given span from now.</summary>
    private static Deadline CapDeadline(Deadline deadline, TimeSpan span) =>
        throw AdapterError.NotSupported("screenshot");
}

/// <summary>Which surface a capture subject names.</summary>
internal enum CaptureSubjectKind
{
    Window,
    Display,
}

/// <summary>The capture subject: a window handle at its scale factor, or a display index. Only the fields the named
/// kind uses are meaningful.</summary>
internal sealed record CaptureSubject(CaptureSubjectKind Kind, IntPtr Handle, double ScaleFactor, int Index)
{
    internal static CaptureSubject ForWindow(IntPtr handle, double scaleFactor) =>
        new(CaptureSubjectKind.Window, handle, scaleFactor, 0);

    internal static CaptureSubject ForDisplay(int index) =>
        new(CaptureSubjectKind.Display, IntPtr.Zero, 0.0, index);
}

/// <summary>Test levers for the capture precedence. Each flag is thread-local so a test scope cannot leak into
/// another thread's capture.</summary>
internal static class CaptureBackendHooks
{
    [ThreadStatic]
    private static bool forceUnsupported;

    [ThreadStatic]
    private static bool forceFailAfterAvailable;

    [ThreadStatic]
    private static bool consumeModernSlice;

    [ThreadStatic]
    private static bool disableDeadlineSlice;

    internal static bool ForceUnsupported => forceUnsupported;

    internal static bool ForceFailAfterAvailable => forceFailAfterAvailable;

    internal static bool ConsumeModernSlice => consumeModernSlice;

    internal static bool DisableDeadlineSlice => disableDeadlineSlice;

    internal static R WithForceUnsupported<R>(Func<R> run) => WithFlag(ref forceUnsupported, run);

    internal static R WithForceFailAfterAvailable<R>(Func<R> run) => WithFlag(ref forceFailAfterAvailable, run);

    internal static R WithConsumeModernSlice<R>(Func<R> run) => WithFlag(ref consumeModernSlice, run);

    internal static R WithDisableDeadlineSlice<R>(Func<R> run) => WithFlag(ref disableDeadlineSlice, run);

    /// <summary>Sets the flag for the duration of <paramref name="run"/> and restores the prior value afterwards.</summary>
    private static R WithFlag<R>(ref bool flag, Func<R> run)
    {
        var previous = flag;
        flag = true;
        try
        {
            return run();
        }
        finally
        {
            flag = previous;
        }
    }
}
