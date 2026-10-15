using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using Microsoft.Win32;

namespace AlterDPI.Core;

public static class Frames
{
    public const int Maximum = 256 * 1024;
    public static async Task<T?> Read<T>(Stream stream, CancellationToken token)
    {
        var length = new byte[4]; await stream.ReadExactlyAsync(length, token); var n = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(length); if (n is < 1 or > Maximum) throw new InvalidDataException("FRAME"); var data = new byte[n]; await stream.ReadExactlyAsync(data, token); return JsonSerializer.Deserialize<T>(data, Product.Json);
    }
    public static async Task Write<T>(Stream stream, T value, CancellationToken token)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, Product.Json); if (bytes.Length > Maximum) throw new InvalidDataException("FRAME"); var length = new byte[4]; System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(length, bytes.Length); await stream.WriteAsync(length, token); await stream.WriteAsync(bytes, token); await stream.FlushAsync(token);
    }
}
public sealed class PipeServer(Controller controller)
{
    private readonly SemaphoreSlim clients = new(16, 16);
    public async Task Run(CancellationToken token)
    {
        var owner = (string?)Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\AlterDPI", "OwnerSid", null) ?? throw new InvalidOperationException("OWNER"); var sid = new SecurityIdentifier(owner);
        var security = new PipeSecurity(); security.SetAccessRuleProtection(true, false); security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null), PipeAccessRights.FullControl, AccessControlType.Deny)); security.AddAccessRule(new PipeAccessRule(sid, PipeAccessRights.ReadWrite, AccessControlType.Allow)); security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
        while (!token.IsCancellationRequested)
        {
            await clients.WaitAsync(token); var pipe = NamedPipeServerStreamAcl.Create(Product.Pipe, PipeDirection.InOut, 16, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 65536, 65536, security, HandleInheritability.None);
            try { await pipe.WaitForConnectionAsync(token); _ = Handle(pipe, owner, token); } catch { pipe.Dispose(); clients.Release(); throw; }
        }
    }
    private async Task Handle(NamedPipeServerStream pipe, string owner, CancellationToken lifetime)
    {
        using (pipe) using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime))
        {
            timeout.CancelAfter(TimeSpan.FromSeconds(120)); var token = timeout.Token;
            try
            {
                // Windows impersonates the sender of the last message read; parse only the bounded frame before authentication.
                var request=await Frames.Read<ControlRequest>(pipe,token);if(request==null)return;
                string? identity = null; pipe.RunAsClient(() => identity = WindowsIdentity.GetCurrent().User?.Value); if (identity != owner || !GetNamedPipeClientProcessId(pipe.SafePipeHandle.DangerousGetHandle(), out var pid)) return;
                var image = ProcessIdentity.Image(pid);
                var native = image != null && Path.GetFullPath(image).Equals(Path.Combine(Product.InstallRoot, "AlterDPI.Native.exe"), StringComparison.OrdinalIgnoreCase);
                if (image == null || !native && !Path.GetFullPath(image).Equals(Path.Combine(Product.InstallRoot, "AlterDPI.exe"), StringComparison.OrdinalIgnoreCase)) return;
                if (native && request.Operation is not ("state" or "snapshot" or "classify" or "begin-exception" or "confirm-exception" or "cancel-exception")) return;
                if (request.Operation == "watch")
                {
                    var last = -1L; while (!token.IsCancellationRequested) { var state = controller.State; if (state.Revision != last) { await Frames.Write(pipe, new ControlReply(true, state), token); last = state.Revision; } await Task.Delay(500, token); }
                    return;
                }
                var reply = await controller.Execute(request, token); await Frames.Write(pipe, reply with { Id = request.Id }, token);
            }
            catch (Exception e) when (e is IOException or OperationCanceledException or UnauthorizedAccessException or JsonException or InvalidOperationException or System.ComponentModel.Win32Exception or ArgumentException) { }
            finally { clients.Release(); }
        }
    }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetNamedPipeClientProcessId(IntPtr handle, out uint id);
}
public static class ControlClient
{
    public static async Task<ControlReply> Send(ControlRequest request, CancellationToken token = default)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(token); limit.CancelAfter(TimeSpan.FromSeconds(110));
        using var pipe = new NamedPipeClientStream(".", Product.Pipe, PipeDirection.InOut, PipeOptions.Asynchronous, TokenImpersonationLevel.Impersonation); await pipe.ConnectAsync(1500, limit.Token);
        if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle.DangerousGetHandle(), out var pid) || !ProcessIdentity.IsSystemService(pid)) throw new UnauthorizedAccessException("PIPE_IDENTITY");
        await Frames.Write(pipe, request, limit.Token); return await Frames.Read<ControlReply>(pipe, limit.Token) ?? throw new IOException("SERVICE");
    }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetNamedPipeServerProcessId(IntPtr handle, out uint id);
}
public static class ProcessIdentity
{
    public static string? Image(uint pid)
    {
        var handle = OpenProcess(0x1000, false, pid); if (handle == IntPtr.Zero) return null;
        try { return Image(handle); } finally { CloseHandle(handle); }
    }
    private static string? Image(IntPtr handle) { var path = new System.Text.StringBuilder(32768); var length = path.Capacity; return QueryFullProcessImageName(handle, 0, path, ref length) ? path.ToString() : null; }
    public static bool IsSystemService(uint pid)
    {
        var expected = '"' + Path.Combine(Product.InstallRoot, "AlterDPI.exe") + "\" --service";
        if (!string.Equals(Registry.GetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\AlterDPI", "ImagePath", null) as string, expected, StringComparison.OrdinalIgnoreCase) || Registry.GetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\AlterDPI", "ObjectName", null) as string != "LocalSystem") return false;
        var manager = OpenSCManager(null, null, 1); if (manager == IntPtr.Zero) return false;
        try
        {
            var service = OpenService(manager, Product.Service, 4); if (service == IntPtr.Zero) return false;
            // SCM binds the kernel-reported pipe PID to the protected LocalSystem service configuration.
            // Ordinary users may not query a SYSTEM process image on every Windows installation.
            try { return QueryServiceStatusEx(service, 0, out var status, 36, out _) && status.State == 4 && status.ProcessId == pid && status.Type == 0x10; } finally { CloseServiceHandle(service); }
        }
        finally { CloseServiceHandle(manager); }
    }
    [StructLayout(LayoutKind.Sequential)] private struct ServiceStatus { public uint Type, State, Accepted, Win32Exit, SpecificExit, Checkpoint, WaitHint, ProcessId, Flags; }
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr OpenSCManager(string? machine, string? database, uint access);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr OpenService(IntPtr manager, string name, uint access);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool QueryServiceStatusEx(IntPtr service, int level, out ServiceStatus status, int size, out int needed);
    [DllImport("advapi32.dll")] private static extern bool CloseServiceHandle(IntPtr handle);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool QueryFullProcessImageName(IntPtr handle, uint flags, System.Text.StringBuilder path, ref int length);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
}
