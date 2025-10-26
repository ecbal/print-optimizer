using Microsoft.Win32;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using ImageRgba32 = SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>;

namespace KirtasiyeApp
{
    public partial class MainWindow : Window
    {
        private ImageRgba32? _originalImage;
        private ImageRgba32? _currentImage;
        private System.Timers.Timer? _debounceTimer;

        private System.Windows.Point _cropStart;
        private bool _isCropping = false;

        public MainWindow()
        {
            InitializeComponent();
        }

        private void BtnSelect_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog { Filter = "Görseller|*.jpg;*.jpeg;*.png;*.bmp" };
            if (dialog.ShowDialog() == true)
            {
                TxtFilePath.Text = dialog.FileName;
                _originalImage = ImageRgba32.Load<Rgba32>(dialog.FileName);
                _currentImage = _originalImage.Clone();
                ShowPreview();
            }
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            if (_currentImage is null)
            {
                MessageBox.Show("Kaydedilecek görsel yok!");
                return;
            }

            var dialog = new SaveFileDialog
            {
                Filter = "PNG Görseli|*.png",
                FileName = "optimized.png"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    using var fs = new FileStream(dialog.FileName, FileMode.Create, FileAccess.Write);
                    _currentImage.Save(fs, new PngEncoder());
                    MessageBox.Show("Dosya başarıyla kaydedildi!");
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Kaydederken hata oluştu: {ex.Message}");
                }
            }
        }

        private void Slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_originalImage is null) return;

            _debounceTimer?.Stop();
            _debounceTimer = new System.Timers.Timer(150);
            _debounceTimer.AutoReset = false;
            _debounceTimer.Elapsed += (s, _) => Dispatcher.Invoke(ApplyAdjustments);
            _debounceTimer.Start();
        }

        private void ShowPreview()
        {
            if (_originalImage is null || _currentImage is null) return;
            PreviewOriginal.Source = ConvertToBitmap(_originalImage);
            PreviewEdited.Source = ConvertToBitmap(_currentImage);
        }

        private BitmapImage ConvertToBitmap(ImageRgba32 img)
        {
            using var ms = new MemoryStream();
            img.Save(ms, new PngEncoder());
            ms.Position = 0;

            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.StreamSource = ms;
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }

        private void ApplyAdjustments()
        {
            if (_originalImage is null) return;

            double total = TotalSlider.Value;
            double brightness = BrightnessSlider.Value * total;
            double contrast = ContrastSlider.Value * total;
            double sharpness = SharpnessSlider.Value;

            _currentImage = _originalImage.Clone(ctx =>
            {
                ctx.Brightness((float)brightness);
                ctx.Contrast((float)contrast);
                if (sharpness > 0)
                    ctx.GaussianSharpen((float)sharpness);
            });

            PreviewEdited.Source = ConvertToBitmap(_currentImage);
        }

        // === Crop İşlemleri ===
        private void CropCanvas_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (_currentImage is null) return;

            _isCropping = true;
            _cropStart = e.GetPosition(CropCanvas);
            Canvas.SetLeft(CropRect, _cropStart.X);
            Canvas.SetTop(CropRect, _cropStart.Y);
            CropRect.Width = 0;
            CropRect.Height = 0;
            CropRect.Visibility = Visibility.Visible;
        }

        private void CropCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isCropping) return;

            System.Windows.Point pos = e.GetPosition(CropCanvas);
            double x = Math.Min(pos.X, _cropStart.X);
            double y = Math.Min(pos.Y, _cropStart.Y);
            double w = Math.Abs(pos.X - _cropStart.X);
            double h = Math.Abs(pos.Y - _cropStart.Y);

            Canvas.SetLeft(CropRect, x);
            Canvas.SetTop(CropRect, y);
            CropRect.Width = w;
            CropRect.Height = h;
        }

        private void CropCanvas_MouseUp(object sender, MouseButtonEventArgs e)
        {
            _isCropping = false;
        }

        private void BtnCrop_Click(object sender, RoutedEventArgs e)
        {
            if (_currentImage is null || CropRect.Visibility == Visibility.Collapsed)
            {
                MessageBox.Show("Kırpma alanı seçmediniz!");
                return;
            }

            // Seçilen dikdörtgen bilgileri
            double rectX = Canvas.GetLeft(CropRect);
            double rectY = Canvas.GetTop(CropRect);
            double rectW = CropRect.Width;
            double rectH = CropRect.Height;

            if (rectW < 5 || rectH < 5)
            {
                MessageBox.Show("Kırpma alanı çok küçük!");
                return;
            }

            // Görselin kaynak boyutları (piksel)
            if (PreviewEdited.Source is not BitmapSource bmpSource)
                return;

            double imgW = bmpSource.PixelWidth;
            double imgH = bmpSource.PixelHeight;

            // Görselin ekrandaki boyutu
            double dispW = PreviewEdited.ActualWidth;
            double dispH = PreviewEdited.ActualHeight;

            // Image içeriğinin küçülme oranı
            double ratio = Math.Min(dispW / imgW, dispH / imgH);

            // Görselin ortalanma farkı (boşluk)
            double offsetX = (dispW - imgW * ratio) / 2;
            double offsetY = (dispH - imgH * ratio) / 2;

            // CropCanvas boyutu PreviewEdited ile aynıysa, burası doğru dönüşüm yapar:
            double scaleX = imgW / (dispW - 2 * offsetX);
            double scaleY = imgH / (dispH - 2 * offsetY);

            // Crop alanını piksel koordinatına dönüştür
            int cropX = (int)Math.Round((rectX - offsetX) * scaleX);
            int cropY = (int)Math.Round((rectY - offsetY) * scaleY);
            int cropW = (int)Math.Round(rectW * scaleX);
            int cropH = (int)Math.Round(rectH * scaleY);

            // Limitleri koru
            if (cropX < 0) cropX = 0;
            if (cropY < 0) cropY = 0;
            if (cropX + cropW > imgW) cropW = (int)(imgW - cropX);
            if (cropY + cropH > imgH) cropH = (int)(imgH - cropY);

            try
            {
                var cropRect = new SixLabors.ImageSharp.Rectangle(cropX, cropY, cropW, cropH);
                _currentImage.Mutate(x => x.Crop(cropRect));
                _originalImage = _currentImage.Clone();

                CropRect.Visibility = Visibility.Collapsed;
                ShowPreview();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Kırpma işlemi başarısız: {ex.Message}");
            }
        }



    }
}
