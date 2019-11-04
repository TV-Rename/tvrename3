using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace TVRename.View.Supporting
{
    /// <summary>
    /// Interaction logic for Filters.xaml
    /// </summary>
    public partial class Filters : Window
    {
        public Filters()
        {
            InitializeComponent();
        }

        private void bnReset_Click(object sender, RoutedEventArgs e)
        {
            tbShowName.Text = "";
            cmbShowStatus.SelectedItem = "";
            cmbNetwork.SelectedItem = "";
            cmbRating.SelectedItem = "";
            //clbGenre.ClearSelected();

            //for (int i = 0; i < clbGenre.Items.Count; i++)
            //{
                //clbGenre.SetItemChecked(i, false);
            //}

            chkHideIgnoredSeasons.IsChecked = false;
        }

        private void btnOk_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = true;
            this.Close();
        }

        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }
    }
}
