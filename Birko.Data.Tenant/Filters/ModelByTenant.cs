using Birko.Data.Expressions;
using Birko.Data.Filters;
using Birko.Data.Models;
using Birko.Data.Tenant.Models;
using System;
using System.Linq.Expressions;

namespace Birko.Data.Tenant.Filters
{
    public class ModelByTenant<TModel> : IFilter<TModel>
         where TModel : AbstractModel, ITenant
    {
        public Guid? TenantGuid { get; set; }
        public Expression<Func<TModel, bool>>? BaseFilter { get; set; }

        public ModelByTenant(Guid? tenantGuid, Expression<Func<TModel, bool>>? filter = null)
        {
            TenantGuid = tenantGuid;
            BaseFilter = filter;
        }

        /// <summary>
        /// Composes the caller's <see cref="BaseFilter"/> with the tenant predicate.
        /// </summary>
        /// <remarks>
        /// <para><b>Only <c>null</c> means "no tenant in scope"</b> and yields an unfiltered (cross-tenant)
        /// read — that is the non-tenant/admin mode, and <c>TenantStoreWrapper.EnsureTenantForStrict</c> is
        /// what refuses it under <see cref="Models.TenantIsolationMode.Strict"/>.</para>
        /// <para><see cref="Guid.Empty"/> is <b>a tenant value like any other</b> and IS filtered on. It used
        /// to short-circuit to <see cref="BaseFilter"/> alongside <c>null</c>, which made a
        /// <c>Guid.Empty</c> scope read <i>every</i> tenant's rows while <c>BelongsToCurrentTenant</c> still
        /// compared writes against <c>Guid.Empty</c> — reads failed open, writes failed closed. Measured in a
        /// Symbio consumer: a list read under an ambient <c>Guid.Empty</c> scope returned another tenant's
        /// rows, and the write that followed refused them as <c>Tenant.Mismatch</c> (Symbio TASK-295; the
        /// reachable path into that scope was Symbio TASK-290, where invited members were minted a
        /// <c>Guid.Empty</c> tenant claim on login). "No tenant" was already expressible as <c>null</c>, so
        /// the special case bought nothing and cost isolation.</para>
        /// <para>Deliberately does <b>not</b> throw on <c>Guid.Empty</c>: the caller-supplied value can come
        /// straight off an <c>X-Tenant-Id</c> header, so throwing would let any client turn a zero GUID into
        /// a 500. Filtering on it returns an empty result — fail-closed and unremarkable — while the wrapper
        /// keeps throwing for the genuinely-unset (<c>null</c>) case under <c>Strict</c>.</para>
        /// </remarks>
        public virtual Expression<Func<TModel, bool>>? Filter()
        {
            if (TenantGuid != null)
            {
                Expression<Func<TModel, bool>> tenantFilter = (x) => x.TenantGuid == TenantGuid;
                return ExpressionParameterReplacer.AndAlso(BaseFilter, tenantFilter);
            }
            return BaseFilter;
        }
    }
}
