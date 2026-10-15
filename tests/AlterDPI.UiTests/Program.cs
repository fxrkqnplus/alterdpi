using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AlterDPI.App;
using AlterDPI.Core;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var application = new Application(); var window = new MainWindow(preview: true); var passed = 0;
        void Check(bool ok) { if (!ok) throw new InvalidOperationException("UI regression"); passed++; }
        Check(!window.ShowInTaskbar && window.WindowStyle == WindowStyle.None && window.ResizeMode == ResizeMode.NoResize);
        var root = (FrameworkElement)window.Content;
        void Layout() { root.Measure(new Size(360, double.PositiveInfinity)); root.Arrange(new Rect(0, 0, 360, root.DesiredSize.Height)); root.UpdateLayout(); Check(root.DesiredSize.Height is > 200 and < 600); }
        Layout(); Check(!Descendants(root).OfType<ScrollViewer>().Any());
        if (args.Length > 0) Render(root, args[0]);
        var update = typeof(MainWindow).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic)!;
        update.Invoke(window, [new AppState { Access = true, Stage = "Bağlantı hazır", Level = "success", LatencyMs = 24 }]); Layout();
        update.Invoke(window, [new AppState { Stage = "Kontrol gerekiyor", Level = "error", Problem = new("TEST", "Ağ ayarları geri yüklenemedi.", "Önceki ayarlar korunuyor.", "Kaydı kontrol edin.") }]); Layout();
        typeof(MainWindow).GetMethod("SetPage", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [true]); Layout(); Check(!Descendants(root).OfType<ScrollViewer>().Any());
        root.Measure(new Size(360, 420)); root.Arrange(new Rect(0, 0, 360, 420)); root.UpdateLayout(); Check(root.RenderSize.Height <= 420);
        window.Close(); application.Shutdown(); Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { passed, failed = 0 })); return 0;
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { var child = VisualTreeHelper.GetChild(root, i); yield return child; foreach (var nested in Descendants(child)) yield return nested; }
    }
    private static void Render(FrameworkElement root, string output)
    {
        var image = new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth), (int)Math.Ceiling(root.ActualHeight), 96, 96, PixelFormats.Pbgra32); image.Render(root);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(image)); using var file = System.IO.File.Create(output); png.Save(file);
    }
}
