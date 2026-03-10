using System;
using System.Collections.Generic;
using System.Linq;

namespace Birko.Models.ViewModels
{
    public class AbstractTree : Birko.Data.ViewModels.LogViewModel
    {
        public const string PathProperty = "Path";
        public const string AbstractTreeObjectProperty = "AbstractTree";

        public AbstractTree()
        {
            PropertyChanged += AbstractTree_PropertyChanged;
        }

        private IEnumerable<Guid> _path;
        public IEnumerable<Guid> Path
        {
            get { return _path; }
            set
            {
                _path = value;
                RaisePropertyChanged(PathProperty);
            }
        }

        private void AbstractTree_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (new[] { PathProperty }.Contains(e.PropertyName))
            {
                RaisePropertyChanged(AbstractTreeObjectProperty);
            }
        }
    }
}
