using System.Diagnostics;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AlterDPI.Core;
using Microsoft.Win32;
using Forms = System.Windows.Forms;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using MessageBox = System.Windows.MessageBox;

namespace AlterDPI.App;

public sealed class MainWindow : Window
{
    private static readonly SolidColorBrush BackgroundBrush = new(Color.FromRgb(15, 19, 25)), CardBrush = new(Color.FromRgb(25, 31, 40)), TextBrush = new(Color.FromRgb(236, 241, 246)), MutedBrush = new(Color.FromRgb(157, 169, 184));
    private readonly TextBlock stage = new(), detail = new(), version = new(), updateText = new(), error = new();
    private readonly Border status = new();
    private readonly Button access, protect, settingsButton, updateButton;
    private readonly CheckBox startup = new() { Content = "Windows açıldığında başlat", Foreground = TextBrush, Margin = new(0, 8, 0, 8) };
    private readonly StackPanel mainPanel = new(), settingsPanel = new();
    private readonly ProgressBar progress = new() { Height = 3, IsIndeterminate = true, Visibility = Visibility.Collapsed, Margin = new(0, 0, 0, 14) };
    private readonly DispatcherTimer poll = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly CancellationTokenSource lifetime = new();
    private readonly Forms.NotifyIcon? tray;
    private readonly EventWaitHandle? showEvent;
    private readonly RegisteredWaitHandle? showWait;
    private AppState state = new();
    private bool initialized, busy, exiting, polling, consent, updateAvailable;
    private readonly bool preview;
    public MainWindow(bool preview = false, bool hidden = false)
    {
        this.preview = preview; Title = Product.Name; Width = 420; Height = 500; MinWidth = 380; MinHeight = 450; ResizeMode = ResizeMode.CanMinimize; Background = BackgroundBrush; Foreground = TextBrush; FontFamily = new("Segoe UI"); FontSize = 14; WindowStartupLocation = WindowStartupLocation.Manual;
        var area = SystemParameters.WorkArea; Left = Math.Max(area.Left, area.Right - Width - 18); Top = Math.Max(area.Top, area.Bottom - Height - 18);
        Icon = BitmapFrame.Create(new Uri("pack://application:,,,/assets/alterdpi.ico"));
        var root = new Grid { Margin = new(24) }; root.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) }); root.RowDefinitions.Add(new() { Height = GridLength.Auto }); Content = root;
        var content = new StackPanel(); var scroll = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; root.Children.Add(scroll);
        var header = new Grid { Margin = new(0, 0, 0, 20) }; header.ColumnDefinitions.Add(new() { Width = new GridLength(66) }); header.ColumnDefinitions.Add(new());
        var logo = new Image { Source = new BitmapImage(new Uri("pack://application:,,,/assets/alterdpi.png")), Width = 56, Height = 56, Stretch = Stretch.Uniform }; header.Children.Add(logo);
        var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center }; names.Children.Add(Label("AlterDPI", 27, TextBrush, FontWeights.SemiBold)); names.Children.Add(Label("Furkan Okan", 12, MutedBrush)); Grid.SetColumn(names, 1); header.Children.Add(names); content.Children.Add(header);
        stage.FontSize = 17; stage.FontWeight = FontWeights.SemiBold; stage.TextWrapping = TextWrapping.Wrap; detail.Foreground = MutedBrush; detail.Margin = new(0, 8, 0, 0); detail.TextWrapping = TextWrapping.Wrap;
        var texts = new StackPanel(); texts.Children.Add(stage); texts.Children.Add(detail); status.Child = texts; status.Background = CardBrush; status.CornerRadius = new(14); status.Padding = new(18); status.Margin = new(0, 0, 0, 20); content.Children.Add(status); content.Children.Add(progress);
        access = MakeButton("Erişimi etkinleştir", async () => await Toggle("access", !state.Access), true); protect = MakeButton("Web korumasını etkinleştir", async () => await Toggle("protection", !state.Protection));
        mainPanel.Children.Add(access); mainPanel.Children.Add(protect); var info = MakeButton("ⓘ  Web koruması hakkında", () => { MessageBox.Show("Bilinen zararlı ve güvenilmez alan adlarını engellemeye yardımcı olur. Özel uyarı ekranı için tarayıcı eklentisi gerekir. Bilinmeyen tehditleri veya bütün saldırıları önleme garantisi vermez.\n\nErişim ve koruma birbirinden bağımsız açılabilir.", "Web koruması", MessageBoxButton.OK, MessageBoxImage.Information); return Task.CompletedTask; }); mainPanel.Children.Add(info); content.Children.Add(mainPanel);
        settingsPanel.Visibility = Visibility.Collapsed; settingsPanel.Children.Add(Label("Ayarlar", 18, TextBrush, FontWeights.SemiBold)); startup.IsChecked = Startup.Enabled; startup.Click += (_, _) => { try { Startup.Set(startup.IsChecked == true); } catch (Exception e) { ShowProblem(Problems.From(e)); } }; settingsPanel.Children.Add(startup);
        updateText.Text = "Güncelleme denetleniyor…"; updateText.Foreground = MutedBrush; updateText.TextWrapping = TextWrapping.Wrap; updateText.Margin = new(0, 10, 0, 4); settingsPanel.Children.Add(updateText);
        updateButton = MakeButton("Güncelleştirmeleri denetle", async () => { if (updateAvailable) await InstallUpdate(); else await CheckUpdates(); }); settingsPanel.Children.Add(updateButton);
        settingsPanel.Children.Add(MakeButton("Tarayıcı korumasını kur", () => { var guide = Path.Combine(AppContext.BaseDirectory, "browser", "KURULUM.html"); if (File.Exists(guide)) Open(guide); else Open("https://github.com/" + Product.Repository + "#web-koruması"); return Task.CompletedTask; }));
        settingsPanel.Children.Add(MakeButton("Geçici izinleri kaldır", async () => await Request(new("clear-exceptions")))); settingsPanel.Children.Add(MakeButton("Hata kayıtlarını aç", () => { Open(Path.Combine(Product.StateRoot, "kayitlar")); return Task.CompletedTask; })); settingsPanel.Children.Add(MakeButton("Programı kaldır", () => { var uninstaller = Path.Combine(Product.InstallRoot, "Kaldir.exe"); if (File.Exists(uninstaller)) Process.Start(new ProcessStartInfo(uninstaller) { UseShellExecute = true, Verb = "runas" }); return Task.CompletedTask; })); content.Children.Add(settingsPanel);
        error.Foreground = new SolidColorBrush(Color.FromRgb(250, 163, 152)); error.TextWrapping = TextWrapping.Wrap; error.FontSize = 12; error.Margin = new(0, 14, 0, 0); content.Children.Add(error);
        var bottom = new Grid { Margin = new(0, 15, 0, 0) }; bottom.ColumnDefinitions.Add(new()); bottom.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); version.Text = Product.DisplayVersion; version.Foreground = MutedBrush; version.VerticalAlignment = VerticalAlignment.Center; bottom.Children.Add(version);
        settingsButton = MakeButton("Ayarlar", () => { var showing = settingsPanel.Visibility == Visibility.Visible; settingsPanel.Visibility = showing ? Visibility.Collapsed : Visibility.Visible; mainPanel.Visibility = showing ? Visibility.Visible : Visibility.Collapsed; settingsButton!.Content = showing ? "Ayarlar" : "Geri"; return Task.CompletedTask; }); settingsButton.Margin = new(0); Grid.SetColumn(settingsButton, 1); bottom.Children.Add(settingsButton); Grid.SetRow(bottom, 1); root.Children.Add(bottom);
        if (preview) { Update(new() { Stage = "Hazır", Level = "idle" }); return; }
        tray = new Forms.NotifyIcon { Text = Product.Name, Icon = new System.Drawing.Icon(System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/assets/alterdpi.ico"))!.Stream), Visible = true };
        var menu = new Forms.ContextMenuStrip(); menu.Items.Add("Göster", null, (_, _) => ShowPanel()); menu.Items.Add("Erişimi etkinleştir / pasifleştir", null, async (_, _) => await Toggle("access", !state.Access)); menu.Items.Add("Ayarlar", null, (_, _) => { ShowPanel(); settingsPanel.Visibility = Visibility.Visible; mainPanel.Visibility = Visibility.Collapsed; settingsButton.Content = "Geri"; }); menu.Items.Add(new Forms.ToolStripSeparator()); menu.Items.Add("Çıkış", null, async (_, _) => await Exit()); tray.ContextMenuStrip = menu; tray.DoubleClick += (_, _) => ShowPanel();
        showEvent = new(false, EventResetMode.AutoReset, "Local\\AlterDPI.Show." + WindowsIdentity.GetCurrent().User?.Value); showWait = ThreadPool.RegisterWaitForSingleObject(showEvent, (_, _) => Dispatcher.Invoke(ShowPanel), null, Timeout.Infinite, false);
        Closing += (_, e) => { if (!exiting) { e.Cancel = true; Hide(); } }; Loaded += async (_, _) => { if (hidden) Hide(); await Initialize(); }; poll.Tick += async (_, _) => await Poll(); poll.Start(); Update(new() { Stage = "Hizmet hazırlanıyor", Level = "warning" });
    }
    private static TextBlock Label(string text, double size, Brush color, FontWeight? weight = null) => new() { Text = text, FontSize = size, Foreground = color, FontWeight = weight ?? FontWeights.Normal, TextWrapping = TextWrapping.Wrap };
    private Button MakeButton(string text, Func<Task> action, bool primary = false)
    {
        var b = new Button { Content = text, FontSize = 14, Padding = new(12, 11, 12, 11), Margin = new(0, 0, 0, 10), Background = primary ? new SolidColorBrush(Color.FromRgb(78, 212, 161)) : CardBrush, Foreground = primary ? BackgroundBrush : TextBrush, BorderThickness = new(0), HorizontalContentAlignment = HorizontalAlignment.Center }; b.Click += async (_, _) => { try { await action(); } catch (Exception e) { ShowProblem(Problems.From(e)); } }; return b;
    }
    private async Task Initialize()
    {
        try
        {
            using var service = new ServiceController(Product.Service); if (service.Status == ServiceControllerStatus.Stopped) { service.Start(); await Task.Run(() => service.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(10))); }
            for (int i = 0; i < 8; i++) { try { var reply = await ControlClient.Send(new("state"), lifetime.Token); initialized = true; consent = reply.Data is JsonElement j && j.TryGetProperty("consent", out var c) && c.GetBoolean(); Update(reply.State!); break; } catch (Exception e) when (e is IOException or TimeoutException) { await Task.Delay(500, lifetime.Token); } }
            if (initialized) { await Request(new("resume")); _ = CheckUpdates(); } else throw new IOException("SERVICE");
        }
        catch (Exception) { stage.Text = "Kurulum gerekiyor"; detail.Text = "Kurulum paketini çalıştırıp Windows izin penceresini onaylayın."; stage.Foreground = new SolidColorBrush(Color.FromRgb(249, 204, 116)); }
    }
    private async Task Poll() { if (!initialized || polling) return; polling = true; try { var r = await ControlClient.Send(new("state"), lifetime.Token); if (r.State != null) Update(r.State); startup.IsChecked = Startup.Enabled; if (!busy && r.State?.Stage == "Hazır" && r.Data is JsonElement p && (p.GetProperty("access").GetBoolean() || p.GetProperty("protection").GetBoolean())) await Request(new("resume")); } catch (Exception e) when (e is IOException or TimeoutException or UnauthorizedAccessException) { stage.Text = "Hizmet bağlantısı bekleniyor"; stage.Foreground = new SolidColorBrush(Color.FromRgb(249, 204, 116)); } catch (OperationCanceledException) { } finally { polling = false; } }
    private async Task Toggle(string feature, bool enabled)
    {
        if (busy) return;
        if (!initialized) { MessageBox.Show("Önce AlterDPI kurulumunu tamamlayın.", Product.Name); return; }
        if (enabled && !consent) { var answer = MessageBox.Show("Bağlantı türü, DNS, rota, protokol ve mevcut ağ ayarları bu bilgisayarda okunacak. Ağ değişirse yerelde yeniden değerlendirilir. Kişisel ağ dökümü gönderilmez. Gerekli DNS kuralları kapatıldığında geri alınır.\n\nDevam etmek istiyor musunuz?", "Tek seferlik ağ bilgilendirmesi", MessageBoxButton.YesNo, MessageBoxImage.Information, MessageBoxResult.No); if (answer != MessageBoxResult.Yes) return; await Request(new("consent", true)); consent = true; }
        ShowPanel(); await Request(new(feature, enabled));
    }
    private async Task<ControlReply> Request(ControlRequest request)
    {
        busy = true; progress.Visibility = Visibility.Visible; access.IsEnabled = protect.IsEnabled = false;
        try { var reply = await ControlClient.Send(request, lifetime.Token); if (reply.State != null) Update(reply.State); if (!reply.Ok && reply.Problem != null) ShowProblem(reply.Problem); return reply; }
        finally { busy = false; progress.Visibility = Visibility.Collapsed; access.IsEnabled = protect.IsEnabled = true; }
    }
    private void Update(AppState current)
    {
        state = current; stage.Text = current.Stage; stage.Foreground = new SolidColorBrush((current.Protection && current.ListStale && current.Level == "success" ? "warning" : current.Level) switch { "success" => Color.FromRgb(94, 230, 176), "error" => Color.FromRgb(255, 142, 130), "warning" => Color.FromRgb(249, 204, 116), _ => Color.FromRgb(236, 241, 246) });
        detail.Text = $"Erişim: {(current.Access ? "Etkin" : "Kapalı")}\nWeb koruması: {current.ProtectionMode}" + (current.Protection && current.ListStale ? "\nKoruma listesi eski; son doğrulanmış liste kullanılıyor." : "") + (current.LatencyMs is double t ? $"\nBağlantı kontrolü: {t:F0} ms" : "");
        access.Content = current.Access ? "Erişimi pasifleştir" : "Erişimi etkinleştir"; protect.Content = current.Protection ? "Web korumasını kapat" : "Web korumasını etkinleştir";
        error.Text = current.Problem == null ? "" : $"{current.Problem.Cause}\n{current.Problem.Effect}\n{current.Problem.Solution}" + (current.LogFile != null ? "\nKayıt: " + Path.GetFileName(current.LogFile) : ""); if (tray != null) tray.Text = "AlterDPI — " + (current.Access ? "Erişim etkin" : "Erişim kapalı");
    }
    private void ShowProblem(Problem p) { error.Text = p.Cause + "\n" + p.Solution; }
    private async Task CheckUpdates()
    {
        if (!initialized) return;
        try
        {
            updateText.Text = "Denetleniyor…"; var reply = await ControlClient.Send(new("check-update"), lifetime.Token); if (!reply.Ok) throw new IOException("UPDATE"); var data = (JsonElement)reply.Data!; var available = data.GetProperty("status").GetString() == "available"; updateText.Text = available ? "Yeni sürüm: " + data.GetProperty("version").GetString() : "Yeni yayımlanmış sürüm bulunamadı.";
            updateButton.Content = available ? "Yeni sürümü indir ve kur" : "Güncelleştirmeleri denetle";
            updateAvailable = available;
        }
        catch (Exception) { updateText.Text = "Güncelleme denetlenemedi. Mevcut sürümü kullanabilirsiniz."; }
    }
    private async Task InstallUpdate()
    {
        if (MessageBox.Show("Doğrulanmış yeni sürümü indirip kurmak istiyor musunuz?", "Güncelleme", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        try { var reply = await Request(new("download-update")); if (!reply.Ok) return; var data = (JsonElement)reply.Data!; var path = data.GetProperty("installer").GetString()!; var prefix = Path.Combine(Product.InstallRoot, "updates") + Path.DirectorySeparatorChar; if (!Path.GetFullPath(path).StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("INTEGRITY"); Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, Verb = "runas" }); CloseUi(); } catch (Exception ex) { ShowProblem(Problems.From(ex)); }
    }
    private void ShowPanel() { Show(); WindowState = WindowState.Normal; Activate(); }
    private static void Open(string target) { if (Directory.Exists(target) || File.Exists(target) || target.StartsWith("https://github.com/" + Product.Repository, StringComparison.Ordinal)) Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
    private void CloseUi() { exiting = true; lifetime.Cancel(); poll.Stop(); showWait?.Unregister(null); showEvent?.Dispose(); tray?.Dispose(); Close(); System.Windows.Application.Current.Shutdown(); }
    private async Task Exit() { if (exiting) return; try { if (initialized) { var reply = await Request(new("stop")); if (!reply.Ok) return; using var service = new ServiceController(Product.Service); service.Stop(); } CloseUi(); } catch (Exception e) { ShowProblem(Problems.From(e)); } }
}
internal static class Startup
{
    private const string Run = @"Software\Microsoft\Windows\CurrentVersion\Run", Approved = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    public static bool Enabled { get { using var run = Registry.CurrentUser.OpenSubKey(Run); using var approved = Registry.CurrentUser.OpenSubKey(Approved); var bytes = approved?.GetValue(Product.Name) as byte[]; return run?.GetValue(Product.Name) != null && !(bytes is { Length: > 0 } && bytes[0] is 3 or 7); } }
    public static void Set(bool enabled) { using var key = Registry.CurrentUser.CreateSubKey(Run); if (enabled) { key.SetValue(Product.Name, '"' + Path.Combine(Product.InstallRoot, "AlterDPI.exe") + "\" --startup"); using var approved = Registry.CurrentUser.OpenSubKey(Approved, true); approved?.DeleteValue(Product.Name, false); } else key.DeleteValue(Product.Name, false); }
}
