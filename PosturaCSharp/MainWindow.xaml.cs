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
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

using System.Drawing;
using System.IO;
using System.Diagnostics;
using System.Media;

using System.Net.Http.Headers;
using System.Net.Http;
using System.Web;

using Microsoft.ProjectOxford.Face;
using Microsoft.ProjectOxford.Face.Contract;

using AForge.Video;
using AForge.Video.DirectShow;

namespace PosturaCSharp
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>

    public partial class MainWindow : Window
    {
        // TODO: Ask whether calibration picture is OK
        // TODO: Show differences
        // TODO: Preferences section: set tolerances, set camera
        // TODO: Make settings_gear.png relative

        private System.Windows.Shapes.Rectangle rct = new System.Windows.Shapes.Rectangle()
        {
            Fill = System.Windows.Media.Brushes.Transparent,
            Stroke = System.Windows.Media.Brushes.Red,
            StrokeThickness = 3,
            Visibility = Visibility.Hidden
        };

        private double imageHeight, imageWidth;
        private FilterInfoCollection videoDevicesList;
        private VideoCaptureDevice camera;
        private Stopwatch sw = new Stopwatch();
        private readonly IFaceServiceClient faceServiceClient = new FaceServiceClient("64204927d74943918f0af8c8151e48c7");
        private bool flip = true;
		private Face goodFace;

        public MainWindow()
        {
            InitializeComponent();
            rctHolder.Children.Add(rct);
        }

        private void btnSettings_Click(object sender, RoutedEventArgs e)
        {
            SettingsForm settingsForm = new SettingsForm(flip);
            settingsForm.Owner = this;
            settingsForm.ShowDialog();
            flip = settingsForm.wantFlip;
            videoBox.RenderTransform = new ScaleTransform(Convert.ToInt32(!flip)*2 - 1, 1);
        }

        private void videoBox_Loaded(object sender, RoutedEventArgs e)
        {
            videoDevicesList = new FilterInfoCollection(FilterCategory.VideoInputDevice);
            foreach (FilterInfo device in videoDevicesList)
            {
                cbDeviceList.Items.Add(device.Name);
            }

            camera = new VideoCaptureDevice();

            if (videoDevicesList.Count != 0)
            {
                cbDeviceList.SelectedIndex = 0;
            }
        }

        private void cbDeviceList_SelectionChanged(object sender, EventArgs e)
        {
            if (camera.IsRunning) camera.SignalToStop();
            camera = new VideoCaptureDevice(videoDevicesList[cbDeviceList.SelectedIndex].MonikerString);
            camera.NewFrame += new NewFrameEventHandler(camera_NewFrame);
            camera.Start();
        }

        private void camera_NewFrame(object sender, NewFrameEventArgs e)
        {
            // The reflection of the image is done using scale transform at the videoBox Image control
            // While this means reflection must be done twice (once here, once when checking faces), it saves
            // time, because reflection of the actual image is costly
            videoBox.Dispatcher.Invoke(delegate { videoBox.Source = BitmapToImageSource(e.Frame); });
        }

        private BitmapImage BitmapToImageSource(Bitmap bitmap)
        {
            using (MemoryStream memory = new MemoryStream())
            {
                // Creates a BitmapImage (which can be fed into videoBox) from a Bitmap
                bitmap.Save(memory, System.Drawing.Imaging.ImageFormat.Bmp);
                memory.Position = 0;
                BitmapImage bitmapimage = new BitmapImage();
                bitmapimage.BeginInit();
                bitmapimage.StreamSource = memory;
                bitmapimage.CacheOption = BitmapCacheOption.OnLoad;
                bitmapimage.EndInit();
                bitmapimage.Freeze();

                return bitmapimage;
            }
        }

        private void MainForm_FormClosing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (camera.IsRunning)
            {
                camera.SignalToStop();
            }
        }

        private async void btnCalibrate_Click(object sender, RoutedEventArgs e)
        {
            btnCalibrate.IsEnabled = false;
            btnContinue.IsEnabled = false;
            btnSettings.IsEnabled = false;
            rct.Visibility = Visibility.Hidden;

            if (camera.IsRunning == false)
            {
                camera.Start();
            }

            await Countdown();
            camera.SignalToStop();
            await VideoBoxFlash();

            sw.Start();

            Face[] faces = await GetJSON();

            sw.Stop();
            lblLag.Content = sw.ElapsedMilliseconds;
            sw.Reset();

            if (faces.Length > 0)
            {
				goodFace = faces[0];
                BoxFace(goodFace);
                Grid.SetColumnSpan(btnCalibrate, 1);
                btnContinue.IsEnabled = true;
                Grid.SetColumnSpan(btnContinue, 1);
            }
            else
            {
                tbCountdown.Text = "No faces found";
                Grid.SetColumnSpan(btnCalibrate, 2);
            }

            btnCalibrate.Content = "Recalibrate!";
            btnCalibrate.IsEnabled = true;
        }

        private void BoxFace(Face face)
        {
            rct.Height = videoBox.ActualHeight * face.FaceRectangle.Height / imageHeight;
            rct.Width = videoBox.ActualWidth * face.FaceRectangle.Width / imageWidth;

            double ratio = videoBox.Source.Width / videoBox.Source.Height;
            double leftPercent = face.FaceRectangle.Left / imageWidth;
            double topPercent = face.FaceRectangle.Top / imageHeight;

            double bigWidth = MainForm.ActualWidth;
            double bigHeight = 0;

            //for (int i = Grid.GetColumn(videoBox); i < Grid.GetColumn(videoBox) + Grid.GetColumnSpan(videoBox); i++)
            //{
            //    bigWidth += MainGrid.ColumnDefinitions[i].ActualWidth;
            //}

            for (int i = Grid.GetRow(videoBox); i < Grid.GetRow(videoBox) + Grid.GetRowSpan(videoBox); i++) {
                bigHeight += MainGrid.RowDefinitions[i].ActualHeight;
            }

            // Dividing by 2 because the space is on both sides
            double trimWidth = (bigWidth - videoBox.ActualWidth) / 2;
            double trimHeight = (bigHeight- videoBox.ActualHeight) / 2;

            rct.Visibility = Visibility.Visible;

            // Only one of these will actually be distinct
            // May decide to put an if statement to reflect this later
            Canvas.SetLeft(rct, videoBox.ActualWidth * leftPercent + trimWidth);
            Canvas.SetTop(rct, videoBox.ActualHeight * topPercent + trimHeight);
        }

        private async Task Countdown()
        {
            for (int i = 3; i > 0; i--)
            {
                tbCountdown.Dispatcher.Invoke(delegate { tbCountdown.Text = i.ToString(); });
                //SystemSounds.Beep.Play();
                await Task.Delay(1000);
            }
            tbCountdown.Dispatcher.Invoke(delegate { tbCountdown.Text = ""; });
        }

        private async Task VideoBoxFlash()
        {
            double op = 0.8;
            while (op > 0)
            {
                videoBox.Dispatcher.Invoke(delegate { videoBox.Opacity = op; });
                op -= 0.25;
                await Task.Delay(1);
            }

            op = 0;

            while (op < 1)
            {
                videoBox.Dispatcher.Invoke(delegate { videoBox.Opacity = op; });
                op += 0.02;
                await Task.Delay(1);
            }
        }

        private async Task<Face[]> GetJSON()
        {
            using (MemoryStream imageFileStream = new MemoryStream())
            {
                var encoder = new PngBitmapEncoder();
                var bmp = new TransformedBitmap();
                bmp.BeginInit();

                videoBox.Dispatcher.Invoke(delegate { bmp.Source = (BitmapSource)videoBox.Source; });

                if (flip) bmp.Transform = new ScaleTransform(-1, 1);

                bmp.EndInit();

                imageHeight = bmp.Height;
                imageWidth = bmp.Width;

                encoder.Frames.Add(BitmapFrame.Create(bmp));
                encoder.Save(imageFileStream);
                imageFileStream.Position = 0;

                FaceAttributeType[] attributes = new FaceAttributeType[] {FaceAttributeType.HeadPose};
                
                return await faceServiceClient.DetectAsync(imageFileStream, true, false, attributes);
            }
        }

        private async void btnContinue_Click(object sender, RoutedEventArgs e)
        {
			Grid.SetColumnSpan(btnCalibrate, 2);
			camera.Start();
			await StartChecking();
		}

		private async Task StartChecking()
		{
			while (true)
			{
				sw.Restart();

				Face[] faces = await GetJSON();

				if (faces.Length < 1 || BadPosture(faces[0]))
				{
					SystemSounds.Beep.Play();
				}

				sw.Stop();
				lblLag.Content = sw.ElapsedMilliseconds + "ms";

				if (sw.ElapsedMilliseconds < 3000)
				{
					TimeSpan x = TimeSpan.FromMilliseconds(3000 - sw.ElapsedMilliseconds);
					await Task.Delay(x);
				}
			}
		}

		private bool BadPosture(Face faceToCheck)
		{
			return Math.Abs(faceToCheck.FaceRectangle.Left - goodFace.FaceRectangle.Left) > goodFace.FaceRectangle.Width;
		}

		private void SavePicture()
        {
            videoBox.Dispatcher.Invoke(delegate
            {
                var encoder = new PngBitmapEncoder();

                using (FileStream stream = new FileStream("snapshot.bmp", FileMode.Create))
                {
                    // Flips the bitmap and saves it to disk
                    var flippedBitmap = new TransformedBitmap();
                    flippedBitmap.BeginInit();
                    flippedBitmap.Source = (BitmapSource)videoBox.Source;
                    var transform = new ScaleTransform(-1, 1);
                    flippedBitmap.Transform = transform;
                    flippedBitmap.EndInit();
                    encoder.Frames.Add(BitmapFrame.Create(flippedBitmap));
                    encoder.Save(stream);
                }

            });
        }
    }
}
