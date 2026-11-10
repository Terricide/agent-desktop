using System.Runtime.InteropServices;
using AgentDesktop.Windows.Uia;

namespace AgentDesktop.Windows.Actions;

/// <summary>A failed UI Automation write: the HRESULT it returned, or the sentinel code an
/// empty pattern or lookup reports in place of one.</summary>
internal readonly record struct WriteFailure(WriteFailureKind Kind, int Value)
{
    internal static WriteFailure FromHresult(int hresult) => new(WriteFailureKind.Hresult, hresult);

    internal static WriteFailure FromSentinel(int sentinel) => new(WriteFailureKind.Sentinel, sentinel);

    /// <summary>A pattern failure carries the code the classifier consumes, sentinel or
    /// HRESULT, with no reinterpretation.</summary>
    public static implicit operator WriteFailure(UiaException error) => error.Failure;

    /// <summary>A pattern call that raised a COM exception is read as the HRESULT it
    /// carries, the same code Rust hands to `classify_write`.</summary>
    public static implicit operator WriteFailure(COMException error) => FromHresult(error.HResult);
}
