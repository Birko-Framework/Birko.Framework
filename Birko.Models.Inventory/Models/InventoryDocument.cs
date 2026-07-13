using System;
using System.Collections.Generic;
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
    {
        public string DocumentNumber { get; set; } = null!;
        public string Status { get; set; } = null!;
        public InventoryDocumentType DocumentType { get; set; }
        public Guid? CurrencyGuid { get; set; }
        public string CurrencySymbol { get; set; } = null!;
        public Guid TenantGuid { get; set; }
        public ICollection<InventoryDocumentLine> Lines { get; set; } = new List<InventoryDocumentLine>();

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
