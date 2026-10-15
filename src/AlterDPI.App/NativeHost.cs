using System.IO.Pipes;
using System.Text.Json;
using System.Windows;
using AlterDPI.Core;

namespace AlterDPI.App;

public static class NativeHost
{
    public static int Run()
    {
        using var input = Console.OpenStandardInput(); using var output = Console.OpenStandardOutput(); var writeGate = new object(); var lifetime = new CancellationTokenSource();
        void Send(object value) { lock (writeGate) { Frames.Write(output, value, CancellationToken.None).GetAwaiter().GetResult(); } }
        _ = Task.Run(async () => { long last = -1; string exceptions = ""; try { while (!lifetime.IsCancellationRequested) { try { var reply = await ControlClient.Send(new("state"), lifetime.Token); var ex = JsonSerializer.Serialize(reply.State?.Exceptions, Product.Json); if (reply.State?.Revision != last || ex != exceptions) { Send(new { eventType = "state", state = reply.State }); last = reply.State?.Revision ?? -1; exceptions = ex; } } catch (Exception e) when (e is IOException or TimeoutException or UnauthorizedAccessException) { Send(new { eventType = "offline" }); last = -1; } await Task.Delay(2000, lifetime.Token); } } catch (Exception e) when (e is IOException or OperationCanceledException or ObjectDisposedException) { } });
        try
        {
            while (true)
            {
                var request = Frames.Read<ControlRequest>(input, CancellationToken.None).GetAwaiter().GetResult(); if (request == null) break;
                if (request.Operation is not ("state" or "snapshot" or "classify" or "exception")) { Send(new { id = request.Id, ok = false }); continue; }
                if (request.Operation != "exception") { var reply = ControlClient.Send(request).GetAwaiter().GetResult(); Send(reply with { Id = request.Id }); continue; }
                var host = Threats.Normalize(request.Host ?? ""); var begin = ControlClient.Send(new("begin-exception", Host: host)).GetAwaiter().GetResult(); if (!begin.Ok) { Send(new { id = request.Id, ok = false }); continue; }
                var token = ((JsonElement)begin.Data!).GetProperty("token").GetString()!; var granted = false;
                for (int step = 1; step <= 2; step++)
                {
                    var answer = MessageBox.Show(step == 1 ? $"{host}\n\nBu alan adı güvenilmez olarak işaretlendi. Yine de devam etmek istiyor musunuz?" : $"{host}\n\nSon onay: bu alan adına bu bilgisayarda 10 dakika erişim izni verilecek. Riskleri kabul ediyor musunuz?", "AlterDPI — " + step + "/2", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
                    if (answer != MessageBoxResult.Yes) { ControlClient.Send(new("cancel-exception", Token: token)).GetAwaiter().GetResult(); break; }
                    var reply = ControlClient.Send(new("confirm-exception", Token: token)).GetAwaiter().GetResult(); if (!reply.Ok) break; granted = ((JsonElement)reply.Data!).GetProperty("granted").GetBoolean();
                }
                Send(new { id = request.Id, ok = true, data = new { granted } });
            }
        }
        catch (Exception e) when (e is EndOfStreamException or IOException or JsonException or ArgumentException or UnauthorizedAccessException or TimeoutException or OperationCanceledException) { return 0; }
        finally { lifetime.Cancel(); }
        return 0;
    }
}
