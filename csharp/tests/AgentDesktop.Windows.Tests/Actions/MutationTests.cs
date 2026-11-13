using AgentDesktop.Core.Response;
using AgentDesktop.Windows.Actions;
using Xunit;

namespace AgentDesktop.Windows.Tests.Actions;

public sealed class MutationTests
{
    private static AdapterError ExpectErr(WriteFailure failure, ErrorCode code, Delivery delivery, Retry retry)
    {
        var error = Assert.Throws<AdapterError>(() => Mutation.ClassifyMutation("SetValue", "ValuePattern.SetValue", failure));
        Assert.Equal(code, error.Code);
        Assert.Equal(delivery, error.Disposition.Delivery);
        Assert.Equal(retry, error.Disposition.Retry);
        return error;
    }

    [Fact]
    public void SuccessHelperIsDelivered()
    {
        Assert.True(Mutation.ClassifySuccess());
    }

    [Fact]
    public void NotSupportedIsAbsenceNeverErr()
    {
        Assert.False(Mutation.ClassifyMutation("Invoke", "InvokePattern.Invoke", WriteFailure.FromHresult(Mutation.UiaENotSupported)));
    }

    [Fact]
    public void EmptyPatternSentinelIsAbsenceNeverErr()
    {
        Assert.False(Mutation.ClassifyMutation("get_pattern", "UIScrollItemPattern", WriteFailure.FromSentinel(Mutation.ErrNone)));
    }

    [Fact]
    public void AccessDeniedIsPermDeniedNotDelivered()
    {
        ExpectErr(WriteFailure.FromHresult(Mutation.EAccessDenied), ErrorCode.PermDenied, Delivery.NotDelivered, Retry.Safe);
    }

    [Fact]
    public void ElementNotAvailableIsStaleNotDeliveredWithRefreshSuggestion()
    {
        var error = Assert.Throws<AdapterError>(() =>
            Mutation.ClassifyMutation("Invoke", "InvokePattern.Invoke", WriteFailure.FromHresult(Mutation.UiaEElementNotAvailable)));
        Assert.Equal(ErrorCode.StaleRef, error.Code);
        Assert.Equal(Delivery.NotDelivered, error.Disposition.Delivery);
        Assert.Equal(Retry.Safe, error.Disposition.Retry);
        Assert.Contains("Refresh the snapshot", error.Suggestion);
    }

    [Fact]
    public void InvalidArgIsInvalidArgsNotDelivered()
    {
        ExpectErr(WriteFailure.FromHresult(Mutation.EInvalidArg), ErrorCode.InvalidArgs, Delivery.NotDelivered, Retry.Safe);
    }

    [Fact]
    public void ElementNotEnabledIsActionFailedNotDelivered()
    {
        ExpectErr(WriteFailure.FromHresult(Mutation.UiaEElementNotEnabled), ErrorCode.ActionFailed, Delivery.NotDelivered, Retry.Safe);
    }

    [Fact]
    public void TransportServerFaultIsAppUnresponsiveUncertain()
    {
        ExpectErr(WriteFailure.FromHresult(Mutation.RpcEServerFault), ErrorCode.AppUnresponsive, Delivery.DeliveryUncertain, Retry.Unsafe);
    }

    [Fact]
    public void TransportDisconnectedIsAppUnresponsiveUncertain()
    {
        ExpectErr(WriteFailure.FromHresult(Mutation.RpcEDisconnected), ErrorCode.AppUnresponsive, Delivery.DeliveryUncertain, Retry.Unsafe);
    }

    [Fact]
    public void TransportServerUnavailableIsAppUnresponsiveUncertain()
    {
        ExpectErr(WriteFailure.FromHresult(Mutation.RpcSServerUnavailable), ErrorCode.AppUnresponsive, Delivery.DeliveryUncertain, Retry.Unsafe);
    }

    [Fact]
    public void TransportCallFailedIsAppUnresponsiveUncertain()
    {
        ExpectErr(WriteFailure.FromHresult(Mutation.RpcSCallFailed), ErrorCode.AppUnresponsive, Delivery.DeliveryUncertain, Retry.Unsafe);
    }

    [Fact]
    public void TimeoutIsTimeoutUncertain()
    {
        var error = Assert.Throws<AdapterError>(() =>
            Mutation.ClassifyMutation("ScrollIntoView", "ScrollItemPattern.ScrollIntoView", WriteFailure.FromHresult(Mutation.UiaETimeout)));
        Assert.Equal(ErrorCode.Timeout, error.Code);
        Assert.Equal(Delivery.DeliveryUncertain, error.Disposition.Delivery);
        Assert.Equal(Retry.Unsafe, error.Disposition.Retry);
        Assert.Contains("Inspect the target state", error.Suggestion);
    }

    [Fact]
    public void UnclassifiedHresultIsActionFailedUncertain()
    {
        ExpectErr(WriteFailure.FromHresult(unchecked((int)0x8013_1509)), ErrorCode.ActionFailed, Delivery.DeliveryUncertain, Retry.Unsafe);
    }

    [Fact]
    public void UnclassifiedSentinelIsActionFailedUncertain()
    {
        ExpectErr(WriteFailure.FromSentinel(Mutation.ErrNotFound), ErrorCode.ActionFailed, Delivery.DeliveryUncertain, Retry.Unsafe);
    }

    [Fact]
    public void ClassifyWriteEmptyPatternSentinelIsAbsenceNotAnError()
    {
        Assert.False(Mutation.ClassifyWrite("get_pattern", "UIScrollItemPattern", WriteFailure.FromSentinel(Mutation.ErrNone)));
    }

    [Fact]
    public void ClassifyWriteNonExhaustionSentinelIsDelegatedToTheMutationTable()
    {
        var error = Assert.Throws<AdapterError>(() =>
            Mutation.ClassifyWrite("Invoke", "InvokePattern.Invoke", WriteFailure.FromSentinel(Mutation.ErrNotFound)));
        Assert.Equal(ErrorCode.ActionFailed, error.Code);
        Assert.Equal(Delivery.DeliveryUncertain, error.Disposition.Delivery);
    }

    [Fact]
    public void ClassifyWriteHresultFailureIsDelegatedToTheMutationTable()
    {
        var error = Assert.Throws<AdapterError>(() =>
            Mutation.ClassifyWrite("SetValue", "ValuePattern.SetValue", WriteFailure.FromHresult(Mutation.EAccessDenied)));
        Assert.Equal(ErrorCode.PermDenied, error.Code);
        Assert.Equal(Delivery.NotDelivered, error.Disposition.Delivery);
    }

    [Fact]
    public void WritePathSourcesNeverReachTheReadClassificationTable()
    {
        var banned = new[] { "ClassifyReadHresult", "HresultRecord", "UiaFailureDisposition" };
        foreach (var source in ActionSources.Scan())
        {
            foreach (var raw in source.Value.Split('\n'))
            {
                var line = raw.Trim();
                var isProse = line.StartsWith("///") || line.StartsWith("//!");
                foreach (var name in banned)
                {
                    Assert.True(isProse || !line.Contains(name), $"{source.Key} must not consult {name}: {line}");
                }
            }
        }

        var mutation = File.ReadAllText(Path.Combine(ActionSources.ActionsDirectory(), "Mutation.cs"));
        Assert.Contains("UiaFailure.Detail", mutation);
    }

    [Fact]
    public void ActionsNeverConstructStaleViaAdapterErrorStaleHelpers()
    {
        var banned = new[] { "AdapterError.Stale(", "AdapterError.StaleBecause(" };
        foreach (var source in ActionSources.Scan())
        {
            foreach (var raw in source.Value.Split('\n'))
            {
                var line = raw.Trim();
                var isProse = line.StartsWith("///") || line.StartsWith("//!");
                foreach (var name in banned)
                {
                    Assert.True(isProse || !line.Contains(name), $"{source.Key} must not call {name}: {line}");
                }
            }
        }

        var mutation = File.ReadAllText(Path.Combine(ActionSources.ActionsDirectory(), "Mutation.cs"));
        Assert.Contains("ErrorCode.StaleRef", mutation);
    }
}

/// <summary>Scans every action source file for names that must stay on the read path.</summary>
internal static class ActionSources
{
    internal static string ActionsDirectory([global::System.Runtime.CompilerServices.CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", "..", "src", "AgentDesktop.Windows", "Actions"));

    internal static IEnumerable<KeyValuePair<string, string>> Scan()
    {
        var actions = ActionsDirectory();
        foreach (var file in Directory.GetFiles(actions, "*.cs"))
        {
            yield return new KeyValuePair<string, string>(Path.GetFileName(file), File.ReadAllText(file));
        }
    }
}
