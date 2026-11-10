using AgentDesktop.Core.Response;
using AgentDesktop.Core.Wait;

namespace AgentDesktop.Windows.Tree.Properties;

/// <summary>
/// Reads the walk's property set from one element, live, bounded, or from its cache. A single
/// failed property makes that slot <c>Unknown</c> and collects its error; it never abandons the
/// element, because a partial read set is what the walk reports as incomplete and a lost element
/// is what the walk reports as a failure.
///
/// The secure-field gate is decided once for the batch by
/// <see cref="ElementProperties.FromReads"/>, which is where the <c>IsPassword</c> read arrives in
/// the same set as the properties it gates.
/// </summary>
internal static class PropertyReader
{
    /// <summary>
    /// Reads one property live, for action preflight, post-action verification, and the provider
    /// class probe. A property the mapping refuses faults before the element is reached.
    /// </summary>
    internal static PropertyOutcome ReadOne(Element element, TreeProperty property) =>
        PropertyUia.ReadOne(element, property);

    /// <summary>
    /// Reads the walk set live. The budget is not consulted between properties: the caller owns it
    /// elsewhere, as the walk does.
    /// </summary>
    internal static (ElementProperties Properties, List<AdapterError> Errors) ReadLive(Element element) =>
        ReadWith(element, PropertyUia.ReadOne, null);

    /// <summary>
    /// The same live reads, bounded by an operation deadline consulted before each property's own
    /// cross-process call. Once the budget is spent the set is truncated: the remaining properties
    /// stay unread and classify <c>Unknown</c>, rather than starting a call that would outlast the
    /// deadline anyway.
    /// </summary>
    internal static (ElementProperties Properties, List<AdapterError> Errors) ReadLiveBounded(
        Element element,
        Deadline deadline) =>
        ReadWith(element, PropertyUia.ReadOne, deadline);

    /// <summary>
    /// Reads the walk set from the element's cache, which the walk's cache request filled when the
    /// element was retrieved. A property the request did not name is a programming error, not a
    /// live fetch, so it classifies <c>Unknown</c> with a structured error and never <c>Absent</c>:
    /// a missing request entry cannot masquerade as a provider that does not implement the property.
    /// </summary>
    internal static (ElementProperties Properties, List<AdapterError> Errors) ReadCached(Element element) =>
        ReadWith(element, PropertyUia.ReadCachedOne, null);

    private static (ElementProperties, List<AdapterError>) ReadWith(
        Element element,
        Func<Element, TreeProperty, PropertyOutcome> read,
        Deadline? deadline)
    {
        List<(TreeProperty, PropertyOutcome)> reads = [];
        List<AdapterError> errors = [];
        var truncated = false;
        foreach (var property in PropertyIds.WalkSet)
        {
            if (property.IsElementValued())
            {
                continue;
            }

            if (deadline is { } limit && limit.IsExpired())
            {
                if (!truncated)
                {
                    errors.Add(limit.TimeoutError());
                    truncated = true;
                }

                reads.Add((property, PropertyOutcome.Unknown));
                continue;
            }

            try
            {
                reads.Add((property, read(element, property)));
            }
            catch (AdapterError error)
            {
                errors.Add(error);
                reads.Add((property, PropertyOutcome.Unknown));
            }
        }

        return (ElementProperties.FromReads(reads), errors);
    }
}
