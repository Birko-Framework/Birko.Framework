namespace Birko.Data.Tagging;

/// <summary>
/// Thrown when a data-access hook returns a <see cref="Models.Tag"/> belonging to a tenant other than the
/// ambient one (SH-H019).
/// </summary>
/// <remarks>
/// <para><b>Why this exists as its own type.</b> <see cref="TagServiceBase"/>'s read/delete hooks take no
/// tenant parameter, so before this guard the base class depended entirely on every implementor
/// remembering to filter — a contract stated in a comment and enforced nowhere. A hook that skipped the
/// filter returned and *deleted* other tenants' data silently. The base now checks the loaded record, so a
/// missing filter surfaces as this exception instead of as a cross-tenant read or a cascade delete.</para>
/// <para>A distinct type rather than <see cref="System.InvalidOperationException"/> so a host can select
/// it — this is an isolation breach worth alerting on, not an ordinary bad-request. It stays inside
/// <c>Birko.Data.Tagging</c> because this project has no dependency on <c>Birko.Data.Tenant</c> and
/// acquiring one to share <c>TenantMismatchException</c> would be a larger change than the guard itself.
/// </para>
/// <para>It signals a <b>bug in the implementation</b>, not bad user input. Catching it to fall back to
/// "no result" would restore the silence this guard exists to remove.</para>
/// </remarks>
public class CrossTenantTagAccessException : InvalidOperationException
{
    public CrossTenantTagAccessException(Guid tagId, Guid recordTenant, Guid currentTenant)
        : base($"Tag {tagId} belongs to tenant {recordTenant} but the ambient tenant is {currentTenant}. "
             + "The data-access hook that returned it is not scoping its query to the current tenant — see "
             + "the TENANT-SCOPING CONTRACT on TagServiceBase. This is an implementation defect, not a "
             + "recoverable condition: do not catch it to return an empty result.")
    {
        TagId = tagId;
        RecordTenant = recordTenant;
        CurrentTenant = currentTenant;
    }

    public Guid TagId { get; }
    public Guid RecordTenant { get; }
    public Guid CurrentTenant { get; }
}
