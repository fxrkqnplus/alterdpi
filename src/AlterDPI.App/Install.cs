using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text.Json;
using AlterDPI.Core;
using Microsoft.Win32;

namespace AlterDPI.App;

public static class Install
{
    private const string Marker = "AlterDPI.Managed.34B7F47A-2168-4DD3-9211-506FA4496890";
    private static readonly string[] Vendors = [@"Software\Google\Chrome", @"Software\Microsoft\Edge", @"Software\Mozilla"];
    private static string ServiceImage => '"' + Path.Combine(Product.InstallRoot, "AlterDPI.exe") + "\" --service";
    public static async Task Configure(bool rollback = false)
    {
        RequireAdmin(); RequireRoot(); var manifest = Trust.VerifyInstalled(Product.InstallRoot); Trust.PlainTree(Product.InstallRoot); RequireService();
        if (!rollback && manifest.Expires < DateTimeOffset.UtcNow) throw new InvalidDataException("INTEGRITY");
        var existing = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\AlterDPI", "OwnerSid", null) as string;
        var owner = existing ?? ExplorerUser(); _ = new SecurityIdentifier(owner);
        SetTreeAcl(Product.InstallRoot, "S-1-5-32-545");
        if (Directory.Exists(Product.StateRoot)) RequireOwned(Product.StateRoot, "state.owner");
        else SafeDirectory.CreateNew(Product.StateRoot, RootAcl(owner));
        SetTreeAcl(Product.StateRoot, owner); File.WriteAllText(Path.Combine(Product.StateRoot, "state.owner"), Marker);
        if (!rollback && manifest.Build < new Store(Product.StateRoot).Read<long>("app-build.json")) throw new InvalidDataException("INTEGRITY");
        using (var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\AlterDPI")) { key.SetValue("OwnerSid", owner); key.SetValue("InstallPath", Product.InstallRoot); }
        File.WriteAllText(Path.Combine(Product.InstallRoot, "install.owner"), Marker);
        foreach (var file in Directory.GetFiles(Product.InstallRoot, "*.ps1")) try { File.Delete(file + ":Zone.Identifier"); } catch (IOException) { }
        await Sc(["create", Product.Service, "binPath=", ServiceImage, "start=", "auto", "DisplayName=", "AlterDPI Arka Plan Hizmeti"]);
        await Sc(["sdset", Product.Service, $"D:(A;;CCLCSWRPWPDTLOCRRC;;;SY)(A;;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;BA)(A;;CCLCSWRPWPLOCRRC;;;{owner})"]);
        await Sc(["failure", Product.Service, "reset=", "86400", "actions=", "restart/1000/restart/5000/restart/10000"]); await Sc(["failureflag", Product.Service, "1"]);
        var hostDir = Path.Combine(Product.InstallRoot, "browser"); var browser = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(Path.Combine(hostDir, "browser.json")), Product.Json);
        var id = browser.GetProperty("extensionId").GetString(); var nativePath = Path.Combine(Product.InstallRoot, "AlterDPI.Native.exe");
        File.WriteAllText(Path.Combine(hostDir, "chromium-host.json"), JsonSerializer.Serialize(new { name = "com.alterdpi.bridge", description = "AlterDPI", path = nativePath, type = "stdio", allowed_origins = new[] { "chrome-extension://" + id + "/" } }, Product.Json));
        File.WriteAllText(Path.Combine(hostDir, "firefox-host.json"), JsonSerializer.Serialize(new { name = "com.alterdpi.bridge", description = "AlterDPI", path = nativePath, type = "stdio", allowed_extensions = new[] { "alterdpi@fxrkqnplus" } }, Product.Json));
        using var hive = Registry.Users.OpenSubKey(owner, true) ?? throw new InvalidOperationException("OWNER");
        foreach (var vendor in Vendors)
        {
            var path = Path.Combine(hostDir, vendor.Contains("Mozilla", StringComparison.Ordinal) ? "firefox-host.json" : "chromium-host.json");
            using var registration = hive.CreateSubKey(vendor + @"\NativeMessagingHosts\com.alterdpi.bridge");
            if (registration.GetValue("") is string old && !old.Equals(path, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("INSTALL_ROOT");
            registration.SetValue("", path);
        }
        SetTreeAcl(Product.InstallRoot, "S-1-5-32-545");
        var store = new Store(Product.StateRoot); store.Write("app-build.json", Math.Max(store.Read<long>("app-build.json"), manifest.Build)); await Sc(["start", Product.Service]);
    }
    public static async Task Unconfigure(bool upgrade = false)
    {
        RequireAdmin(); RequireRoot(); RequireOwned(Product.InstallRoot, "install.owner"); RequireService();
        if (upgrade) foreach (var p in Process.GetProcessesByName("AlterDPI")) using (p) { if (p.Id != Environment.ProcessId && p.SessionId != 0 && string.Equals(ProcessIdentity.Image((uint)p.Id), Path.Combine(Product.InstallRoot, "AlterDPI.exe"), StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("APP_RUNNING"); }
        foreach (var p in Process.GetProcessesByName("AlterDPI.Native")) using (p) { if (string.Equals(ProcessIdentity.Image((uint)p.Id), Path.Combine(Product.InstallRoot, "AlterDPI.Native.exe"), StringComparison.OrdinalIgnoreCase)) p.Kill(); }
        await StopService(); await Native.Policy("Remove", CancellationToken.None);
        if (Registry.GetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\AlterDPI", "ImagePath", null) != null) await Sc(["delete", Product.Service]);
        for (var i = 0; i < 50 && Registry.GetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\AlterDPI", "ImagePath", null) != null; i++) await Task.Delay(100);
        if (Registry.GetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\AlterDPI", "ImagePath", null) != null) throw new IOException("SERVICE");
        var owner = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\AlterDPI", "OwnerSid", null) as string;
        if (owner != null)
        {
            using var hive = Registry.Users.OpenSubKey(owner, true);
            if (hive != null)
            {
                foreach (var vendor in Vendors) { var keyPath = vendor + @"\NativeMessagingHosts\com.alterdpi.bridge"; using var key = hive.OpenSubKey(keyPath); var expected = Path.Combine(Product.InstallRoot, "browser", vendor.Contains("Mozilla", StringComparison.Ordinal) ? "firefox-host.json" : "chromium-host.json"); if (key?.GetValue("") is string value && value.Equals(expected, StringComparison.OrdinalIgnoreCase)) hive.DeleteSubKeyTree(keyPath, false); }
                if (!upgrade) { using var run = hive.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true); if (run?.GetValue(Product.Name) as string == '"' + Path.Combine(Product.InstallRoot, "AlterDPI.exe") + "\" --startup") { run.DeleteValue(Product.Name, false); using var approved = hive.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run", true); approved?.DeleteValue(Product.Name, false); } }
            }
        }
        if (!upgrade) { await Native.Policy("LegacyResume", CancellationToken.None); Registry.LocalMachine.DeleteSubKeyTree(@"SOFTWARE\AlterDPI", false); }
    }
    public static void ValidateBackup() { RequireAdmin(); RequireOwned(Product.InstallRoot + ".rollback", "install.owner"); }
    public static void DeleteLogs() { RequireAdmin(); RequireRoot(); if (!Directory.Exists(Product.StateRoot)) return; RequireOwned(Product.StateRoot, "state.owner"); Directory.Delete(Product.StateRoot, true); }
    public static void KeepOnlyLogs()
    {
        RequireAdmin(); RequireRoot(); if (!Directory.Exists(Product.StateRoot)) return; RequireOwned(Product.StateRoot, "state.owner");
        foreach (var path in Directory.GetFileSystemEntries(Product.StateRoot))
        {
            if (Path.GetFileName(path) is "state.owner" or "kayitlar") continue;
            if (Directory.Exists(path)) Directory.Delete(path, true); else File.Delete(path);
        }
    }
    private static void RequireRoot() { if (!Path.GetFullPath(AppContext.BaseDirectory).TrimEnd('\\').Equals(Product.InstallRoot, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("INSTALL_ROOT"); }
    private static void RequireOwned(string root, string marker)
    {
        Trust.PlainTree(root); if (!File.Exists(Path.Combine(root, marker)) || File.ReadAllText(Path.Combine(root, marker)) != Marker) throw new InvalidOperationException("INSTALL_ROOT");
        var sid = (new DirectoryInfo(root).GetAccessControl().GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier)?.Value; if (sid is not ("S-1-5-18" or "S-1-5-32-544")) throw new InvalidOperationException("INSTALL_ROOT");
    }
    private static void RequireService() { var image = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\AlterDPI", "ImagePath", null) as string; if (image != null && !image.Equals(ServiceImage, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("SERVICE_OWNER"); }
    private static async Task StopService()
    {
        using var service = new ServiceController(Product.Service); try { if (service.Status != ServiceControllerStatus.Stopped) { service.Stop(); await Task.Run(() => service.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(25))); } } catch (InvalidOperationException e) when (e.InnerException is System.ComponentModel.Win32Exception { NativeErrorCode: 1060 }) { }
    }
    private static Task<string> Sc(string[] args) => Native.Run(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "sc.exe"), args, CancellationToken.None);
    private static void SetTreeAcl(string root, string reader)
    {
        Trust.PlainTree(root); new DirectoryInfo(root).SetAccessControl(RootAcl(reader));
        foreach (var d in Directory.GetDirectories(root, "*", SearchOption.AllDirectories)) { var inherited = new DirectorySecurity(); inherited.SetAccessRuleProtection(false, false); inherited.SetOwner(new SecurityIdentifier("S-1-5-32-544")); new DirectoryInfo(d).SetAccessControl(inherited); }
        foreach (var f in Directory.GetFiles(root, "*", SearchOption.AllDirectories)) { var inherited = new FileSecurity(); inherited.SetAccessRuleProtection(false, false); inherited.SetOwner(new SecurityIdentifier("S-1-5-32-544")); new FileInfo(f).SetAccessControl(inherited); }
    }
    private static DirectorySecurity RootAcl(string reader)
    {
        var acl = new DirectorySecurity(); acl.SetAccessRuleProtection(true, false); acl.SetOwner(new SecurityIdentifier("S-1-5-32-544"));
        foreach (var sid in new[] { "S-1-5-18", "S-1-5-32-544", reader }) acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid), sid == reader ? FileSystemRights.ReadAndExecute : FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow)); return acl;
    }
    private static void RequireAdmin() { if (!new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator)) throw new UnauthorizedAccessException(); }
    private static string ExplorerUser()
    {
        foreach (var p in Process.GetProcessesByName("explorer")) using (p) { try { if (p.SessionId != Process.GetCurrentProcess().SessionId) continue; if (OpenProcessToken(p.Handle, 8, out var token)) { try { using var identity = new WindowsIdentity(token); if (identity.User != null) return identity.User.Value; } finally { CloseHandle(token); } } } catch (System.ComponentModel.Win32Exception) { } }
        return WindowsIdentity.GetCurrent().User!.Value;
    }
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
}
