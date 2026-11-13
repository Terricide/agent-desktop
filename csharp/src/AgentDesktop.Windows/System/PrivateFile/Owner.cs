using AgentDesktop.Core.Response;
using AgentDesktop.Windows.System.Runtime;
using Microsoft.Win32.SafeHandles;

namespace AgentDesktop.Windows.System.PrivateFile;

/// <summary>
/// Ownership validation against the set of SIDs Windows itself would permit this process's token to hold as a
/// file owner. New filesystem objects land owned by <c>TokenOwner</c>, not <c>TokenUser</c>: measured at both
/// High and Medium integrity, a file created by an admin-group account is owned by the Administrators group
/// while the owner does not match <c>TokenUser</c> and does match <c>TokenOwner</c>. Group membership is the
/// variable, never integrity.
/// <para>
/// <c>TokenOwner</c> is not itself stable for one human across elevation: a non-elevated process's
/// <c>TokenOwner</c> reads as the user SID, while an elevated process's reads as <c>BUILTIN\Administrators</c>,
/// because UAC elevation swaps the token's default owner to whichever group carries the <c>SE_GROUP_OWNER</c>
/// attribute. Validation therefore accepts the owner-eligible set: <c>TokenUser</c>, <c>TokenOwner</c>, and every
/// <c>TokenGroups</c> entry whose attributes carry <c>SE_GROUP_OWNER</c>. That is exactly the set of SIDs
/// <c>SetSecurityInfo</c> would let this token assign as an owner, no wider and no narrower.
/// </para>
/// <para>
/// The purpose is narrow: detecting a path pre-created by a foreign principal, the Windows analogue of the unix
/// uid post-condition in the core private-file module. It is not an isolation boundary between administrator
/// processes, because an administrator holds <c>SeTakeOwnershipPrivilege</c>. The unix <c>nlink</c>
/// post-condition has no measured Windows analogue and is not carried here.
/// </para>
/// <para>
/// Only the owner is read, through <c>GetSecurityInfo</c> with <c>OWNER_SECURITY_INFORMATION</c> and no DACL, so
/// the read consumes a fixed-layout SID and never touches an ACE.
/// </para>
/// </summary>
internal static class Owner
{
    private const int WinLocalSystemSid = 22;

    private static readonly Lazy<IReadOnlyList<SidBuffer>> OwnerEligibleSidsCache =
        new(ReadOwnerEligibleSidsWrapped);

    private static readonly Lazy<SidBuffer> ProcessTokenOwnerCache =
        new(ReadProcessTokenOwnerWrapped);

    [ThreadStatic]
    private static bool forceForeignOwner;

    /// <summary>
    /// Refuses a file whose owner is not in the owner-eligible set of this process's token. The owner is read
    /// through the forced-foreign-owner seam when a test has activated it.
    /// </summary>
    internal static void RequireOwnedByEligiblePrincipal(SafeFileHandle file, string what)
    {
        var eligible = ProcessOwnerEligibleSids();
        var actual = ActualOwnerForComparison(file);
        RequireOwnerInSet(actual, eligible, what);
    }

    /// <summary>Refuses a file whose real owner, read without the test seam, is not owner-eligible.</summary>
    internal static void RequireActualOwnerByEligiblePrincipal(SafeFileHandle file, string what)
    {
        var eligible = ProcessOwnerEligibleSids();
        var actual = FileOwnerSid(file);
        RequireOwnerInSet(actual, eligible, what);
    }

    /// <summary>
    /// The owner-eligible set of this process, read once and cached. Any failure while reading the token is
    /// reported as <c>PERM_DENIED</c>, and the failure is cached so every later caller sees the same answer.
    /// </summary>
    internal static IReadOnlyList<SidBuffer> ProcessOwnerEligibleSids() => OwnerEligibleSidsCache.Value;

    /// <summary>
    /// The owner of this process's token, read once and cached. Test-only in the Rust source; kept internal here
    /// so the same assertion can run against the same value.
    /// </summary>
    internal static SidBuffer ProcessTokenOwnerSid() => ProcessTokenOwnerCache.Value;

    /// <summary>The user of this process's token, read without caching.</summary>
    internal static SidBuffer ProcessTokenUserSidForTests() => ReadProcessTokenUser();

    /// <summary>
    /// Runs <paramref name="run"/> with the owner comparison forced to observe a foreign principal, so the
    /// refusal branch can be exercised without a file pre-created by another account. The substituted owner is
    /// the <c>WinLocalSystemSid</c>, built programmatically so the seam stays portable and privilege-free. The
    /// previous flag value is restored when the run returns or throws.
    /// </summary>
    internal static T WithForcedForeignOwner<T>(Func<T> run)
    {
        var previous = forceForeignOwner;
        forceForeignOwner = true;
        try
        {
            return run();
        }
        finally
        {
            forceForeignOwner = previous;
        }
    }

    /// <summary>The owner <c>GetSecurityInfo</c> reports for an open file handle.</summary>
    internal static SidBuffer FileOwnerSid(SafeFileHandle file) => TokenSid.ReadFileOwnerSid(file);

    /// <summary>
    /// Whether <paramref name="candidate"/> names the same principal as one owner-eligible entry. Membership is
    /// tested with <c>EqualSid</c> semantics through <see cref="SidBuffer.Matches"/>.
    /// </summary>
    internal static bool IsEligible(IReadOnlyList<SidBuffer> eligible, SidBuffer candidate) =>
        eligible.Any(sid => sid.Matches(candidate));

    private static SidBuffer ActualOwnerForComparison(SafeFileHandle file)
    {
        if (forceForeignOwner)
        {
            return ForeignSid();
        }

        return FileOwnerSid(file);
    }

    private static SidBuffer ForeignSid() => SidBuffer.WellKnown(WinLocalSystemSid);

    private static void RequireOwnerInSet(SidBuffer actual, IReadOnlyList<SidBuffer> eligible, string what)
    {
        if (!IsEligible(eligible, actual))
        {
            throw new AdapterError(
                ErrorCode.PermDenied,
                $"{what} is owned by a foreign principal, not this process's token user, token owner, or an "
                + "owner-eligible token group; if a different principal owns this path intentionally, relocate the "
                + "state root with AGENT_DESKTOP_HOME instead of reusing it");
        }
    }

    private static IReadOnlyList<SidBuffer> ReadOwnerEligibleSidsWrapped()
    {
        try
        {
            return ReadOwnerEligibleSids();
        }
        catch (AdapterError error)
        {
            throw new AdapterError(ErrorCode.PermDenied, error.Message);
        }
    }

    private static SidBuffer ReadProcessTokenOwnerWrapped()
    {
        try
        {
            return TokenSid.ReadTokenOwnerSid(TokenSource.CurrentProcess);
        }
        catch (AdapterError error)
        {
            throw new AdapterError(ErrorCode.PermDenied, error.Message);
        }
    }

    private static IReadOnlyList<SidBuffer> ReadOwnerEligibleSids()
    {
        var entries = new List<SidBuffer>
        {
            ReadProcessTokenUser(),
            TokenSid.ReadTokenOwnerSid(TokenSource.CurrentProcess),
        };
        entries.AddRange(ReadOwnerFlaggedGroupSids());
        return entries;
    }

    /// <summary>
    /// Every <c>TokenGroups</c> SID whose attributes carry <c>SE_GROUP_OWNER</c> (<c>0x00000008</c>). Only the owner
    /// flag selects a group, so a group such as <c>Everyone</c> that every token holds stays out of the set.
    /// </summary>
    /// <remarks>
    /// SEAM: TokenSid has no TokenGroups read, so this read is not yet ported. Replaced with the NotSupported
    /// error. Consequence: every owner-eligibility check fails closed with PERM_DENIED until the seam is filled.
    /// </remarks>
    private static IReadOnlyList<SidBuffer> ReadOwnerFlaggedGroupSids() =>
        throw AdapterError.NotSupported("TokenSid.ReadOwnerFlaggedGroupSids");

    private static SidBuffer ReadProcessTokenUser() => TokenSid.ReadTokenUserSid(TokenSource.CurrentProcess);
}
