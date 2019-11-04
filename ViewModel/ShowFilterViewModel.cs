using System.Collections.Generic;
using System.ComponentModel;

namespace TVRename.ViewModel
{
    class ShowFilterViewModel :ViewModelBase
    {
        private readonly ShowLibrary library;

        public ShowFilterViewModel(ShowLibrary lib)
        {
            library = lib;
        }

        private bool hideIgnoredSeasons;
        private string selectedShowName;
        private string selectedShowStatus;
        private string selectedShowRating;
        private string selectedNetwork;
        private List<string> selectedGenres;

        public event PropertyChangedEventHandler PropertyChanged;

        public IEnumerable<string> AvailableGenres => library.GetGenres();
        public IEnumerable<string> AvailableNetworks => library.GetNetworks();
        public IEnumerable<string> AvailableShowStatii => library.GetStatuses();
        public IEnumerable<string> AvailableRatings => library.GetContentRatings();

        public bool HideIgnoredSeasons
        {
            get => hideIgnoredSeasons;
            set => SetProperty(ref hideIgnoredSeasons,  value);
        }

        public string SelectedShowName
        {
            get => selectedShowName;
            set => SetProperty(ref selectedShowName, value);
        }

        public string SelectedShowStatus
        {
            get => selectedShowStatus;
            set => SetProperty(ref selectedShowStatus, value);
        }

        public string SelectedShowRating
        {
            get => selectedShowRating;
            set => SetProperty(ref selectedShowRating, value);
        }
        public string SelectedNetwork
        {
            get => selectedNetwork;
            set => SetProperty(ref selectedNetwork, value);
        }

        public List<string> SelectedGenres
        {
            get => selectedGenres;
            set
            {
                selectedGenres = value;
                NotifyPropertyChanged();
            }
        }
    }
}
