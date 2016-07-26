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
        //TODO: Ask whether calibration picture is OK
        //TODO: Show differences
        //TODO: Preferences section: set tolerances, set camera

        private double imageHeight, imageWidth;
        private FilterInfoCollection videoDevicesList;
        private VideoCaptureDevice camera;
        private Stopwatch sw = new Stopwatch();
        private readonly IFaceServiceClient faceServiceClient = new FaceServiceClient("64204927d74943918f0af8c8151e48c7");

        public MainWindow()
        {
            InitializeComponent();
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
            sw.Start();
            videoBox.Dispatcher.Invoke(delegate { videoBox.Source = BitmapToImageSource(e.Frame); });

            sw.Stop();
            lblLag.Dispatcher.Invoke(delegate { lblLag.Content = sw.ElapsedMilliseconds; });
            sw.Reset();

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

            if (camera.IsRunning == false)
            {
                camera.Start();
            }

            await Countdown();
            camera.SignalToStop();
            await VideoBoxFlash();
            Face[] jsonFaces = await GetJSON();

            if (jsonFaces.Length > 0) {
                BoxFace(jsonFaces[0]);
            }

            btnCalibrate.IsEnabled = true;
        }

        private void BoxFace(Face face)
        {
            System.Windows.Shapes.Rectangle rct = new System.Windows.Shapes.Rectangle();
            rct.Fill = System.Windows.Media.Brushes.Transparent;
            rct.Stroke = System.Windows.Media.Brushes.Red;
            rct.StrokeThickness = 3;

            rct.Height = videoBox.ActualHeight * face.FaceRectangle.Height / imageHeight;
            rct.Width = videoBox.ActualWidth * face.FaceRectangle.Width / imageWidth;

            double ratio = videoBox.Source.Width / videoBox.Source.Height;
            double leftPercent = face.FaceRectangle.Left / imageWidth;
            double topPercent = face.FaceRectangle.Top / imageHeight;

            double bigWidth = 0;
            double bigHeight = 0;

            for (int i = Grid.GetColumn(videoBox); i < Grid.GetColumn(videoBox) + Grid.GetColumnSpan(videoBox); i++)
            {
                bigWidth += Organizer.ColumnDefinitions[i].ActualWidth;
            }

            for (int i = Grid.GetRow(videoBox); i < Grid.GetRow(videoBox) + Grid.GetRowSpan(videoBox); i++) {
                bigHeight += Organizer.RowDefinitions[i].ActualHeight;
            }

            // Dividing by 2 because the space is on both sides
            double trimWidth = (bigWidth - videoBox.ActualWidth) / 2;
            double trimHeight = (bigHeight- videoBox.ActualHeight) / 2;
            
            // Only one of these will actually be distinct
            // May decide to put an if statement to reflect this later
            Canvas.SetLeft(rct, videoBox.ActualWidth * leftPercent + trimWidth);
            Canvas.SetTop(rct, videoBox.ActualHeight * topPercent + trimHeight);

            rctHolder.Children.Add(rct);
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

        private void btnSettings_Click(object sender, RoutedEventArgs e)
        {

        }

        private async Task<Face[]> GetJSON()
        {
            using (MemoryStream imageFileStream = new MemoryStream())
            {
                var encoder = new PngBitmapEncoder();
                var flippedBitmap = new TransformedBitmap();
                flippedBitmap.BeginInit();

                videoBox.Dispatcher.Invoke(delegate { flippedBitmap.Source = (BitmapSource)videoBox.Source; });

                flippedBitmap.Transform = new ScaleTransform(-1, 1);
                flippedBitmap.EndInit();
                imageHeight = flippedBitmap.Height;
                imageWidth = flippedBitmap.Width;
                encoder.Frames.Add(BitmapFrame.Create(flippedBitmap));
                encoder.Save(imageFileStream);
                imageFileStream.Position = 0;

                FaceAttributeType[] attributes = new FaceAttributeType[] {FaceAttributeType.HeadPose};
                
                return await faceServiceClient.DetectAsync(imageFileStream, true, false, attributes);
            }
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
