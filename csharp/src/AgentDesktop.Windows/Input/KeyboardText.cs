using System.Text.Json.Nodes;
using AgentDesktop.Core.Response;
using AgentDesktop.Core.Wait;

namespace AgentDesktop.Windows.Input;

/// <summary>UTF-16 `type_text` chunking and Unicode `SendInput` synthesis. The chunk boundary
/// (at most 32 UTF-16 units, never splitting a surrogate) is ported from the macOS keyboard
/// layer verbatim: nothing about `SendInput` forces this size, but it bounds each chunk against
/// the deadline and gives per-chunk delivery accounting. Each chunk becomes one `SendInput`
/// batch of `KEYEVENTF_UNICODE` down/up pairs, one pair per code unit, so a surrogate pair is
/// two pairs. A CR LF pair is posted as its CR alone, because each Unicode CR or LF is a line
/// break on its own and posting both would type two. No arm of this module places the typed
/// text itself into an error: only chunk counts survive into `details`.</summary>
internal static class KeyboardText
{
    internal const int TextChunkUtf16 = 32;

    internal const int MaxTextUtf16 = 1_000_000;

    internal const long TextChunkBudgetMs = 400;

    internal static int PreflightText(string text, Deadline deadline)
    {
        var chunks = PlannedChunkCount(text);
        var required = TextChunkBudgetMs * chunks;
        var remaining = deadline.RemainingMs();
        if (remaining < required)
        {
            throw AdapterError.Timeout("Text cannot be delivered safely within the remaining deadline")
                .WithDetails(new JsonObject
                {
                    ["delivered_chunks"] = 0,
                    ["total_chunks"] = chunks,
                    ["required_ms"] = required,
                    ["remaining_ms"] = remaining,
                })
                .WithDisposition(DeliverySemantics.NotDelivered());
        }

        return chunks;
    }

    internal static void EnsureChunkBudget(Deadline deadline, int total, int deliveredChunks)
    {
        if (deadline.IsExpired())
        {
            throw Tree.WindowRoot.DeadlineExceeded(deadline)
                .WithDisposition(deliveredChunks == 0
                    ? DeliverySemantics.NotDelivered()
                    : DeliverySemantics.DeliveredUnverified())
                .WithDetails(new JsonObject
                {
                    ["delivered_chunks"] = deliveredChunks,
                    ["total_chunks"] = total,
                });
        }
    }

    internal static void SynthesizeText(string text, Deadline deadline, Func<Deadline, bool> verifyTarget)
    {
        var chunkCount = PreflightText(text, deadline);
        var chunks = TextChunks(text);
        var total = chunks.Count;
        for (var delivered = 0; delivered < total; delivered++)
        {
            EnsureChunkBudget(deadline, total, delivered);
            if (!verifyTarget(deadline))
            {
                throw new AdapterError(ErrorCode.ActionFailed, "Target element lost focus before physical input delivery")
                    .WithDisposition(delivered == 0
                        ? DeliverySemantics.NotDelivered()
                        : DeliverySemantics.DeliveredUnverified())
                    .WithDetails(new JsonObject
                    {
                        ["delivered_chunks"] = delivered,
                        ["total_chunks"] = total,
                    });
            }

            KeyboardSend.PostKeyboardInputs(ChunkEvents(chunks[delivered]));
        }
    }

    internal static List<KeyboardInputEvent> ChunkEvents(IReadOnlyList<char> chunk)
    {
        var events = new List<KeyboardInputEvent>(chunk.Count * 2);
        foreach (var unit in chunk)
        {
            events.Add(new KeyboardInputEvent(0, unit, Uia.KeyboardIds.KeyEventUnicode));
            events.Add(new KeyboardInputEvent(0, unit, Uia.KeyboardIds.KeyEventUnicode | Uia.KeyboardIds.KeyEventKeyUp));
        }

        return events;
    }

    internal static List<List<char>> TextChunks(string text)
    {
        var chunks = new List<List<char>>();
        var current = new List<char>();
        foreach (var character in TypedChars(text))
        {
            var encoded = new List<char>(character.ToString().ToCharArray());
            if (current.Count > 0 && current.Count + encoded.Count > TextChunkUtf16)
            {
                chunks.Add(current);
                current = new List<char>();
            }

            current.AddRange(encoded);
        }

        if (current.Count > 0)
        {
            chunks.Add(current);
        }

        return chunks;
    }

    internal static int PlannedChunkCount(string text)
    {
        long units = 0;
        var chunks = 0;
        var current = 0;
        foreach (var character in TypedChars(text))
        {
            units += 1;
            if (units > MaxTextUtf16)
            {
                throw TextTooLarge();
            }

            if (current > 0 && current + 1 > TextChunkUtf16)
            {
                chunks += 1;
                current = 0;
            }

            current += 1;
        }

        return chunks + (current > 0 ? 1 : 0);
    }

    /// <summary>The characters posted for `text`: every character except the LF of a CR LF pair.</summary>
    internal static IEnumerable<char> TypedChars(string text)
    {
        char? previous = null;
        foreach (var character in text)
        {
            var skip = character == '\n' && previous == '\r';
            previous = character;
            if (!skip)
            {
                yield return character;
            }
        }
    }

    private static AdapterError TextTooLarge() =>
        AdapterError.InvalidArgs("Text input must not exceed 1000000 UTF-16 code units");
}
