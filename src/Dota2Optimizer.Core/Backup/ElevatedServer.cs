using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using Dota2Optimizer.Core.Infrastructure;

namespace Dota2Optimizer.Core.Backup;

/// <summary>
/// The session helper: one elevated copy of the exe (one UAC prompt) that serves apply / rollback requests for as long as the window
/// that started it is open. Requests come over a named pipe that only the current user can open, and the helper answers only the
/// process that started it (checked by process id), so another program of the same user can't borrow its rights.
/// One JSON line in, one JSON line out.
/// </summary>
[SupportedOSPlatform("windows")]
public static class ElevatedServer
{
    public const string Argument = "--elevated-serve";

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetNamedPipeClientProcessId(IntPtr pipe, out uint clientProcessId);

    public static int Serve(string pipeName, int parentPid)
    {
        Process parent;
        try
        {
            parent = Process.GetProcessById(parentPid);
        }
        catch (ArgumentException)
        {
            return 1;
        }

        // The helper ends together with the window that asked for it.
        var watcher = new Thread(() =>
        {
            parent.WaitForExit();
            Log.Info("Окно программы закрыто — помощник с правами администратора завершается");
            Environment.Exit(0);
        }) { IsBackground = true };
        watcher.Start();

        var security = new PipeSecurity();
        var user = WindowsIdentity.GetCurrent().User ?? throw new InvalidOperationException("Не удалось определить пользователя");
        security.AddAccessRule(new PipeAccessRule(user, PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null), PipeAccessRights.FullControl, AccessControlType.Deny));

        while (true)
        {
            using var pipe = NamedPipeServerStreamAcl.Create(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.None, 0, 0, security);
            pipe.WaitForConnection();
            try
            {
                if (!GetNamedPipeClientProcessId(pipe.SafePipeHandle.DangerousGetHandle(), out var pid) || pid != parentPid)
                {
                    Log.Warn($"Помощник отклонил подключение чужого процесса (pid {pid})");
                    continue;
                }

                using var reader = new StreamReader(pipe, leaveOpen: true);
                using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
                var line = reader.ReadLine();
                if (line is null)
                {
                    continue;
                }

                ElevatedResult result;
                try
                {
                    var request = ElevatedHost.Deserialize<ElevatedRequest>(line) ?? throw new InvalidOperationException("Пустой запрос");
                    result = ElevatedHost.Execute(request);
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.Text.Json.JsonException or IOException or UnauthorizedAccessException)
                {
                    Log.Error("Помощник не выполнил запрос", ex);
                    result = new ElevatedResult { Messages = { "Ошибка: " + ex.Message } };
                }

                writer.WriteLine(ElevatedHost.Serialize(result));
            }
            catch (IOException ex)
            {
                Log.Warn($"Связь с окном программы прервалась: {ex.Message}");
            }
        }
    }
}
