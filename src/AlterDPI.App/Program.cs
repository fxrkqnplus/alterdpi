using System.Diagnostics;
using System.Security.Principal;
using System.ServiceProcess;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AlterDPI.Core;

namespace AlterDPI.App;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            if (args.Contains("--configure")) { Install.Configure().GetAwaiter().GetResult(); return 0; }
            if (args.Contains("--restore")) { Install.Configure(true).GetAwaiter().GetResult(); return 0; }
            if (args.Contains("--unconfigure")) { Install.Unconfigure().GetAwaiter().GetResult(); return 0; }
            if (args.Contains("--prepare-upgrade")) { Install.Unconfigure(true).GetAwaiter().GetResult(); return 0; }
            if (args.Contains("--delete-logs")) { Install.DeleteLogs(); return 0; }
            if (args.Contains("--keep-logs")) { Install.KeepOnlyLogs(); return 0; }
            if (args.Contains("--validate-backup")) { Install.ValidateBackup(); return 0; }
            if (args.Contains("--service"))
            {
                if (WindowsIdentity.GetCurrent().User?.Value != "S-1-5-18") return 5;
                ServiceBase.Run(new NetworkService()); return 0;
            }
            if (args.Contains("--recover"))
            {
                if (!new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator)) return 5;
                Native.Policy("Remove", CancellationToken.None).GetAwaiter().GetResult(); return 0;
            }
            if (args.Contains("--native") || string.Equals(Path.GetFileName(Environment.ProcessPath), "AlterDPI.Native.exe", StringComparison.OrdinalIgnoreCase)) return NativeHost.Run();
            var application = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            if (args.Length == 2 && args[0] == "--render-preview")
            {
                var window = new MainWindow(true); var content = (FrameworkElement)window.Content; window.Content = null; var surface = new System.Windows.Controls.Border { Background = new SolidColorBrush(Color.FromRgb(15, 19, 25)), Child = content }; surface.Measure(new System.Windows.Size(420, 500)); surface.Arrange(new Rect(0, 0, 420, 500)); surface.UpdateLayout();
                var image = new RenderTargetBitmap(420, 500, 96, 96, PixelFormats.Pbgra32); image.Render(surface); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); using var output = File.Create(args[1]); encoder.Save(output); return 0;
            }
            if (new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator)) { System.Windows.MessageBox.Show("Arayüzü Başlat menüsünden normal kullanıcı olarak açın. Yönetici izni yalnız kurulumda gereklidir.", Product.Name); return 5; }
            using var single = new Mutex(true, "Local\\AlterDPI.UI." + WindowsIdentity.GetCurrent().User?.Value, out var created);
            if (!created) { using var evt = EventWaitHandle.OpenExisting("Local\\AlterDPI.Show." + WindowsIdentity.GetCurrent().User?.Value); evt.Set(); return 0; }
            var main = new MainWindow(false, args.Contains("--startup")); application.Run(main); return 0;
        }
        catch (Exception e)
        {
            var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Product.Name); var p = Problems.From(e); var file = new Journal(new Store(root)).Log(p, "Başlatma", e);
            var maintenance = args.Any(x => x is "--configure" or "--restore" or "--unconfigure" or "--prepare-upgrade" or "--delete-logs" or "--keep-logs" or "--validate-backup");
            if (!maintenance && !args.Contains("--render-preview")) System.Windows.MessageBox.Show(p.Cause + "\n\n" + p.Solution + "\n\nKayıt: " + file, Product.Name, MessageBoxButton.OK, MessageBoxImage.Warning); return 1;
        }
    }
}
public sealed class NetworkService : ServiceBase
{
    private readonly CancellationTokenSource lifetime = new();
    private Controller? controller;
    public NetworkService() { ServiceName = Product.Service; CanStop = true; CanShutdown = true; AutoLog = true; }
    protected override void OnStart(string[] args)
    {
        _ = Task.Run(async () =>
        {
            try { controller = new Controller(); await controller.Initialize(lifetime.Token); await new PipeServer(controller).Run(lifetime.Token); }
            catch (OperationCanceledException) { }
            catch (Exception e) { new Journal(new Store(Product.StateRoot)).Log(Problems.From(e), "Hizmet", e); ExitCode = 1067; _ = Task.Run(Stop); }
        });
    }
    protected override void OnStop()
    {
        try { RequestAdditionalTime(60000); } catch (InvalidOperationException) { }
        lifetime.Cancel(); var current = Interlocked.Exchange(ref controller, null); if (current == null) return;
        try { current.Shutdown().GetAwaiter().GetResult(); }
        catch (Exception e) { ExitCode = 1; new Journal(new Store(Product.StateRoot)).Log(Problems.From(e), "Hizmet kapatma", e); }
        finally { current.Dispose(); }
    }
    protected override void OnShutdown() => OnStop();
    protected override void Dispose(bool disposing) { if (disposing) lifetime.Dispose(); base.Dispose(disposing); }
}
