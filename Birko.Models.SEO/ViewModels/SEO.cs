using System;
using System.Collections.Generic;
using System.Linq;

namespace Birko.Models.SEO.ViewModels
{
    public class SEO : Birko.Data.ViewModels.LogViewModel, Birko.Data.Models.ILoadable<Birko.Models.SEO.SEO>, Birko.Data.Models.ILoadable<SEO>
    {
        public const string TitleProperty = "Title";
        public const string PathProperty = "Path";
        public const string DescriptionProperty = "Description";
        public const string SEOObjectProperty = "SEO";

        public SEO()
        {
            PropertyChanged += SEO_PropertyChanged;
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

        private string _path = null!;
        public string Path
        {
            get { return _path; }
            set
            {
                _path = value;
                RaisePropertyChanged(PathProperty);
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

        private void SEO_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (new[] {
                    TitleProperty,
                    PathProperty,
                    DescriptionProperty,
                }.Contains(e.PropertyName)
            )
            {
                RaisePropertyChanged(SEOObjectProperty);
            }
        }

        public void LoadFrom(Birko.Models.SEO.SEO data)
        {
            base.LoadFrom(data);
            if (data == null) return;

            Title = data.Title;
            Path = data.Path;
            Description = data.Description;
        }

        public virtual void LoadFrom(SEO data)
        {
            base.LoadFrom(data);
            if (data == null) return;

            Title = data.Title;
            Path = data.Path;
            Description = data.Description;
        }
    }
}
