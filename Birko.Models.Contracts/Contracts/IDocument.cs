using System;
using System.Collections.Generic;

namespace Birko.Models.Contracts
{
    /// <summary>
    /// Document header (invoice, receipt, transfer, etc.).
    /// </summary>
    public interface IDocument<TLine> where TLine : IDocumentLine
    {
        string DocumentNumber { get; set; }
        string Status { get; set; }
        ICollection<TLine> Lines { get; set; }
    }

    /// <summary>
    /// Single line within a document.
    /// </summary>
    public interface IDocumentLine
    {
        decimal Quantity { get; set; }
        decimal? UnitPrice { get; set; }
    }
}
