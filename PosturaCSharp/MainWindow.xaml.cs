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

using System.Drawing.Imaging;

using Microsoft.ProjectOxford.Face;
using Microsoft.ProjectOxford.Face.Contract;

using AForge.Video;
using AForge.Video.DirectShow;

using Emgu.CV;
using Emgu.CV.Structure;
using Emgu.CV.UI;
using Emgu.Util;

namespace PosturaCSharp
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>

    public partial class MainWindow : Window
    {
		// TODO: Scale red box with form (for auto-minimize)
		// TODO: Publish

		// Can't use System.Drawing.Rectangle because it has no way to change visibility (can't delete it for recalibrate)
        private System.Windows.Shapes.Rectangle rctRed = new System.Windows.Shapes.Rectangle()
        {
            Fill = System.Windows.Media.Brushes.Transparent,
            Stroke = System.Windows.Media.Brushes.Red,
            StrokeThickness = 3,
            Visibility = Visibility.Hidden
        };
		private double imageHeight, imageWidth, heightMult = 1, widthMult = 1, rollLimit = 50, yawLimit = 50, normalWidth = 700, normalHeight = 500;
		private const double minimizedHeight = 200;
		private int consecutiveWrongLimit = 1, consecutiveWrong = 0;
		private bool flip = true, isSettingsOpen = false, isWaitingForContinue = false, isRunning = false, isSmall = false, useFaceAPI = false;
		private string azureSubKey = string.Empty;
		private FilterInfoCollection videoDevicesList;
        private VideoCaptureDevice camera;
        private Stopwatch sw = new Stopwatch();
		private Face goodFace;

        public MainWindow()
        {
            InitializeComponent();
			MainForm.Height = normalHeight;
			MainForm.Width = normalWidth;
			rctHolder.Children.Add(rctRed);
        }

        private void btnSettings_Click(object sender, RoutedEventArgs e)
        {
			camera.SignalToStop();
            SettingsForm settingsForm = new SettingsForm(flip, useFaceAPI, azureSubKey, heightMult, widthMult, rollLimit, yawLimit, consecutiveWrongLimit);
            settingsForm.Owner = this;
			isSettingsOpen = true;
			settingsForm.ShowDialog();

			// Code moves on when settings dialog stops

			isSettingsOpen = false;

			// Update settings
			flip = (bool)settingsForm.cbFlip.IsChecked;
			useFaceAPI = (bool)settingsForm.cbFaceAPI.IsChecked;
			azureSubKey = settingsForm.tbAzureKey.Text;
			heightMult = settingsForm.slHeight.Value;
			widthMult = settingsForm.slWidth.Value;
			rollLimit = settingsForm.slRoll.Value;
			yawLimit = settingsForm.slYaw.Value;
			consecutiveWrongLimit = (int)settingsForm.slCWLimit.Value;
			consecutiveWrong = 0;

			// Converts bool to -1 and 1
			videoBox.RenderTransform = new ScaleTransform(Convert.ToInt32(!flip)*2 - 1, 1);

			if (!isWaitingForContinue) camera.Start();

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
            // While this means reflection must be done twice (once here, once when checking faces), it 
			// allows for no lag on the live feed (downside is processing takes a bit longer)
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

				imageWidth = bitmapimage.Width;
				imageHeight = bitmapimage.Height;

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
			isRunning = false;
			isWaitingForContinue = false;
			btnCalibrate.IsEnabled = false;
            btnContinue.IsEnabled = false;
            btnSettings.IsEnabled = false;
            rctRed.Visibility = Visibility.Hidden;

            if (camera.IsRunning == false)
            {
                camera.Start();
            }

            await Countdown();
            camera.SignalToStop();
            VideoBoxFlash();
			isWaitingForContinue = true;

			try
			{
				sw.Start();

				Face[] faces = await GetFaces();

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
			}
			catch (FaceAPIException ex)
			{
				tbCountdown.Text = ex.ErrorMessage;
			}
			finally
			{
				btnCalibrate.Content = "Recalibrate!";
				btnCalibrate.IsEnabled = true;
				btnSettings.IsEnabled = true;
			}
		}

		private void MainForm_MouseDown(object sender, MouseButtonEventArgs e)
		{
			isSmall = false;
			MainGrid.RowDefinitions[3].Height = new GridLength(1.2, GridUnitType.Star);
			MainGrid.RowDefinitions[4].Height = new GridLength(2, GridUnitType.Star);
			MainForm.ResizeMode = ResizeMode.CanResize;
			MainForm.WindowStyle = WindowStyle.SingleBorderWindow;
			MainForm.Height = normalHeight;
			MainForm.Width = normalWidth;
		}

		private void BoxFace(Face face)
        {

            rctRed.Height = videoBox.ActualHeight * face.FaceRectangle.Height / imageHeight;
            rctRed.Width = videoBox.ActualWidth * face.FaceRectangle.Width / imageWidth;
            double leftPercent = face.FaceRectangle.Left / imageWidth;
            double topPercent = face.FaceRectangle.Top / imageHeight;

            double bigWidth = MainGrid.ColumnDefinitions[0].ActualWidth;
            double bigHeight = 0;

            for (int i = Grid.GetRow(videoBox); i < Grid.GetRow(videoBox) + Grid.GetRowSpan(videoBox); i++) {
                bigHeight += MainGrid.RowDefinitions[i].ActualHeight;
            }

			// At most one of these will be non-zero (can only be limited by either height or width, or neither, not both)
			// May decide to put an if statement to reflect this later
			// Dividing by 2 because the space is on both sides
			double trimWidth = (bigWidth - videoBox.ActualWidth) / 2;
            double trimHeight = (bigHeight - videoBox.ActualHeight) / 2;

            rctRed.Visibility = Visibility.Visible;

			Canvas.SetTop(rctRed, videoBox.ActualHeight * topPercent + trimHeight);
			Canvas.SetLeft(rctRed, videoBox.ActualWidth * leftPercent + trimWidth);
		}

		private void MainForm_Deactivated(object sender, EventArgs e)
		{
			if (!isSettingsOpen && !isSmall)
			{
				MainGrid.RowDefinitions[3].Height = new GridLength(0);
				MainGrid.RowDefinitions[4].Height = new GridLength(0);
				MainForm.ResizeMode = ResizeMode.NoResize;
				MainForm.WindowStyle = WindowStyle.ToolWindow;
				normalHeight = MainForm.Height;
				normalWidth = MainForm.Width;
				MainForm.Height = minimizedHeight;
				isSmall = true;

				// Inner padding values (cannot change) are as follows: None = 7, SingleBorder/Tool = 8, 3D = 10
				// Title bar thickness for tool is 25; 2 * 8 + 25 = 39 extra padding on height
				// Width is then increased by 16 to account for padding on both sides
				MainForm.Width = (MainForm.ActualHeight - 39) * imageWidth / imageHeight + 16;
			}
		}

		private void MainForm_SizeChanged(object sender, SizeChangedEventArgs e)
		{
			if (isRunning || isWaitingForContinue) BoxFace(goodFace);
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

		private async Task<Face[]> GetFaces()
        {
            using (MemoryStream imageFileStream = new MemoryStream())
            {
				BitmapSource currSource = null;

                videoBox.Dispatcher.Invoke(delegate { currSource = (BitmapSource)videoBox.Source; });

				var encoder = new PngBitmapEncoder();
				var flipped_bmp = new TransformedBitmap();
				flipped_bmp.BeginInit();
				flipped_bmp.Source = currSource;

				if (flip) flipped_bmp.Transform = new ScaleTransform(-1, 1);

				flipped_bmp.EndInit();

				encoder.Frames.Add(BitmapFrame.Create(flipped_bmp));
				encoder.Save(imageFileStream);

				if (useFaceAPI)
				{
					imageFileStream.Position = 0;

					FaceServiceClient faceServiceClient = new FaceServiceClient(azureSubKey);
					FaceAttributeType[] attributes = new FaceAttributeType[] { FaceAttributeType.HeadPose };

					return await faceServiceClient.DetectAsync(imageFileStream, false, false, attributes);
				}
				else
				{
					Bitmap bmp = (Bitmap)Bitmap.FromStream(imageFileStream);

					Task<Face[]> DetectEmguCVAsync = new Task<Face[]>(() => FacesFromRectangles(bmp));
					DetectEmguCVAsync.Start();

					return await DetectEmguCVAsync;
				}
			}
        }

		private Face[] FacesFromRectangles(Bitmap bmp)
		{
			Image<Bgr, byte> cv_bmp = new Image<Bgr, byte>(bmp);
			Image<Gray, byte> grayframe = cv_bmp.Convert<Gray, byte>();

			CascadeClassifier cascadeClassifier = new CascadeClassifier(@"C:\Users\Anubhav\Documents\Miscellaneous\PosturaCSharp\haarcascade_frontalface_default.xml");

			System.Drawing.Rectangle[] faceRects = cascadeClassifier.DetectMultiScale(grayframe, 1.1, 10, System.Drawing.Size.Empty);

			Face[] faces = new Face[faceRects.Length];
			for (int i = 0; i < faceRects.Length; i++)
			{
				faces[i] = new Face();
				faces[i].FaceAttributes = new FaceAttributes();
				faces[i].FaceAttributes.HeadPose = new HeadPose();
				faces[i].FaceAttributes.HeadPose.Pitch = 0;
				faces[i].FaceAttributes.HeadPose.Roll = 0;
				faces[i].FaceAttributes.HeadPose.Yaw = 0;
				faces[i].FaceRectangle = new FaceRectangle();
				faces[i].FaceRectangle.Left = faceRects[i].Left;
				faces[i].FaceRectangle.Top = faceRects[i].Top;
				faces[i].FaceRectangle.Height = faceRects[i].Height;
				faces[i].FaceRectangle.Width = faceRects[i].Width;
			}

			return faces;
		}

        private async void btnContinue_Click(object sender, RoutedEventArgs e)
        {
			isRunning = true;
			Grid.SetColumnSpan(btnCalibrate, 2);
			isWaitingForContinue = false;
			camera.Start();

			await StartChecking();
		}

		private async Task StartChecking()
		{
			while (true)
			{
				if (!isSettingsOpen)
				{
					sw.Restart();

					Face[] faces;

					faces = await GetFaces();

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

					int refreshGap = useFaceAPI ? 4000 : 1000;
					// Wait 4 seconds between photos because of the limit of the API, and 1000 seems like a nice number
					if (sw.ElapsedMilliseconds < refreshGap)
					{
						await Task.Delay(TimeSpan.FromMilliseconds(refreshGap - sw.ElapsedMilliseconds));
					}
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
