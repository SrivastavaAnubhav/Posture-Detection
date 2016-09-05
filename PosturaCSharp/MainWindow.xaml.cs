using AForge.Video;
using AForge.Video.DirectShow;
using Emgu.CV;
using Emgu.CV.Structure;
using Microsoft.ProjectOxford.Face;
using Microsoft.ProjectOxford.Face.Contract;
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Media;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace PosturaCSharp
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>

    public partial class MainWindow : Window
    {
        // TODO: Do the TODOs

        private enum AppState
        {
            Started,
            Calibrating,
            WaitingForContinue,
            Running,
            Settings
        };
        // Can't use System.Drawing.Rectangle because it has no way to change visibility (can't delete it for recalibrate)
        private System.Windows.Shapes.Rectangle rctRed = new System.Windows.Shapes.Rectangle()
        {
            Fill = System.Windows.Media.Brushes.Transparent,
            Stroke = System.Windows.Media.Brushes.Red,
            StrokeThickness = 3,
            Visibility = Visibility.Hidden
        };
        private double imageHeight, imageWidth, topMult = 0.5, leftMult = 0.5, heightMult = 0.5, rollLimit = 50, yawLimit = 50, normalWidth = 700, normalHeight = 500;
        private const double minimizedHeight = 200;
        private int consecutiveWrongLimit = 1, consecutiveWrong = 0;
        private bool flip = true, isSmall = false, useFaceAPI = false;
        private string azureSubKey = "";
        private FilterInfoCollection videoDevicesList;
        private VideoCaptureDevice camera;
        private Stopwatch sw = new Stopwatch();
        private Face goodFace;
        private Thread runningThread;
        private AppState appState = AppState.Started;
        CascadeClassifier cascadeClassifier = new CascadeClassifier(AppDomain.CurrentDomain.BaseDirectory + "\\Resources\\haarcascade_frontalface_default.xml");

        public MainWindow()
        {
            InitializeComponent();
            LoadAndParseSettings();
            MainForm.Height = normalHeight;
            MainForm.Width = normalWidth;
            rctHolder.Children.Add(rctRed);
        }

        private void LoadAndParseSettings()
        {
            if (File.Exists("FaceSettings.txt"))
            {
                using (StreamReader sr = new StreamReader("FaceSettings.txt"))
                {
                    flip = Convert.ToBoolean(sr.ReadLine());
                    useFaceAPI = Convert.ToBoolean(sr.ReadLine());
                    azureSubKey = sr.ReadLine();
                    topMult = Convert.ToDouble(sr.ReadLine());
                    leftMult = Convert.ToDouble(sr.ReadLine());
                    heightMult = Convert.ToDouble(sr.ReadLine());
                    rollLimit = Convert.ToDouble(sr.ReadLine());
                    yawLimit = Convert.ToDouble(sr.ReadLine());
                    consecutiveWrongLimit = Convert.ToInt32(sr.ReadLine());
                }
            }
        }

        private void btnSettings_Click(object sender, RoutedEventArgs e)
        {
            camera.SignalToStop();
            SettingsForm settingsForm = new SettingsForm(flip, useFaceAPI, azureSubKey, topMult, leftMult, heightMult, rollLimit, yawLimit, consecutiveWrongLimit);
            settingsForm.Owner = this;

            AppState tempState = appState;
            appState = AppState.Settings;
            settingsForm.ShowDialog();

            // Code moves on when settings dialog stops

            appState = tempState;

            // Update settings
            LoadAndParseSettings();

            // Converts bool to -1 and 1
            videoBox.RenderTransform = new ScaleTransform(Convert.ToInt32(!flip) * 2 - 1, 1);

            if (appState != AppState.WaitingForContinue) camera.Start();

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

            if (runningThread != null) runningThread.Abort();
        }

        private async void btnCalibrate_Click(object sender, RoutedEventArgs e)
        {
            btnCalibrate.IsEnabled = false;
            btnContinue.IsEnabled = false;
            btnSettings.IsEnabled = false;
            rctRed.Visibility = Visibility.Hidden;
            appState = AppState.Calibrating;

            if (camera.IsRunning == false)
            {
                camera.Start();
            }

            await Countdown();
            camera.SignalToStop();

            // TODO: Is this the best way to do this?
            DoubleAnimation fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(500));
            DoubleAnimation fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(1000));
            fadeIn.Completed += new EventHandler(fadeIn_Completed);
            videoBox.Dispatcher.Invoke(delegate
            {
                videoBox.BeginAnimation(System.Windows.Controls.Image.OpacityProperty, fadeOut);
                videoBox.BeginAnimation(System.Windows.Controls.Image.OpacityProperty, fadeIn);
            });
 
        }

        private void fadeIn_Completed(object sender, EventArgs e)
        {
            Calibrate();
        }

        private async void Calibrate()
        {
            try
            {
                sw.Start();

                Face[] faces = useFaceAPI ? await GetFacesFaceAPIAsync() : GetFacesEmguCV();

                sw.Stop();
                lblLag.Content = sw.ElapsedMilliseconds + "ms";
                sw.Reset();

                if (faces.Length > 0)
                {
                    goodFace = faces[0];
                    BoxFace(faces[0]);
                    if (useFaceAPI)
                    {
                        lblNotifier.Content = string.Format("Pitch: {0}, Roll: {1}, Yaw: {2}", faces[0].FaceAttributes.HeadPose.Pitch, faces[0].FaceAttributes.HeadPose.Roll, faces[0].FaceAttributes.HeadPose.Yaw);
                    }
                    else lblNotifier.Content = "";
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
                appState = AppState.WaitingForContinue;
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
            // Note to self: setting rctRed's properties will need dispatcher if done through EmguCV in the future

            double topPercent = face.FaceRectangle.Top / imageHeight;
            double leftPercent = face.FaceRectangle.Left / imageWidth;
            double bigHeight = 0;
            double bigWidth = MainGrid.ColumnDefinitions[0].ActualWidth;

            // Get bigHeight as the sum of all the rows the grid occupies
            for (int i = Grid.GetRow(videoBox); i < Grid.GetRow(videoBox) + Grid.GetRowSpan(videoBox); i++)
            {
                bigHeight += MainGrid.RowDefinitions[i].ActualHeight;
            }

            // At most one of these will be non-zero (can only be limited by either height or width, or neither, not both)
            // May decide to put an if statement to reflect this later
            // Dividing by 2 because the space is on both sides
            double trimHeight = (bigHeight - videoBox.ActualHeight) / 2;
            double trimWidth = (bigWidth - videoBox.ActualWidth) / 2;

            rctRed.Height = videoBox.ActualHeight * face.FaceRectangle.Height / imageHeight;
            rctRed.Width = videoBox.ActualWidth * face.FaceRectangle.Width / imageWidth;
            rctRed.Visibility = Visibility.Visible;
            Canvas.SetTop(rctRed, videoBox.ActualHeight * topPercent + trimHeight);
            Canvas.SetLeft(rctRed, videoBox.ActualWidth * leftPercent + trimWidth);
        }

        private void MainForm_Deactivated(object sender, EventArgs e)
        {
            if (appState != AppState.Settings && !isSmall)
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
                // Title bar thickness for tool is 23; 2 * 8 + 23 = 39 extra padding on height
                // Width is then increased by 16 to account for padding on both sides
                MainForm.Width = (MainForm.ActualHeight - 39) * imageWidth / imageHeight + 16;
            }
        }

        private void MainForm_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if ((appState == AppState.Running || appState == AppState.WaitingForContinue) && goodFace != null) BoxFace(goodFace);
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

        private async void btnContinue_Click(object sender, RoutedEventArgs e)
        {
            appState = AppState.Running;
            Grid.SetColumnSpan(btnCalibrate, 2);
            camera.Start();

            if (useFaceAPI)
            {
                await StartCheckingFaceAPIAsync();
            }
            else
            {
                runningThread = new Thread(new ThreadStart(CheckEmguCV));
                runningThread.IsBackground = true;
                runningThread.Name = "EmguChecking";
                runningThread.Start();
            }

        }

        private void CheckEmguCV()
        {
            // TODO: Add cancellation token
            while (true)
            {
                if (appState == AppState.Running)
                {
                    sw.Restart();

                    Face[] faces = GetFacesEmguCV();

                    // TODO: Consecutive wrong should be based on time
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
                    lblLag.Dispatcher.Invoke(() => lblLag.Content = sw.ElapsedMilliseconds + "ms");
                }
                else Thread.Sleep(100);
            }
        }

        private async Task StartCheckingFaceAPIAsync()
        {
            while (true)
            {
                if (appState == AppState.Running)
                {
                    sw.Restart();

                    Face[] faces;

                    faces = await GetFacesFaceAPIAsync();

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

                    int refreshGap = 4000;
                    // Wait 4 seconds between photos because of the limit of the API
                    if (sw.ElapsedMilliseconds < refreshGap)
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(refreshGap - sw.ElapsedMilliseconds));
                    }
                }
                else await Task.Delay(100);
            }
        }

        private async Task<Face[]> GetFacesFaceAPIAsync()
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

                imageFileStream.Position = 0;

                FaceServiceClient faceServiceClient = new FaceServiceClient(azureSubKey);
                FaceAttributeType[] attributes = new FaceAttributeType[] { FaceAttributeType.HeadPose };

                return await faceServiceClient.DetectAsync(imageFileStream, false, false, attributes);
            }
        }

        private Face[] GetFacesEmguCV()
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

                Bitmap bmp = (Bitmap)Bitmap.FromStream(imageFileStream);

                using (Image<Gray, byte> grayframe = new Image<Gray, byte>(bmp))
                {
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
            }
        }

        private bool IsPostureBad(Face faceToCheck)
        {
            if (Math.Abs(faceToCheck.FaceRectangle.Left - goodFace.FaceRectangle.Left) > leftMult * goodFace.FaceRectangle.Width)
            {
                lblNotifier.Dispatcher.Invoke(() => lblNotifier.Content = "Too far left/right");
                return true;
            }
            else if (Math.Abs(faceToCheck.FaceRectangle.Top - goodFace.FaceRectangle.Top) > topMult * goodFace.FaceRectangle.Height)
            {
                lblNotifier.Dispatcher.Invoke(() => lblNotifier.Content = "Too far up/down");
                return true;
            }
            else if (Math.Abs(faceToCheck.FaceRectangle.Height - goodFace.FaceRectangle.Height) > heightMult * goodFace.FaceRectangle.Height)
            {
                lblNotifier.Dispatcher.Invoke(() => lblNotifier.Content = "Too near/far");
                return true;
            }
            else if (Math.Abs(faceToCheck.FaceAttributes.HeadPose.Roll - goodFace.FaceAttributes.HeadPose.Roll) > rollLimit)
            {
                lblNotifier.Dispatcher.Invoke(() => lblNotifier.Content = "Too much roll");
                return true;
            }
            else if (Math.Abs(faceToCheck.FaceAttributes.HeadPose.Yaw - goodFace.FaceAttributes.HeadPose.Yaw) > yawLimit)
            {
                lblNotifier.Dispatcher.Invoke(() => lblNotifier.Content = "Too much yaw");
                return true;
            }
            else
            {
                lblNotifier.Dispatcher.Invoke(() => lblNotifier.Content = "");
                return false;
            }
        }
    }
}
