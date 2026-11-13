using System.Runtime.InteropServices;
using AgentDesktop.Windows.Actions;

namespace AgentDesktop.Windows.Uia;

/// <summary>A UI Automation pattern call failed. Carries the failure the write classifier
/// consumes, and names the method and pattern that failed. Derives from COMException so a
/// consumer that catches COMException, as the scroll ladder does, sees the same failure.
/// A pattern an element does not expose carries sentinel 0, the code Rust feeds to
/// `classify_write("get_pattern", label, &error)`; a call the provider refused carries its
/// HRESULT.</summary>
internal sealed class UiaException : COMException
{
    internal UiaException(WriteFailure failure, string method, string mechanism)
        : base($"{method} failed on {mechanism}", failure.Kind == WriteFailureKind.Hresult ? failure.Value : 0)
    {
        Failure = failure;
        Method = method;
        Mechanism = mechanism;
    }

    internal WriteFailure Failure { get; }

    internal string Method { get; }

    internal string Mechanism { get; }
}
