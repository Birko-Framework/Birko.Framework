using System;
using System.ComponentModel;

namespace Birko.Models.Inventory.ViewModels
{
    public class StorageLocation : Data.ViewModels.LogViewModel
    {
        public const string TitleProperty = "Title";
        public const string SortOrderProperty = "SortOrder";
        public const string StorageLocationObjectProperty = "StorageLocation";

        public StorageLocation()
        {
            PropertyChanged += StorageLocation_PropertyChanged;
        }

        private string _title = null!;
        public string Title { get => _title; set { if (_title != value) { _title = value; RaisePropertyChanged(TitleProperty); } } }

        private int _sortOrder;
        public int SortOrder { get => _sortOrder; set { if (_sortOrder != value) { _sortOrder = value; RaisePropertyChanged(SortOrderProperty); } } }

        public Guid? ParentGuid { get; set; }
        public string Path { get; set; } = null!;
        public int Depth { get; set; }
        public Guid TenantGuid { get; set; }

        private void StorageLocation_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == TitleProperty || e.PropertyName == SortOrderProperty)
            {
                RaisePropertyChanged(StorageLocationObjectProperty);
            }
        }
    }
}
