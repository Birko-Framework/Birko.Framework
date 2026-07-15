using System;
using System.Linq;

namespace Birko.Models.Category.ViewModels
{
    public class Category : Data.ViewModels.LogViewModel, Data.Models.ILoadable<Birko.Models.Category.Category>, Data.Models.ILoadable<Category>
    {
        public const string TitleProperty = "Title";
        public const string SlugProperty = "Slug";
        public const string PathProperty = "Path";
        public const string DescriptionProperty = "Description";
        public const string CategoryObjectProperty = "Category";

        public Category()
        {
            PropertyChanged += Category_PropertyChanged;
        }

        private string _title = null!;
        public string Title
        {
            get { return _title; }
            set
            {
                if (_title != value)
                {
                    _title = value;
                    RaisePropertyChanged(TitleProperty);
                }
            }
        }

        private string? _slug;
        public string? Slug
        {
            get { return _slug; }
            set
            {
                if (_slug != value)
                {
                    _slug = value;
                    RaisePropertyChanged(SlugProperty);
                }
            }
        }

        private string _path = null!;
        public string Path
        {
            get { return _path; }
            set
            {
                // CR-L304: guard like the other setters — setting Path to its current value must not fire a
                // spurious PropertyChanged (which cascades to the "Category" object notification).
                if (_path != value)
                {
                    _path = value;
                    RaisePropertyChanged(PathProperty);
                }
            }
        }

        private string _description = null!;
        public string Description
        {
            get { return _description; }
            set
            {
                if (_description != value)
                {
                    _description = value;
                    RaisePropertyChanged(DescriptionProperty);
                }
            }
        }

        private void Category_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (new[] {
                    TitleProperty,
                    SlugProperty,
                    PathProperty,
                    DescriptionProperty,
                }.Contains(e.PropertyName)
            )
            {
                RaisePropertyChanged(CategoryObjectProperty);
            }
        }

        public void LoadFrom(Birko.Models.Category.Category data)
        {
            // CR-L303: guard first (guard-clause convention) — base is null-safe, so this is an ordering fix.
            if (data == null) return;
            base.LoadFrom(data);

            Title = data.Title;
            Slug = data.Slug;
            Path = data.Path;
            Description = data.Description;
        }

        public virtual void LoadFrom(Category data)
        {
            // CR-L303: guard first (guard-clause convention) — base is null-safe, so this is an ordering fix.
            if (data == null) return;
            base.LoadFrom(data);

            Title = data.Title;
            Slug = data.Slug;
            Path = data.Path?.Trim() ?? string.Empty;
            Description = data.Description;
        }
    }
}
