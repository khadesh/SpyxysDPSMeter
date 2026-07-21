using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;
using Brush = global::System.Windows.Media.Brush;
using Color = global::System.Windows.Media.Color;

namespace SpyxysDPSMeter
{
    public partial class App : global::System.Windows.Application
    {
        protected override async void OnStartup(
            StartupEventArgs e)
        {
            base.OnStartup(e);

            Window splashWindow = CreateSplashWindow();
            splashWindow.Show();

            await Task.Delay(TimeSpan.FromSeconds(2));

            splashWindow.Close();

            MainWindow mainWindow = new();
            MainWindow = mainWindow;
            mainWindow.Show();
        }

        private static Window CreateSplashWindow()
        {
            global::System.Windows.Controls.Image splashImage = new()
            {
                Stretch = Stretch.Uniform
            };

            try
            {
                splashImage.Source = new BitmapImage(
                    new Uri(
                        "pack://application:,,,/Assets/icon-spyxy-dps.png",
                        UriKind.Absolute));
            }
            catch
            {
                // The app can still start if the image resource is missing.
            }

            Border frame = new()
            {
                Background = new SolidColorBrush(
                    Color.FromArgb(245, 14, 16, 23)),
                BorderBrush = new SolidColorBrush(
                    Color.FromRgb(112, 72, 185)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(18),
                Padding = new Thickness(8),
                Child = splashImage
            };

            return new Window
            {
                Title = "Spyxy's DPS Meter",
                Width = 430,
                Height = 430,
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation =
                    WindowStartupLocation.CenterScreen,
                AllowsTransparency = true,
                Background = System.Windows.Media.Brushes.Transparent,
                ShowInTaskbar = false,
                ShowActivated = false,
                Topmost = true,
                Content = frame
            };
        }
    }
}
