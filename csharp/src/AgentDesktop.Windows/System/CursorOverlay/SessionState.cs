using AgentDesktop.Core.Response;

namespace AgentDesktop.Windows.System.CursorOverlay;

/// <summary>
/// Whether the session this renderer serves is still live.
/// A session can end without a disable ever reaching the renderer (a crashed agent, a session gc, an operator who
/// simply stops). The child has no console, no taskbar entry and no Alt-Tab presence, so an abandoned overlay would be
/// a topmost animated window with nothing in the product able to remove it. It therefore ends itself.
/// It does not use core's manifest reader: that reader routes every non-NotFound error and every parse failure into the
/// same absent result a deleted session produces, so polling it would tear a live overlay down on a transient file
/// error. Fault and absence are separated here, and only absence counts.
/// Two consecutive readings are required before teardown, so a single unreadable tick (the manifest is rewritten
/// mid-session by set_cursor_overlay and by ending a session) never ends a healthy overlay.
/// </summary>
internal static class SessionState
{
    /// <summary>
    /// What one look at the manifest established.
    /// Live: the session is live and still wants an overlay.
    /// Finished: the manifest is gone, its end is recorded, or its overlay is switched off. Any of the three means this
    /// renderer should stop.
    /// Unknown: the manifest could not be read or could not be parsed. Not a fact about the session, so it is never a
    /// reason to tear down.
    /// </summary>
    internal enum SessionReading
    {
        Live,
        Finished,
        Unknown,
    }

    /// <summary>
    /// The manifest's text, or the error that stopped it being read. A null record stands for "nothing was read".
    /// </summary>
    internal sealed record ManifestRead(string? Body, global::System.Exception? Error);

    internal const long ManifestReadLimit = 64 * 1024;

    internal static SessionReading Classify(ManifestRead? raw)
    {
        if (raw is null)
        {
            return SessionReading.Finished;
        }

        if (raw.Error is global::System.IO.FileNotFoundException or global::System.IO.DirectoryNotFoundException)
        {
            return SessionReading.Finished;
        }

        if (raw.Error is not null || raw.Body is null)
        {
            return SessionReading.Unknown;
        }

        return ClassifyBody(raw.Body);
    }

    /// <summary>
    /// A disabled overlay is the config default, and the manifest does not serialize defaults, so switching the overlay
    /// off removes the cursor_overlay key rather than setting it to false. Reading an absent key as "still enabled" is
    /// what made that whole teardown condition unable to fire.
    /// </summary>
    internal static SessionReading ClassifyBody(string body)
    {
        global::System.Text.Json.JsonDocument document;
        try
        {
            document = global::System.Text.Json.JsonDocument.Parse(body);
        }
        catch (global::System.Text.Json.JsonException)
        {
            return SessionReading.Unknown;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind == global::System.Text.Json.JsonValueKind.Object
                && root.TryGetProperty("ended_at", out var endedAt)
                && endedAt.ValueKind != global::System.Text.Json.JsonValueKind.Null)
            {
                return SessionReading.Finished;
            }

            if (root.ValueKind != global::System.Text.Json.JsonValueKind.Object
                || !root.TryGetProperty("cursor_overlay", out var overlay))
            {
                return SessionReading.Finished;
            }

            if (overlay.ValueKind == global::System.Text.Json.JsonValueKind.Object
                && overlay.TryGetProperty("enabled", out var enabled))
            {
                if (enabled.ValueKind == global::System.Text.Json.JsonValueKind.True)
                {
                    return SessionReading.Live;
                }

                if (enabled.ValueKind == global::System.Text.Json.JsonValueKind.False)
                {
                    return SessionReading.Finished;
                }
            }

            return SessionReading.Unknown;
        }
    }

    /// <summary>
    /// Tracks consecutive readings so one unreadable tick cannot end a live overlay.
    /// Unknown resets the count rather than passing over it. Without the reset the rule was "two finished readings,
    /// ever", so a finished reading, an unreadable tick and a second finished reading tore the overlay down, and an
    /// unreadable tick is exactly what a manifest being rewritten mid-session produces.
    /// </summary>
    internal sealed class EndWatch
    {
        private byte consecutiveFinished;

        /// <summary>
        /// True once the session has read finished twice running.
        /// </summary>
        internal bool Observe(SessionReading reading)
        {
            switch (reading)
            {
                case SessionReading.Finished:
                    if (consecutiveFinished < byte.MaxValue)
                    {
                        consecutiveFinished++;
                    }

                    return consecutiveFinished >= 2;
                case SessionReading.Live:
                case SessionReading.Unknown:
                default:
                    consecutiveFinished = 0;
                    return false;
            }
        }
    }

    /// <summary>
    /// Reads the session manifest for <paramref name="sessionId"/> through the hardened private-file read.
    /// The path must come from core so it cannot drift from where the manifest lives, and a path that cannot be
    /// resolved is reported as an error rather than as a missing manifest: a state root that is momentarily unreadable
    /// is not a session that ended. A hardened read that fails classifies as Unknown, so a misconfigured state root
    /// never self-reclaims and keeps drawing; that is the safe direction, ended only by cursor-overlay disable or by
    /// killing the renderer.
    /// </summary>
    internal static ManifestRead? ReadManifest(string sessionId)
    {
        throw AdapterError.NotSupported("cursor-overlay");
    }

    /// <summary>
    /// Decodes manifest bytes as strict UTF-8. Invalid UTF-8 is reported as an error, which classifies as Unknown.
    /// </summary>
    internal static ManifestRead DecodeManifest(byte[] bytes)
    {
        try
        {
            var text = new global::System.Text.UTF8Encoding(false, true).GetString(bytes);
            return new ManifestRead(text, null);
        }
        catch (global::System.Text.DecoderFallbackException error)
        {
            return new ManifestRead(null, new global::System.IO.InvalidDataException(error.Message, error));
        }
    }
}
