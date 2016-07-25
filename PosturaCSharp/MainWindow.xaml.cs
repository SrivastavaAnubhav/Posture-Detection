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
            if (camera.IsRunning) camera.Stop();
            camera = new VideoCaptureDevice(videoDevicesList[cbDeviceList.SelectedIndex].MonikerString);
            camera.NewFrame += new NewFrameEventHandler(camera_NewFrame);
            camera.Start();
        }

        private void camera_NewFrame(object sender, NewFrameEventArgs e)
        {
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
            if (camera.IsRunning == false)
            {
                camera.Start();
            }

            await Countdown();
            await VideoBoxFlash();
            camera.SignalToStop();
            Face[] jsonFaces = await GetJSON();

            if (jsonFaces.Length > 0) {
                BoxFace(jsonFaces[0]);
            }
        }

        private void BoxFace(Face face)
        {
            System.Windows.Shapes.Rectangle rct = new System.Windows.Shapes.Rectangle();
            rct.Fill = System.Windows.Media.Brushes.Transparent;
            rct.Stroke = System.Windows.Media.Brushes.Red;
            rct.StrokeThickness = 3;

            if (Grid.)

            rct.Height = videoBox.ActualHeight * face.FaceRectangle.Height / imageHeight;
            rct.Width = videoBox.ActualWidth * face.FaceRectangle.Width / imageWidth;

            Canvas.SetLeft(rct, videoBox.ActualWidth * face.FaceRectangle.Left / imageWidth);
            Canvas.SetTop(rct, videoBox.ActualHeight * face.FaceRectangle.Top / imageHeight);

            

            rctHolder.Children.Add(rct);

            lblLag.Dispatcher.Invoke(delegate { lblLag.Content = sw.ElapsedMilliseconds.ToString() + "ms"; });
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

        private async Task<Face[]> GetJSON()
        {
            using (MemoryStream imageFileStream = new MemoryStream())
            {
                var encoder = new PngBitmapEncoder();
                var flippedBitmap = new TransformedBitmap();
                flippedBitmap.BeginInit();

                videoBox.Dispatcher.Invoke(delegate { flippedBitmap.Source = (BitmapSource)videoBox.Source; });

                var transform = new ScaleTransform(-1, 1);
                flippedBitmap.Transform = transform;
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
    }
}
