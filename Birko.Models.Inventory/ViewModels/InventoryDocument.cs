using System;
using System.ComponentModel;

namespace Birko.Models.Inventory.ViewModels
{
    public class InventoryDocument : Data.ViewModels.LogViewModel
    {
        public const string DocumentNumberProperty = "DocumentNumber";
        public const string StatusProperty = "Status";
        public const string InventoryDocumentObjectProperty = "InventoryDocument";

        public InventoryDocument()
        {
            PropertyChanged += InventoryDocument_PropertyChanged;
        }

        private string _documentNumber = null!;
        public string DocumentNumber { get => _documentNumber; set { if (_documentNumber != value) { _documentNumber = value; RaisePropertyChanged(DocumentNumberProperty); } } }

        private string _status = null!;
        public string Status { get => _status; set { if (_status != value) { _status = value; RaisePropertyChanged(StatusProperty); } } }

        public InventoryDocumentType DocumentType { get; set; }
        public Guid? CurrencyGuid { get; set; }
        public string CurrencySymbol { get; set; } = null!;
        public Guid TenantGuid { get; set; }

        private void InventoryDocument_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == DocumentNumberProperty || e.PropertyName == StatusProperty)
            {
                RaisePropertyChanged(InventoryDocumentObjectProperty);
            }
        }
    }
}
