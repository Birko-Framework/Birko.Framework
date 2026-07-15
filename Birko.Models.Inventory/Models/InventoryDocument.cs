using System;
using System.Collections.Generic;
using System.Linq;
using Birko.Data.Models;
using Birko.Models.Contracts;

namespace Birko.Models.Inventory
{
    public enum InventoryDocumentType
    {
        Receipt = 0,
        Issue = 1,
        Transfer = 2
    }

    /// <summary>
    /// Inventory document (receipt, issue, or transfer).
    /// Clean replacement for Warehouse.WareHouseDocument — pricing extracted.
    /// </summary>
    public class InventoryDocument
        : AbstractLogModel
        , IDocument<InventoryDocumentLine>
        , ILoadable<ViewModels.InventoryDocument>
        , ICopyable<InventoryDocument>
    {
        public string DocumentNumber { get; set; } = null!;
        public string Status { get; set; } = null!;
        public InventoryDocumentType DocumentType { get; set; }
        public Guid? CurrencyGuid { get; set; }
        public string CurrencySymbol { get; set; } = null!;
        public Guid TenantGuid { get; set; }
        public ICollection<InventoryDocumentLine> Lines { get; set; } = new List<InventoryDocumentLine>();

        // CR-L309: typed CopyTo so cloning copies this document's own fields (base handles only
        // AbstractLogModel fields), consistent with StockItem/StockItemVariant. Lines are deep-copied
        // (each via InventoryDocumentLine.CopyTo) so the clone doesn't alias the original's line objects.
        public virtual InventoryDocument CopyTo(InventoryDocument clone)
        {
            if (clone == null)
            {
                clone = new InventoryDocument();
            }
            base.CopyTo(clone);
            clone.DocumentNumber = DocumentNumber;
            clone.Status = Status;
            clone.DocumentType = DocumentType;
            clone.CurrencyGuid = CurrencyGuid;
            clone.CurrencySymbol = CurrencySymbol;
            clone.TenantGuid = TenantGuid;
            clone.Lines = Lines?.Select(l => l.CopyTo(new InventoryDocumentLine())).ToList<InventoryDocumentLine>()
                ?? new List<InventoryDocumentLine>();
            return clone;
        }

        /// <summary>
        /// Hydrates the document's scalar fields from the view model. CR-M221: <see cref="Lines"/> is
        /// <b>not</b> populated here — the view model carries no line collection by design; lines are
        /// loaded and persisted separately (via the InventoryDocumentLine store), so a VM→document load
        /// intentionally yields an empty Lines collection rather than round-tripping children.
        /// </summary>
        public virtual void LoadFrom(ViewModels.InventoryDocument data)
        {
            base.LoadFrom(data);
            if (data == null) return;

            DocumentNumber = data.DocumentNumber;
            Status = data.Status;
            DocumentType = data.DocumentType;
            CurrencyGuid = data.CurrencyGuid;
            CurrencySymbol = data.CurrencySymbol;
            TenantGuid = data.TenantGuid;
        }
    }
}
