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
        public bool wantFlip { get; private set; }
		public double hMult { get; private set; }
		public double wMult { get; private set; }
		public double roll { get; private set; }
		public double yaw { get; private set; }
		public int cwLim { get; private set; }

	public SettingsForm(bool flip, double heightMult, double widthMult, double rollLimit, double yawLimit, int consecutiveWrongLimit)
        {
            InitializeComponent();

            cbFlip.IsChecked = flip;
			slHeight.Value = heightMult;
			slWidth.Value = widthMult;
			slRoll.Value = rollLimit;
			slYaw.Value = yawLimit;
			slCWLimit.Value = consecutiveWrongLimit;
        }

		private void btnSave_Click(object sender, RoutedEventArgs e)
		{
			// TODO: Save settings
			this.Close();
		}

		private void btnReset_Click(object sender, RoutedEventArgs e)
		{
			cbFlip.IsChecked = true;
			slHeight.Value = 1;
			slWidth.Value = 1;
			slRoll.Value = 50;
			slYaw.Value = 50;
			slCWLimit.Value = 1;
		}
	}
}
