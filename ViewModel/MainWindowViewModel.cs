using System.ComponentModel;
using System.Runtime.CompilerServices;
using JetBrains.Annotations;
using TVRename.ViewModel;

namespace TVRename
{
    public class MainWindowViewModel : ViewModelBase
    {
        public string Airing
        {
            get => airing;
            set
            {
                airing = "Next Airing: "+value;
                NotifyPropertyChanged();
            }
        }

        public string Downloading
        {
            get => downloading;
            set => SetProperty(ref downloading, "Background Download: " + value);
        }

        public int Status
        {
            get => status;
            set
            {
                status = value;
                NotifyPropertyChanged();
            }
        }
        private string downloading;
        private int status;
        private string airing;



    }
}