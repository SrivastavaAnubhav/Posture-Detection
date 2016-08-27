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

using System.IO;


namespace PosturaCSharp
{
    /// <summary>
    /// Interaction logic for SettingsForm.xaml
    /// </summary>
    public partial class SettingsForm : Window
    {
        public bool wantFlip { get; private set; }
		public bool useFaceAPI { get; private set; }
		public double hMult { get; private set; }
		public double wMult { get; private set; }
		public double roll { get; private set; }
		public double yaw { get; private set; }
		public int cwLim { get; private set; }

	public SettingsForm(bool flip, bool useFaceAPI, string azureSubKey, double topMult, double leftMult, double heightMult, double rollLimit, double yawLimit, int consecutiveWrongLimit)
        {
            InitializeComponent();

			cbFlip.IsChecked = flip;
			cbFaceAPI.IsChecked = useFaceAPI;
			tbAzureKey.Text = azureSubKey;
			slTop.Value = topMult;
			slLeft.Value = leftMult;
			slHeight.Value = heightMult;
			slRoll.Value = rollLimit;
			slYaw.Value = yawLimit;
			slCWLimit.Value = consecutiveWrongLimit;
        }

		private void btnSave_Click(object sender, RoutedEventArgs e)
		{
			using (StreamWriter sw = new StreamWriter("FaceSettings.txt"))
			{
				sw.WriteLine(cbFlip.IsChecked);
				sw.WriteLine(cbFaceAPI.IsChecked);
				sw.WriteLine(tbAzureKey.Text);
				sw.WriteLine(slTop.Value);
				sw.WriteLine(slLeft.Value);
				sw.WriteLine(slHeight.Value);
				sw.WriteLine(slRoll.Value);
				sw.WriteLine(slYaw.Value);
				sw.WriteLine(slCWLimit.Value);
			}

			this.Close();
		}

		private void btnReset_Click(object sender, RoutedEventArgs e)
		{
			cbFlip.IsChecked = true;
			cbFaceAPI.IsChecked = true;
			tbAzureKey.Text = string.Empty;
			slTop.Value = 1;
			slLeft.Value = 1;
			slRoll.Value = 50;
			slYaw.Value = 50;
			slCWLimit.Value = 1;
		}
	}
}
