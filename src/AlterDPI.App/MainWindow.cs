using System.Diagnostics;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AlterDPI.Core;
using Microsoft.Win32;
using Forms = System.Windows.Forms;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;

namespace AlterDPI.App;

public sealed class MainWindow : Window
{
    private static SolidColorBrush Brush(string color) => (SolidColorBrush)new BrushConverter().ConvertFromString(color)!;
    private static readonly Brush Surface = Brush("#10151D"), Card = Brush("#1A222E"), Ink = Brush("#F0F4FA"), Muted = Brush("#9BAABF"), Accent = Brush("#66E3BA");
    private readonly StackPanel home = new(), settings = new(), dialog = new();
    private readonly TextBlock title = Text("Hazır", 17), description = Text("Erişim ve web koruması kapalı.", 12), errorText = Text("", 12), updateText = Text("Güncelleme henüz denetlenmedi.", 12);
    private readonly Border errorCard;
    private readonly Button access, protection, settingsButton, updateButton;
    private readonly CheckBox startup = new() { Content = "Windows ile başlat", Foreground = Ink, Margin = new(0, 0, 0, 14) };
    private readonly ProgressBar progress = new() { Height = 3, IsIndeterminate = true, Visibility = Visibility.Collapsed, Foreground = Accent, Background = Card, BorderThickness = new(0), Margin = new(0, 0, 0, 12) };
    private readonly DispatcherTimer poll = new() { Interval = TimeSpan.FromSeconds(3) };
    private readonly CancellationTokenSource lifetime = new();
    private readonly Forms.NotifyIcon? tray;
    private readonly EventWaitHandle? showEvent;
    private readonly RegisteredWaitHandle? showWait;
    private TaskCompletionSource<bool>? confirmation;
    private AppState state = new();
    private bool initialized, busy, exiting, polling, consent, updateAvailable, settingsOpen;
    private DateTimeOffset hiddenAt, nextInitialize;
    public MainWindow(bool preview = false, bool hidden = false)
    {
        Title = Product.Name; Width = 360; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        WindowStyle = WindowStyle.None; ShowInTaskbar = false; Topmost = true; AllowsTransparency = true;
        Background = Brushes.Transparent; Foreground = Ink; FontFamily = new("Segoe UI"); FontSize = 13;
        WindowStartupLocation = WindowStartupLocation.Manual; UseLayoutRounding = true;
        Icon = BitmapFrame.Create(Resource("alterdpi.ico"));
        Resources.Add(typeof(Button), XamlReader.Parse("""
            <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="Button">
              <Setter Property="FocusVisualStyle" Value="{x:Null}"/>
              <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Button">
                <Border x:Name="Frame" CornerRadius="11" Background="{TemplateBinding Background}" Padding="{TemplateBinding Padding}">
                  <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
                </Border>
                <ControlTemplate.Triggers>
                  <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Frame" Property="Opacity" Value="0.85"/></Trigger>
                  <Trigger Property="IsPressed" Value="True"><Setter TargetName="Frame" Property="Opacity" Value="0.65"/></Trigger>
                  <Trigger Property="IsEnabled" Value="False"><Setter TargetName="Frame" Property="Opacity" Value="0.4"/></Trigger>
                  <Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="Frame" Property="BorderBrush" Value="#66E3BA"/><Setter TargetName="Frame" Property="BorderThickness" Value="1"/></Trigger>
                </ControlTemplate.Triggers>
              </ControlTemplate></Setter.Value></Setter>
            </Style>
            """));
        Resources.Add(typeof(CheckBox), XamlReader.Parse("""
            <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="CheckBox">
              <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="CheckBox">
                <Grid Background="Transparent" Margin="0,12,0,0">
                  <Grid.ColumnDefinitions><ColumnDefinition/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions>
                  <ContentPresenter VerticalAlignment="Center"/>
                  <Border x:Name="Track" Grid.Column="1" Width="34" Height="20" Background="#354153" CornerRadius="10">
                    <Border x:Name="Knob" Width="14" Height="14" Margin="3" Background="#F0F4FA" CornerRadius="7" HorizontalAlignment="Left"/>
                  </Border>
                </Grid>
                <ControlTemplate.Triggers>
                  <Trigger Property="IsChecked" Value="True"><Setter TargetName="Track" Property="Background" Value="#398E75"/><Setter TargetName="Knob" Property="HorizontalAlignment" Value="Right"/></Trigger>
                  <Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="Track" Property="BorderBrush" Value="#66E3BA"/><Setter TargetName="Track" Property="BorderThickness" Value="1"/></Trigger>
                </ControlTemplate.Triggers>
              </ControlTemplate></Setter.Value></Setter>
            </Style>
            """));
        var content = new StackPanel { Margin = new(20) };
        var frame = new Border { Width = 360, CornerRadius = new(18), Background = Surface, BorderBrush = Brush("#303B4A"), BorderThickness = new(1), Child = content };
        Content = new Viewbox { Child = frame, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly };
        var header = new Grid { Margin = new(0, 0, 0, 18) };
        header.ColumnDefinitions.Add(new() { Width = new GridLength(48) }); header.ColumnDefinitions.Add(new()); header.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        header.Children.Add(new Image { Source = new BitmapImage(Resource("alterdpi.png")), Width = 38, Height = 38, Stretch = Stretch.Uniform });
        var brand = new StackPanel(); brand.Children.Add(Text("AlterDPI", 23, Ink, FontWeights.SemiBold)); brand.Children.Add(Text("Furkan Okan", 10)); Grid.SetColumn(brand, 1); header.Children.Add(brand);
        var hide = MakeButton("✕", () => { HidePanel(); return Task.CompletedTask; }); hide.Padding = new(10, 5, 10, 5); hide.VerticalAlignment = VerticalAlignment.Top; hide.ToolTip = "Tepsiye gizle"; Grid.SetColumn(hide, 2); header.Children.Add(hide); content.Children.Add(header);
        var status = new StackPanel(); status.Children.Add(title); description.Margin = new(0, 7, 0, 0); status.Children.Add(description);
        home.Children.Add(new Border { Child = status, Background = Card, CornerRadius = new(12), Padding = new(15), Margin = new(0, 0, 0, 14) });
        home.Children.Add(progress);
        access = MakeButton("Erişimi etkinleştir", () => Toggle("access", !state.Access), true); home.Children.Add(access);
        protection = MakeButton("Web korumasını etkinleştir", () => Toggle("protection", !state.Protection)); home.Children.Add(protection);
        var info = MakeButton("ⓘ  Web koruması hakkında", async () => await Explain("Web koruması", "Bilinen zararlı alan adlarını engellemeye yardımcı olur. Özel uyarı ekranı için tarayıcı eklentisi gerekir.\n\nBilinmeyen bütün tehditleri veya açık bağlantıları engellemez.", false)); info.FontSize = 11; info.Background = Surface; home.Children.Add(info); content.Children.Add(home);
        settings.Visibility = Visibility.Collapsed; settings.Children.Add(Text("Ayarlar", 18, Ink, FontWeights.SemiBold));
        startup.IsChecked = preview ? false : Startup.Enabled; startup.Click += (_, _) => { try { Startup.Set(startup.IsChecked == true); } catch (Exception e) { ShowProblem(Problems.From(e)); } }; settings.Children.Add(startup);
        updateText.Margin = new(0, 0, 0, 9); settings.Children.Add(updateText);
        updateButton = MakeButton("Güncellemeleri denetle", async () => { if (updateAvailable) await InstallUpdate(); else await CheckUpdates(); }); settings.Children.Add(updateButton);
        settings.Children.Add(MakeButton("Tarayıcı korumasını kur", () => { Open(Path.Combine(AppContext.BaseDirectory, "browser", "KURULUM.html")); return Task.CompletedTask; }));
        settings.Children.Add(MakeButton("Geçici izinleri kaldır", async () => await Request(new("clear-exceptions"))));
        settings.Children.Add(MakeButton("Hata kayıtlarını aç", () => { Open(Path.Combine(Product.StateRoot, "kayitlar")); return Task.CompletedTask; }));
        settings.Children.Add(MakeButton("Programı kaldır", async () => { if (await Explain("AlterDPI'ı kaldır", "Kurulum kaldırıcı açılacak. Uygulamaya ait ağ ayarları geri alınacak; kayıtları silmek isteğe bağlıdır.", true)) { Open(Path.Combine(Product.InstallRoot, "Kaldir.exe")); } })); content.Children.Add(settings);
        dialog.Visibility = Visibility.Collapsed; content.Children.Add(dialog);
        errorCard = new Border { Background = Brush("#30252A"), CornerRadius = new(10), Padding = new(12), Margin = new(0, 6, 0, 8), Visibility = Visibility.Collapsed, Child = errorText }; content.Children.Add(errorCard);
        errorText.Foreground = Brush("#FFC1AE"); errorCard.MouseLeftButtonUp += async (_, _) => { if (state.Problem is { } p) await Explain("İşlem ayrıntıları", p.Cause + "\n\n" + p.Effect + "\n\n" + p.Solution + (state.LogFile == null ? "" : "\n\nKayıt: " + Path.GetFileName(state.LogFile)), false); };
        var footer = new Grid { Margin = new(0, 10, 0, 0) }; footer.ColumnDefinitions.Add(new()); footer.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); footer.Children.Add(Text(Product.DisplayVersion, 11));
        settingsButton = MakeButton("Ayarlar", () => { SetPage(!settingsOpen); return Task.CompletedTask; }); settingsButton.Padding = new(12, 7, 12, 7); settingsButton.Margin = new(0); Grid.SetColumn(settingsButton, 1); footer.Children.Add(settingsButton); content.Children.Add(footer);
        SizeChanged += (_, _) => PositionPanel(); PreviewKeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) { HidePanel(); e.Handled = true; } };
        if (preview) { Update(new()); return; }
        tray = new Forms.NotifyIcon { Text = Product.Name, Icon = new System.Drawing.Icon(System.Windows.Application.GetResourceStream(Resource("alterdpi.ico"))!.Stream), Visible = true };
        var menu = new Forms.ContextMenuStrip { BackColor = System.Drawing.Color.FromArgb(26, 34, 46), ForeColor = System.Drawing.Color.WhiteSmoke, ShowImageMargin = false, Font = new System.Drawing.Font("Segoe UI", 10) };
        menu.Renderer = new Forms.ToolStripProfessionalRenderer(new TrayColors()) { RoundedEdges = true };
        var visibility = menu.Items.Add("Göster"); menu.Opening += (_, _) => visibility.Text = IsVisible || DateTimeOffset.UtcNow - hiddenAt < TimeSpan.FromMilliseconds(500) ? "Gizle" : "Göster";
        visibility.Click += (_, _) => { if (visibility.Text == "Gizle") HidePanel(); else ShowPanel(); };
        menu.Items.Add("Erişimi aç / kapat", null, async (_, _) => await Toggle("access", !state.Access));
        menu.Items.Add("Ayarlar", null, (_, _) => { SetPage(true); ShowPanel(); }); menu.Items.Add(new Forms.ToolStripSeparator()); menu.Items.Add("Çıkış", null, async (_, _) => await Exit()); tray.ContextMenuStrip = menu;
        tray.MouseUp += (_, e) => { if (e.Button == Forms.MouseButtons.Left) { if (IsVisible || DateTimeOffset.UtcNow - hiddenAt < TimeSpan.FromMilliseconds(500)) HidePanel(); else ShowPanel(); } };
        showEvent = new(false, EventResetMode.AutoReset, "Local\\AlterDPI.Show." + WindowsIdentity.GetCurrent().User?.Value); showWait = ThreadPool.RegisterWaitForSingleObject(showEvent, (_, _) => Dispatcher.BeginInvoke(ShowPanel), null, Timeout.Infinite, false);
        Deactivated += (_, _) => Dispatcher.BeginInvoke(() => { if (!IsActive && IsVisible) HidePanel(); }, DispatcherPriority.Background);
        Closing += (_, e) => { if (!exiting) { e.Cancel = true; HidePanel(); } };
        Loaded += async (_, _) => { PositionPanel(); if (hidden) HidePanel(); await Initialize(); };
        poll.Tick += async (_, _) => await Poll(); poll.Start(); Update(new() { Stage = "Hizmet hazırlanıyor", Level = "warning" });
    }
    private static Uri Resource(string file) => new("pack://application:,,,/AlterDPI;component/assets/" + file);
    private static TextBlock Text(string text, double size, Brush? brush = null, FontWeight? weight = null) => new() { Text = text, FontSize = size, Foreground = brush ?? Muted, FontWeight = weight ?? FontWeights.Normal, TextWrapping = TextWrapping.Wrap };
    private Button MakeButton(string text, Func<Task> action, bool primary = false)
    {
        var button = new Button { Content = text, Padding = new(12, 12, 12, 12), Margin = new(0, 0, 0, 9), Background = primary ? Accent : Card, Foreground = primary ? Surface : Ink, BorderThickness = new(0), Cursor = System.Windows.Input.Cursors.Hand, FontSize = 13 };
        button.Click += async (_, _) => { try { await action(); } catch (OperationCanceledException) { } catch (Exception e) { ShowProblem(Problems.From(e)); } }; return button;
    }
    private void PositionPanel()
    {
        var screen = Forms.Screen.FromPoint(Forms.Cursor.Position).WorkingArea; var transform = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var start = transform.Transform(new Point(screen.Left, screen.Top)); var end = transform.Transform(new Point(screen.Right, screen.Bottom));
        MaxHeight = Math.Max(200, end.Y - start.Y - 24); Left = Math.Max(start.X + 12, end.X - Width - 12); Top = Math.Max(start.Y + 12, end.Y - ActualHeight - 12);
    }
    private void SetPage(bool value) { settingsOpen = value; confirmation?.TrySetResult(false); confirmation = null; dialog.Visibility = Visibility.Collapsed; home.Visibility = value ? Visibility.Collapsed : Visibility.Visible; settings.Visibility = value ? Visibility.Visible : Visibility.Collapsed; settingsButton.Content = value ? "Geri" : "Ayarlar"; }
    private async Task<bool> Explain(string heading, string message, bool confirm)
    {
        confirmation?.TrySetResult(false); var reply = confirmation = new(TaskCreationOptions.RunContinuationsAsynchronously); home.Visibility = settings.Visibility = Visibility.Collapsed; dialog.Children.Clear(); dialog.Children.Add(Text(heading, 18, Ink, FontWeights.SemiBold)); var body = Text(message, 13); body.Margin = new(0, 14, 0, 18); dialog.Children.Add(body);
        dialog.Children.Add(MakeButton(confirm ? "Devam et" : "Anladım", () => { reply.TrySetResult(true); return Task.CompletedTask; }, true));
        if (confirm) dialog.Children.Add(MakeButton("Vazgeç", () => { reply.TrySetResult(false); return Task.CompletedTask; })); dialog.Visibility = Visibility.Visible; ShowPanel();
        var answer = await reply.Task; if (ReferenceEquals(confirmation, reply)) { confirmation = null; SetPage(settingsOpen); } return answer;
    }
    private async Task Initialize()
    {
        nextInitialize = DateTimeOffset.UtcNow.AddSeconds(20);
        try
        {
            using var service = new ServiceController(Product.Service); if (service.Status == ServiceControllerStatus.Stopped) { service.Start(); await Task.Run(() => service.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(10))); }
            var reply = await ControlClient.Send(new("state"), lifetime.Token); initialized = true; consent = reply.Data is JsonElement data && data.TryGetProperty("consent", out var value) && value.GetBoolean(); Update(reply.State!); await Request(new("resume")); _ = CheckUpdates();
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { initialized = false; title.Text = "Hizmete ulaşılamıyor"; description.Text = "Kurulumu onarın. Ayrıntılar hata kayıtlarında."; ShowProblem(Problems.From(e)); }
    }
    private async Task Poll()
    {
        if (polling || exiting) return; polling = true;
        try { if (!initialized) { if (DateTimeOffset.UtcNow >= nextInitialize) await Initialize(); return; } var reply = await ControlClient.Send(new("state"), lifetime.Token); if (reply.State != null) Update(reply.State); }
        catch (OperationCanceledException) { }
        catch (Exception e) { initialized = false; ShowProblem(Problems.From(e)); }
        finally { polling = false; }
    }
    private async Task Toggle(string feature, bool enabled)
    {
        if (busy || exiting) return; if (!initialized) { await Initialize(); if (!initialized) return; }
        if (enabled && !consent) { if (!await Explain("Ağ ayarları hakkında", "Bağlantı türü, DNS ve rota bilgisi yalnız bu bilgisayarda okunacak. Gerekli DNS kuralları kapatıldığında geri alınır. Kişisel ağ dökümü gönderilmez.\n\nDevam edilsin mi?", true)) return; var approved = await Request(new("consent", true)); if (!approved.Ok) return; consent = true; }
        ShowPanel(); await Request(new(feature, enabled));
    }
    private async Task<ControlReply> Request(ControlRequest request)
    {
        busy = true; progress.Visibility = Visibility.Visible; access.IsEnabled = protection.IsEnabled = false;
        try { var reply = await ControlClient.Send(request, lifetime.Token); if (reply.State != null) Update(reply.State); if (!reply.Ok && reply.Problem != null) ShowProblem(reply.Problem); return reply; }
        finally { busy = false; progress.Visibility = Visibility.Collapsed; access.IsEnabled = protection.IsEnabled = true; }
    }
    private void Update(AppState current)
    {
        state = current; title.Text = current.Stage; title.Foreground = current.Level switch { "success" => Accent, "warning" => Brush("#F1CA83"), "error" => Brush("#FFB3A2"), _ => Ink };
        description.Text = $"Erişim: {(current.Access ? "Etkin" : "Kapalı")}  ·  Koruma: {(current.Protection ? "Etkin" : "Kapalı")}" + (current.LatencyMs is double ms ? $"\nKontrol gecikmesi: {ms:F0} ms" : "") + (current.ListStale && current.Protection ? "\nKoruma listesi güncellenmeyi bekliyor." : "");
        access.Content = current.Access ? "Erişimi kapat" : "Erişimi etkinleştir"; protection.Content = current.Protection ? "Web korumasını kapat" : "Web korumasını etkinleştir";
        errorCard.Visibility = current.Problem == null ? Visibility.Collapsed : Visibility.Visible; if (current.Problem != null) ShowProblem(current.Problem);
        if (tray != null) tray.Text = "AlterDPI · " + (current.Access ? "Erişim etkin" : "Erişim kapalı");
    }
    private void ShowProblem(Problem problem) { state = state with { Problem = problem }; errorText.Text = problem.Cause + "\nAyrıntılar için dokunun."; errorCard.Visibility = Visibility.Visible; }
    private sealed class TrayColors : Forms.ProfessionalColorTable
    {
        public override System.Drawing.Color ToolStripDropDownBackground => System.Drawing.Color.FromArgb(26, 34, 46);
        public override System.Drawing.Color MenuBorder => System.Drawing.Color.FromArgb(48, 59, 74);
        public override System.Drawing.Color MenuItemSelected => System.Drawing.Color.FromArgb(47, 69, 68);
        public override System.Drawing.Color MenuItemBorder => MenuItemSelected;
    }
    private async Task CheckUpdates()
    {
        if (!initialized) return;
        try { updateText.Text = "Denetleniyor…"; var reply = await ControlClient.Send(new("check-update"), lifetime.Token); if (!reply.Ok) { updateText.Text = "Güncelleme şu anda denetlenemedi."; return; } var data = (JsonElement)reply.Data!; updateAvailable = data.GetProperty("status").GetString() == "available"; updateText.Text = updateAvailable ? "Yeni sürüm: " + data.GetProperty("version").GetString() : "Yeni yayımlanmış sürüm yok."; updateButton.Content = updateAvailable ? "Güncellemeyi indir ve kur" : "Güncellemeleri denetle"; }
        catch (Exception) { updateText.Text = "Güncelleme denetlenemedi; mevcut sürüm korunuyor."; }
    }
    private async Task InstallUpdate()
    {
        if (!await Explain("Güncelleme", "Doğrulanmış yeni sürüm indirilip kurulsun mu?", true)) return;
        var reply = await Request(new("download-update")); if (!reply.Ok) return; var data = (JsonElement)reply.Data!; var path = data.GetProperty("installer").GetString()!; var prefix = Path.Combine(Product.InstallRoot, "updates") + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(path).StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("INTEGRITY"); Open(path); CloseUi();
    }
    private void ShowPanel() { PositionPanel(); Show(); Activate(); if (SystemParameters.ClientAreaAnimation) BeginAnimation(OpacityProperty, new DoubleAnimation(0.75, 1, TimeSpan.FromMilliseconds(120))); poll.Interval = TimeSpan.FromSeconds(3); }
    private void HidePanel() { hiddenAt = DateTimeOffset.UtcNow; confirmation?.TrySetResult(false); Hide(); poll.Interval = TimeSpan.FromSeconds(10); }
    private static void Open(string target) { if (Directory.Exists(target) || File.Exists(target)) Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
    private void CloseUi() { exiting = true; lifetime.Cancel(); poll.Stop(); showWait?.Unregister(null); showEvent?.Dispose(); tray?.Dispose(); Close(); System.Windows.Application.Current.Shutdown(); }
    private async Task Exit()
    {
        if (exiting) return; exiting = true; HidePanel();
        try { using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8)); var reply = await ControlClient.Send(new("stop"), timeout.Token); if (reply.Ok) { using var service = new ServiceController(Product.Service); service.Stop(); } else throw new IOException("SERVICE"); }
        catch (Exception e) { var problem = Problems.From(e); new Journal(new Store(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Product.Name))).Log(problem, "Arayüz kapatma", e); tray?.ShowBalloonTip(3000, Product.Name, "Pencere kapandı. Ağ geri yüklemesi tamamlanamadı; kayıt tutuldu.", Forms.ToolTipIcon.Warning); }
        finally { CloseUi(); }
    }
}
internal static class Startup
{
    private const string Run = @"Software\Microsoft\Windows\CurrentVersion\Run", Approved = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    public static bool Enabled { get { using var run = Registry.CurrentUser.OpenSubKey(Run); using var approved = Registry.CurrentUser.OpenSubKey(Approved); var bytes = approved?.GetValue(Product.Name) as byte[]; return run?.GetValue(Product.Name) != null && !(bytes is { Length: > 0 } && bytes[0] is 3 or 7); } }
    public static void Set(bool enabled) { using var key = Registry.CurrentUser.CreateSubKey(Run); if (enabled) { key.SetValue(Product.Name, '"' + Path.Combine(Product.InstallRoot, "AlterDPI.exe") + "\" --startup"); using var approved = Registry.CurrentUser.OpenSubKey(Approved, true); approved?.DeleteValue(Product.Name, false); } else key.DeleteValue(Product.Name, false); }
}
