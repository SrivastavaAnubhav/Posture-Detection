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
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

using System.Drawing;
using System.IO;
using System.Diagnostics;
using System.Media;

using System.Threading;

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
        // TODO: Tilt limit
        // TODO: Make settings_gear.png relative

        private System.Windows.Shapes.Rectangle rct = new System.Windows.Shapes.Rectangle()
        {
            Fill = System.Windows.Media.Brushes.Transparent,
            Stroke = System.Windows.Media.Brushes.Red,
            StrokeThickness = 3,
            Visibility = Visibility.Hidden
        };
		private double imageHeight, imageWidth, heightMult = 1, widthMult = 1, rollLimit = 50, yawLimit = 50;
		private int consecutiveWrongLimit = 1, consecutiveWrong = 0;
        private FilterInfoCollection videoDevicesList;
        private VideoCaptureDevice camera;
        private Stopwatch sw = new Stopwatch();
        private readonly IFaceServiceClient faceServiceClient = new FaceServiceClient("64204927d74943918f0af8c8151e48c7");
        private bool flip = true, settingsOpen;
		private Face goodFace;

        public MainWindow()
        {
            InitializeComponent();
            rctHolder.Children.Add(rct);
        }

        private void btnSettings_Click(object sender, RoutedEventArgs e)
        {
			camera.SignalToStop();
            SettingsForm settingsForm = new SettingsForm(flip, heightMult, widthMult, rollLimit, yawLimit, consecutiveWrongLimit);
            settingsForm.Owner = this;
			settingsOpen = true;
            settingsForm.ShowDialog();

			// Code moves on when settings dialog stops

			settingsOpen = false;
			flip = (bool)settingsForm.cbFlip.IsChecked;
			heightMult = settingsForm.slHeight.Value;
			widthMult = settingsForm.slWidth.Value;
			rollLimit = settingsForm.slRoll.Value;
			yawLimit = settingsForm.slYaw.Value;
			consecutiveWrongLimit = (int)settingsForm.slCWLimit.Value;
			consecutiveWrong = 0;

			videoBox.RenderTransform = new ScaleTransform(Convert.ToInt32(!flip)*2 - 1, 1);

			camera.Start();
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
            VideoBoxFlash();

            sw.Start();

            Face[] faces = await GetJSON();

            sw.Stop();
            lblLag.Content = sw.ElapsedMilliseconds + "ms";
            sw.Reset();

            if (faces.Length > 0)
            {
				goodFace = faces[0];
                BoxFace(faces[0]);
				lblNotifier.Content = "Closer to 0 is better\n";
				lblNotifier.Content += string.Format("Pitch: {0}, Roll: {1}, Yaw: {2}", faces[0].FaceAttributes.HeadPose.Pitch, faces[0].FaceAttributes.HeadPose.Roll, faces[0].FaceAttributes.HeadPose.Yaw);
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
			btnSettings.IsEnabled = true;
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

        private void VideoBoxFlash()
        {
			videoBox.BeginAnimation(System.Windows.Controls.Image.OpacityProperty, new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(500)));
			videoBox.BeginAnimation(System.Windows.Controls.Image.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(1000)));
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
                
                return await faceServiceClient.DetectAsync(imageFileStream, false, false, attributes);
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
				// TODO: Find elegant way of doing this
				if (!settingsOpen)
				{
					sw.Restart();

					Face[] faces = await GetJSON();

					if (faces.Length < 1 || IsPostureBad(faces[0]))
					{
						consecutiveWrong++;
					}
					else consecutiveWrong = 0;

					if (consecutiveWrong >= consecutiveWrongLimit)
					{
						SystemSounds.Beep.Play();
					}

					sw.Stop();
					lblLag.Content = sw.ElapsedMilliseconds + "ms";

					if (sw.ElapsedMilliseconds < 4000)
					{
						TimeSpan x = TimeSpan.FromMilliseconds(4000 - sw.ElapsedMilliseconds);
						await Task.Delay(x);
					}
				}
				else
				{
					await Task.Delay(500);
				}
			}
		}

		private bool IsPostureBad(Face faceToCheck)
		{

			return Math.Abs(faceToCheck.FaceRectangle.Left - goodFace.FaceRectangle.Left) > widthMult*goodFace.FaceRectangle.Width ||
				Math.Abs(faceToCheck.FaceRectangle.Top- goodFace.FaceRectangle.Top) > heightMult*goodFace.FaceRectangle.Height ||
				Math.Abs(faceToCheck.FaceAttributes.HeadPose.Roll - goodFace.FaceAttributes.HeadPose.Roll) > rollLimit ||
				Math.Abs(faceToCheck.FaceAttributes.HeadPose.Yaw - goodFace.FaceAttributes.HeadPose.Yaw) > yawLimit;
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
