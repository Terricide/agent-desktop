using System.Text.Json.Nodes;
using AgentDesktop.Core.Response;
using Xunit;

namespace AgentDesktop.Core.Tests.Response;

public class EnvelopeTests
{
    [Fact]
    public void SuccessEnvelopeCarriesVersionAndCommand()
    {
        var json = Envelope.Ok("snapshot", JsonNode.Parse("""{"ref_count": 14}""")).ToJsonString();

        Assert.Contains("\"version\":\"2.6\"", json);
        Assert.Contains("\"ok\":true", json);
        Assert.Contains("\"command\":\"snapshot\"", json);
    }

    [Fact]
    public void ErrorEnvelopeCarriesCodeAndRecovery()
    {
        var json = Envelope.Err("click", AdapterError.Stale("e1")).ToJsonString();

        Assert.Contains("\"ok\":false", json);
        Assert.Contains("\"code\":\"STALE_REF\"", json);
        Assert.Contains("refresh_snapshot_then_retry_original", json);
    }

    [Fact]
    public void NoneFieldsAreOmitted()
    {
        var json = Envelope
            .Err("click", AdapterError.FromCode(ErrorCode.ElementNotFound, "Element @e9 is gone"))
            .ToJsonString();

        Assert.DoesNotContain("suggestion", json);
        Assert.DoesNotContain("recovery", json);
        Assert.DoesNotContain("platform_detail", json);
    }

    [Fact]
    public void SuccessExitsZero()
    {
        var envelope = Envelope.Ok("snapshot", JsonNode.Parse("{}"));

        Assert.Equal(ExitCodes.Success, Envelope.ExitCode(envelope));
    }

    [Fact]
    public void StructuredErrorExitsOne()
    {
        var envelope = Envelope.Err("click", AdapterError.Stale("e1"));

        Assert.Equal(ExitCodes.Error, Envelope.ExitCode(envelope));
    }
}
