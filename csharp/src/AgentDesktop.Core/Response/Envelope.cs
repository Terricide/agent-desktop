using System.Text.Json.Nodes;



namespace AgentDesktop.Core.Response;

/// <summary>Builds the single JSON envelope every command prints. Null fields are omitted.</summary>
public static class Envelope
{
    public const string Version = "2.6";

    /// <summary>The exit code the process returns for an envelope: success, structured error, argument or parse error.</summary>
    public static int ExitCode(JsonNode envelope)
    {
        var ok = envelope["ok"]?.GetValue<bool>();
        return ok == true ? ExitCodes.Success : ok == false ? ExitCodes.Error : ExitCodes.ParseError;
    }

    public static JsonObject Ok(string command, JsonNode? data)
    {
        var payload = new JsonObject();
        payload["version"] = Version;
        payload["ok"] = true;
        payload["command"] = command;

        if (data is not null)
        {
            payload["data"] = data;
        }

        return payload;
    }

    public static JsonObject Err(string command, AdapterError error)
    {
        var payload = new JsonObject();
        payload["version"] = Version;
        payload["ok"] = false;
        payload["command"] = command;
        payload["error"] = ErrorPayload(error);
        return payload;
    }

    public static JsonObject ErrorPayload(AdapterError error)
    {
        var payload = new JsonObject();
        payload["code"] = error.Code.AsString();
        payload["message"] = error.Message;

        if (error.Suggestion is not null)
        {
            payload["suggestion"] = error.Suggestion;
        }

        var recovery = error.Recovery;
        if (recovery is not null)
        {
            payload["recovery"] = recovery.ToNode();
        }

        if (error.PlatformDetail is not null)
        {
            payload["platform_detail"] = error.PlatformDetail;
        }

        if (error.Details is not null)
        {
            payload["details"] = error.Details;
        }

        payload["disposition"] = Disposition(error.Disposition);
        return payload;
    }

    public static JsonObject Disposition(DeliverySemantics disposition)
    {
        var payload = new JsonObject();
        payload["delivery"] = DeliveryNames.AsString(disposition.Delivery);
        payload["retry"] = RetryNames.AsString(disposition.Retry);
        return payload;
    }
}

public static class ExitCodes
{
    public const int Success = 0;

    public const int Error = 1;

    public const int ParseError = 2;
}

public static class DeliveryNames
{
    public static string AsString(Delivery delivery) => delivery switch
    {
        Delivery.Unknown => "unknown",
        Delivery.NotDelivered => "not_delivered",
        Delivery.DeliveryUncertain => "delivery_uncertain",
        Delivery.DeliveredUnverified => "delivered_unverified",
        Delivery.DeliveredVerified => "delivered_verified",
        _ => "unknown",
    };
}

public static class RetryNames
{
    public static string AsString(Retry retry) => retry switch
    {
        Retry.Unknown => "unknown",
        Retry.Safe => "safe",
        Retry.Unsafe => "unsafe",
        _ => "unknown",
    };
}
