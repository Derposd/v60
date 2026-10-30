using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using Dota2Optimizer.Core.Backup;
using Dota2Optimizer.Core.Infrastructure;

namespace Dota2Optimizer.App.ViewModels;

/// <summary>
/// Client side of <see cref="ElevatedServer"/>: the helper is started (one UAC prompt) the first time admin rights are needed and then
/// reused until the window closes. If the helper is gone or can't be reached, the next request asks for rights again.
/// </summary>
public sealed class ElevatedSession
{
    private readonly object _gate = new();
    private readonly string _pipeName = "FrameLab-" + Guid.NewGuid().ToString("N");
    private Process? _helper;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetNamedPipeServerProcessId(IntPtr pipe, out uint serverProcessId);

    /// <summary>Rights were granted in this session and the helper still runs.</summary>
    public bool IsActive
    {
        get
        {
            lock (_gate)
            {
                return _helper is { HasExited: false };
            }
        }
    }

    /// <summary>Sends one request. Returns null when the user declined the UAC prompt or the helper could not be reached.</summary>
    public ElevatedResult? Send(ElevatedRequest request)
    {
        request.BackupDir = AppPaths.Backups;
        lock (_gate)
        {
            if (_helper is not { HasExited: false })
            {
                _helper?.Dispose();
                var exe = Environment.ProcessPath ?? throw new InvalidOperationException("Не удалось определить путь к программе");
                _helper = ElevatedProcess.StartAs(exe, $"{ElevatedServer.Argument} {_pipeName} {Environment.ProcessId} --root \"{AppPaths.Root}\"");
                if (_helper is null)
                {
                    return null;
                }
            }

            try
            {
                using var pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut);
                pipe.Connect(30_000);
                // Talk only to the helper this window started.
                if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle.DangerousGetHandle(), out var pid) || pid != _helper.Id)
                {
                    Log.Warn($"Канал помощника открыт чужим процессом (pid {pid}) — запрос не отправлен");
                    return null;
                }

                using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
                using var reader = new StreamReader(pipe, leaveOpen: true);
                writer.WriteLine(ElevatedHost.Serialize(request));
                var line = reader.ReadLine();
                return line is null ? null : ElevatedHost.Deserialize<ElevatedResult>(line);
            }
            catch (Exception ex) when (ex is IOException or TimeoutException or System.Text.Json.JsonException)
            {
                Log.Error("Нет связи с помощником с правами администратора", ex);
                return null;
            }
        }
    }
}
