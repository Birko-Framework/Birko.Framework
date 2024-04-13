using System;
using System.Linq;

namespace Birko.ViewModels
{
    public class Category : Data.ViewModels.LogViewModel, Data.Models.ILoadable<Models.Category>, Data.Models.ILoadable<Category>
    {
        public const string TitleProperty = "Title";
        public const string PathProperty = "Path";
        public const string DescriptionProperty = "Description";
        public const string CategoryObjectProperty = "Category";

        public Category()
        {
            PropertyChanged += Category_PropertyChanged;
        }

        private string _title;
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

        private string _path;
        public string Path
        {
            get { return _path; }
            set
            {
                _path = value;
                RaisePropertyChanged(PathProperty);
            }
        }

        private string _description;
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

        private void Category_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (new[] {
                    TitleProperty,
                    PathProperty,
                    DescriptionProperty,
                }.Contains(e.PropertyName)
            )
            {
                RaisePropertyChanged(CategoryObjectProperty);
            }
        }

        public void LoadFrom(Models.Category data)
        {
            base.LoadFrom(data);
            if (data != null)
            {
                Title = data.Title;
                Path = data.Path;
                Description = data.Description;
            }
        }

        public virtual void LoadFrom(Category data)
        {
            base.LoadFrom(data);
            if (data != null)
            {
                Title = data.Title;
                Path = data.Path?.Trim();
                Description = data.Description;
            }
        }
    }
}
