namespace Birko.Data.SQL.Extensions
{
    /// <summary>
    /// SH-H036 — this class previously carried a <c>ReadOne(filter, orderByExpr)</c> extension that read
    /// through <c>repository.Connector</c>. It has been REMOVED, not fixed, and it must not come back.
    ///
    /// <para><b>Why it was a defect.</b> <c>Connector</c> resolves as
    /// <c>Store?.GetUnwrappedStore&lt;…&gt;()?.Connector</c>, and <c>GetUnwrappedStore</c> walks
    /// <c>IStoreWrapper.GetInnerStore()</c> down to the innermost store. Selecting through it therefore
    /// applied no decorator at all: <c>TenantStoreWrapper.Read</c> is what injects the
    /// <c>ModelByTenant</c> predicate, so the read returned the first matching row from <b>any</b> tenant,
    /// and the soft-delete, localization and audit wrappers were skipped by the same call.</para>
    ///
    /// <para><b>Why removed rather than repaired.</b> An extension in this assembly cannot reach the
    /// decorated chain — <c>AbstractViewModelRepository.Store</c> is <c>protected</c>, which is exactly
    /// why the original reached for <c>Connector</c>. The capability is only implementable safely as an
    /// instance method, so it now lives on the repository itself as
    /// <c>AbstractViewModelRepository.ReadOne(IFilter&lt;TModel&gt;?, OrderBy&lt;TModel&gt;?)</c>, which
    /// reads through <c>Store</c>. Nothing in the framework, the test tree or any consumer called the
    /// extension, so no call site had to change.</para>
    ///
    /// <para><b>The trap worth remembering.</b> The safe
    /// <c>AbstractViewModelRepository.ReadOne(IFilter&lt;TModel&gt;?)</c> and this unsafe one differed
    /// only in arity, and C# prefers an applicable instance method over an extension — so
    /// <c>ReadOne(filter)</c> was tenant-scoped while <c>ReadOne(filter, orderBy)</c> was not. Adding an
    /// ordering to a working call silently changed its isolation, with no error and no diff at the call
    /// site beyond one argument. Reaching for <c>Connector</c> / the <c>XStore</c> properties is the
    /// deliberate escape hatch to backend-native features; it is never the way to serve a portable read.
    /// </para>
    /// </summary>
    /// <remarks>
    /// Kept as an empty, documented class rather than deleting the file so the reasoning survives where
    /// the defect was, and so a future reader adding a repository extension here meets the warning first.
    /// </remarks>
    public static class IDataBaseRepositoryExtensions
    {
    }
}
