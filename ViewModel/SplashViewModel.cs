using System.ComponentModel;
using System.Runtime.CompilerServices;
using JetBrains.Annotations;

namespace TVRename.ViewModel
{
    public class SplashViewModel : INotifyPropertyChanged
    {
        public string Version
        {
            get => version;
            set
            {
                version = value;
                NotifyPropertyChanged();
            }
        }

        public string Information
        {
            get => info;
            set
            {
                info = value;
                NotifyPropertyChanged();
            }
        }

        public string Status
        {
            get => status;
            set
            {
                status = value;
                NotifyPropertyChanged();
            }
        }

        public int Progress
        {
            get => progress;
            set
            {
                progress = value;
                NotifyPropertyChanged();
            }
        }

        private int progress;
        private string status;
        private string info;
        private string version;

        public event PropertyChangedEventHandler PropertyChanged;

        [NotifyPropertyChangedInvocator]
        protected virtual void NotifyPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
