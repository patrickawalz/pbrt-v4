// (not in pbrt-v4) magiview/MainWindow.xaml.cs
//
// A window on a progressive render. It holds a RenderSession (progressive.cs): the render runs on a thread of the window's, the
// session's film thread makes an image at the interval asked for, and the window shows it. The buttons are the session's
// Pause(), Resume(), Stop() and Threads; the rows of the panel are the settings of the scene's light layers, and a change to
// one asks the session for a new image, of a render that is running, paused or finished alike.

using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using pbrt;

namespace magiview
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            // The numbers of scene files and images are read and written the same on every machine
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
            Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
            InitializeComponent();
            zoomBox.Items.Add("Fit");
            for (int i = 0; i < ZoomPanel.LevelCount; ++i)
            {
                zoomBox.Items.Add(ZoomText(ZoomPanel.Level(i)));
            }
            ShowZoom();
            zoomPanel.ZoomFactorChanged += OnZoomFactorChanged;
            // The renderer is started once for the life of the window
            PBRTOptions options = new PBRTOptions();
            options.quiet = true;
            pbrt.pbrt.InitPBRT(options);
            threadsText.Text = pbrt.pbrt.RunningThreads().ToString(CultureInfo.InvariantCulture);
            statusTimer = new DispatcherTimer();
            statusTimer.Interval = TimeSpan.FromMilliseconds(250);
            statusTimer.Tick += OnStatusTick;
            statusTimer.Start();
        }

        // MainWindow Private Methods
        private void OnOpen(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dialog = new OpenFileDialog();
            dialog.Filter = "pbrt scenes (*.pbrt)|*.pbrt|All files (*.*)|*.*";
            if (dialog.ShowDialog(this) == true)
            {
                sceneFile = dialog.FileName;
                statusText.Text = sceneFile;
                startButton.IsEnabled = session == null;
            }
        }

        private static double Number(TextBox box)
        {
            double value;
            if (!double.TryParse(box.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) || value < 0)
            {
                value = 0;
                box.Text = "0";
            }
            return value;
        }

        private void OnStart(object sender, RoutedEventArgs e)
        {
            if (sceneFile == null || session != null)
            {
                return;
            }
            RenderSession started = new RenderSession();
            started.HaltSpp = (int)Number(haltSppBox);
            started.HaltTime = Number(haltTimeBox);
            started.DevelopInterval = Math.Max(0.05, Number(intervalBox));
            started.Developed += OnDeveloped;
            session = started;
            layersShown = 0;
            string file = sceneFile;
            renderThread = new Thread(() =>
            {
                // The scene is read and rendered here; the session is this render's
                Directory.SetCurrentDirectory(Path.GetDirectoryName(file));
                BasicScene scene = new BasicScene();
                BasicSceneBuilder builder = new BasicSceneBuilder(scene);
                pbrt.pbrt.ParseFiles(builder, new string[] { file });
                RenderSession.Next = started;
                pbrt.pbrt.RenderCPU(scene);
                Dispatcher.BeginInvoke(new Action(OnRenderEnded));
            });
            renderThread.IsBackground = true;
            renderThread.Name = "render";
            renderThread.Start();
            startButton.IsEnabled = false;
            openButton.IsEnabled = false;
            pauseButton.IsEnabled = true;
            pauseButton.Content = "Pause";
            stopButton.IsEnabled = true;
            fewerButton.IsEnabled = true;
            moreButton.IsEnabled = true;
            saveButton.IsEnabled = true;
        }

        private void OnRenderEnded()
        {
            // The session is kept: its film still makes images when a layer changes
            ended = session;
            session = null;
            startButton.IsEnabled = sceneFile != null;
            openButton.IsEnabled = true;
            pauseButton.IsEnabled = false;
            stopButton.IsEnabled = false;
            ShowImage(ended.LatestImage);
        }

        private void OnPause(object sender, RoutedEventArgs e)
        {
            if (session == null)
            {
                return;
            }
            if (session.IsPaused)
            {
                session.Resume();
                pauseButton.Content = "Pause";
            }
            else
            {
                session.Pause();
                pauseButton.Content = "Resume";
            }
        }

        private void OnStop(object sender, RoutedEventArgs e)
        {
            if (session != null)
            {
                // (the pass under way is finished first)
                session.Stop();
                stopButton.IsEnabled = false;
                pauseButton.IsEnabled = false;
            }
        }

        private void OnFewerThreads(object sender, RoutedEventArgs e)
        {
            if (session != null)
            {
                session.Threads = session.Threads - 1;
            }
        }

        private void OnMoreThreads(object sender, RoutedEventArgs e)
        {
            if (session != null)
            {
                session.Threads = session.Threads + 1;
            }
        }

        private void OnExposure(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            RenderSession shown = session ?? ended;
            if (shown != null)
            {
                ShowImage(shown.LatestImage);
            }
        }

        private void OnSave(object sender, RoutedEventArgs e)
        {
            RenderSession shown = session ?? ended;
            if (shown == null || shown.LatestImage == null)
            {
                return;
            }
            SaveFileDialog dialog = new SaveFileDialog();
            dialog.Filter = "OpenEXR (*.exr)|*.exr|PNG (*.png)|*.png";
            if (dialog.ShowDialog(this) == true)
            {
                // The image as the layers are set now, with the layers
                shown.LatestImage.Write(dialog.FileName, new pbrt.ImageMetadata());
            }
        }

        private void OnStatusTick(object sender, EventArgs e)
        {
            threadsText.Text = pbrt.pbrt.RunningThreads().ToString(CultureInfo.InvariantCulture);
            RenderSession shown = session ?? ended;
            if (shown == null || shown.Film == null)
            {
                return;
            }
            string state = session == null ? "finished" : (session.IsPaused ? "paused" : "rendering");
            statusText.Text = string.Format(CultureInfo.InvariantCulture, "{0}   {1} passes   {2:F1} s   {3:F2} passes/s   image at {4:F2} spp   {5}",
                                            state, shown.Passes, shown.Elapsed, shown.Elapsed > 0 ? shown.Passes / shown.Elapsed : 0,
                                            shown.LatestImageSpp, sceneFile);
        }

        // (on the session's film thread) There is a new image
        private void OnDeveloped(RenderSession developed)
        {
            // One update at a time: an image that comes while the window is still drawing the last is shown with the next
            if (Interlocked.Exchange(ref showing, 1) == 0)
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    ShowImage(developed.LatestImage);
                    Interlocked.Exchange(ref showing, 0);
                }));
            }
        }

        // The image in the window: its R, G and B at the exposure of the slider, as sRGB
        private void ShowImage(pbrt.Image image)
        {
            if (image == null)
            {
                return;
            }
            ShowLayers();
            int width = image.Resolution().x, height = image.Resolution().y;
            if (bitmap == null || bitmap.PixelWidth != width || bitmap.PixelHeight != height)
            {
                bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgr32, null);
                pixels = new byte[4 * width * height];
                filmImage.Source = bitmap;
            }
            float exposure = (float)Math.Pow(2, exposureSlider.Value);
            ImageChannelDesc desc = image.GetChannelDesc(new string[] { "R", "G", "B" });
            for (int y = 0; y < height; ++y)
            {
                for (int x = 0; x < width; ++x)
                {
                    ImageChannelValues rgb = image.GetChannels(new Point2i(x, y), desc);
                    int at = 4 * (y * width + x);
                    pixels[at] = Encode(exposure * rgb[2]);
                    pixels[at + 1] = Encode(exposure * rgb[1]);
                    pixels[at + 2] = Encode(exposure * rgb[0]);
                    pixels[at + 3] = 255;
                }
            }
            bitmap.WritePixels(new Int32Rect(0, 0, width, height), pixels, 4 * width, 0);
        }

        private static byte Encode(float linear)
        {
            if (!(linear > 0))
            {
                return 0;
            }
            if (linear >= 1)
            {
                return 255;
            }
            double encoded = linear <= 0.0031308 ? 12.92 * linear : 1.055 * Math.Pow(linear, 1 / 2.4) - 0.055;
            return (byte)(255 * encoded + 0.5);
        }

        // A row of the panel for each light layer of the scene that has none yet
        private void ShowLayers()
        {
            LightLayers layers = LightLayers.Scene;
            if (layersShown > layers.Count)
            {
                layersShown = 0;
            }
            if (layersShown == 0)
            {
                // (the heading stays)
                layersPanel.Children.RemoveRange(1, layersPanel.Children.Count - 1);
            }
            for (; layersShown < layers.Count; ++layersShown)
            {
                layersPanel.Children.Add(LayerRow(layers[layersShown]));
            }
        }

        private UIElement LayerRow(LightLayer layer)
        {
            StackPanel row = new StackPanel();
            row.Margin = new Thickness(0, 0, 0, 12);
            CheckBox enabled = new CheckBox();
            enabled.Content = layer.name;
            enabled.IsChecked = layer.enabled;
            enabled.Foreground = Brushes.White;
            enabled.FontWeight = FontWeights.Bold;
            enabled.Checked += (object sender, RoutedEventArgs e) => { layer.enabled = true; LayersChanged(); };
            enabled.Unchecked += (object sender, RoutedEventArgs e) => { layer.enabled = false; LayersChanged(); };
            row.Children.Add(enabled);
            row.Children.Add(LayerSlider("scale", 0, 8, layer.scale, (double value) => { layer.scale = (float)value; }));
            row.Children.Add(LayerSlider("temperature (K, 0: none)", 0, 12000, layer.temperature, (double value) =>
            {
                // (below 1000 K is none)
                layer.temperature = value < 1000 ? 0 : (float)value;
            }));
            row.Children.Add(LayerSlider("red", 0, 2, layer.tint.r, (double value) => { layer.tint = new RGB((float)value, layer.tint.g, layer.tint.b); }));
            row.Children.Add(LayerSlider("green", 0, 2, layer.tint.g, (double value) => { layer.tint = new RGB(layer.tint.r, (float)value, layer.tint.b); }));
            row.Children.Add(LayerSlider("blue", 0, 2, layer.tint.b, (double value) => { layer.tint = new RGB(layer.tint.r, layer.tint.g, (float)value); }));
            return row;
        }

        private UIElement LayerSlider(string name, double minimum, double maximum, double value, Action<double> set)
        {
            DockPanel line = new DockPanel();
            line.Margin = new Thickness(18, 2, 0, 0);
            TextBlock label = new TextBlock();
            label.Text = name;
            label.Width = 140;
            label.Foreground = Brushes.LightGray;
            DockPanel.SetDock(label, Dock.Left);
            line.Children.Add(label);
            TextBlock number = new TextBlock();
            number.Text = value.ToString("G4", CultureInfo.InvariantCulture);
            number.Width = 46;
            number.TextAlignment = TextAlignment.Right;
            number.Foreground = Brushes.White;
            DockPanel.SetDock(number, Dock.Right);
            line.Children.Add(number);
            Slider slider = new Slider();
            slider.Minimum = minimum;
            slider.Maximum = maximum;
            slider.Value = value;
            slider.ValueChanged += (object sender, RoutedPropertyChangedEventArgs<double> e) =>
            {
                set(e.NewValue);
                number.Text = e.NewValue.ToString("G4", CultureInfo.InvariantCulture);
                LayersChanged();
            };
            line.Children.Add(slider);
            return line;
        }

        // A setting of a layer has changed: a new image, of a render that runs, is paused or has ended
        private void LayersChanged()
        {
            if (session != null)
            {
                session.RequestDevelop();
            }
            else if (ended != null && ended.Film != null)
            {
                // (the film thread of a render that has ended is gone: the film is still, and the image is made here)
                double spp = ended.LatestImageSpp;
                ShowImage(ended.Film.GetImage(new pbrt.ImageMetadata(), spp > 0 ? (float)(ended.SplatUnit / spp) : 0, LightLayers.Scene));
            }
        }

        // The zoom of the film

        private static string ZoomText(double factor)
        {
            return (100 * factor).ToString("0.##", CultureInfo.InvariantCulture) + "%";
        }

        // The zoom as the text of the box (a text that is one of the levels selects it: that is not a level being chosen)
        private void ShowZoom()
        {
            showingZoom = true;
            zoomBox.Text = ZoomText(zoomPanel.ZoomFactor);
            showingZoom = false;
        }

        private void OnZoomFactorChanged(object sender, EventArgs e)
        {
            ShowZoom();
        }

        // A zoom level chosen from the list
        private void OnZoomChosen(object sender, SelectionChangedEventArgs e)
        {
            if (showingZoom || e.AddedItems.Count == 0)
            {
                return;
            }
            string chosen = (string)e.AddedItems[0];
            if (chosen == "Fit")
            {
                zoomPanel.ZoomToFit();
            }
            else
            {
                zoomPanel.ZoomTo(double.Parse(chosen.TrimEnd('%'), CultureInfo.InvariantCulture) / 100);
            }
            ShowZoom();
        }

        private void OnZoomOut(object sender, RoutedEventArgs e)
        {
            zoomPanel.ZoomOut();
        }

        private void OnZoomIn(object sender, RoutedEventArgs e)
        {
            zoomPanel.ZoomIn();
        }

        private void OnZoomFit(object sender, RoutedEventArgs e)
        {
            zoomPanel.ZoomToFit();
        }

        private void OnZoomActual(object sender, RoutedEventArgs e)
        {
            zoomPanel.Zoom100Percent();
        }

        // The keys of Photoshop: Ctrl 0 fits, Ctrl 1 is 100%, Ctrl + and Ctrl - go a zoom level in and out
        private void OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (System.Windows.Input.Keyboard.Modifiers != System.Windows.Input.ModifierKeys.Control)
            {
                return;
            }
            if (e.Key == System.Windows.Input.Key.D0 || e.Key == System.Windows.Input.Key.NumPad0)
            {
                zoomPanel.ZoomToFit();
            }
            else if (e.Key == System.Windows.Input.Key.D1 || e.Key == System.Windows.Input.Key.NumPad1)
            {
                zoomPanel.Zoom100Percent();
            }
            else if (e.Key == System.Windows.Input.Key.OemPlus || e.Key == System.Windows.Input.Key.Add)
            {
                zoomPanel.ZoomIn();
            }
            else if (e.Key == System.Windows.Input.Key.OemMinus || e.Key == System.Windows.Input.Key.Subtract)
            {
                zoomPanel.ZoomOut();
            }
            else
            {
                return;
            }
            e.Handled = true;
        }

        private void OnClosing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            statusTimer.Stop();
            if (session != null)
            {
                session.Stop();
                renderThread.Join(5000);
            }
        }

        // MainWindow Private Members
        private string sceneFile;
        private RenderSession session, ended;
        private Thread renderThread;
        private WriteableBitmap bitmap;
        private bool showingZoom;
        private byte[] pixels;
        private int showing;
        private int layersShown;
        private readonly DispatcherTimer statusTimer;
    }
}
