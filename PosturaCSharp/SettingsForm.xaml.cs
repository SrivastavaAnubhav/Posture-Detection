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

namespace PosturaCSharp
{
    /// <summary>
    /// Interaction logic for SettingsForm.xaml
    /// </summary>
    public partial class SettingsForm : Window
    {
        // TODO: Save settings
        public bool wantFlip { get; private set; }

        public SettingsForm(bool flip)
        {
            InitializeComponent();

            checkBox.IsChecked = flip;
        }


        private void checkBox_Checked(object sender, RoutedEventArgs e)
        {
            wantFlip = true;
        }

        private void checkBox_Unchecked(object sender, RoutedEventArgs e)
        {
            wantFlip = false;
        }
    }
}
