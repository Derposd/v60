using System.Collections.ObjectModel;
using System.Net.NetworkInformation;
using System.Windows.Input;
using Dota2Optimizer.Core.Backup;
using Dota2Optimizer.Core.Infrastructure;
using Dota2Optimizer.Core.Network;

namespace Dota2Optimizer.App.ViewModels;

public sealed class NetworkViewModel : ObservableObject
{
    private readonly AppServices _s;
    private string _adapterText = "Нажмите «Запустить тест». Сеть используется только во время теста.";
    private string? _verdictTitle;
    private string? _verdictDetails;
    private bool _pingValve = true;
    private bool _busy;
    private string _dnsChoice = "cloudflare";
    private CancellationTokenSource? _cts;

    public NetworkViewModel(AppServices services)
    {
        _s = services;
        RunCommand = new AsyncCommand(RunAsync, () => !_busy);
        CancelCommand = new RelayCommand(() => _cts?.Cancel(), () => _busy);
        FlushDnsCommand = new RelayCommand(() =>
        {
            var r = new NetworkDiagnostics(_s.Commands).FlushDns();
            Ui.Info(r.Success ? "Кеш DNS очищен." : $"Не удалось: {r.StdErr}");
        });
        ChangeDnsCommand = new AsyncCommand(ChangeDnsAsync, () => !_busy);
        ResetStackCommand = new RelayCommand(ResetStack, () => !_busy);
        CopyCommand = new RelayCommand(() => System.Windows.Clipboard.SetText(ResultsText()));
    }

    public ICommand RunCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand FlushDnsCommand { get; }
    public ICommand ChangeDnsCommand { get; }
    public ICommand ResetStackCommand { get; }
    public ICommand CopyCommand { get; }

    public ObservableCollection<TargetResult> Results { get; } = new();

    public string AdapterText
    {
        get => _adapterText;
        private set => Set(ref _adapterText, value);
    }

    public string? VerdictTitle
    {
        get => _verdictTitle;
        private set => Set(ref _verdictTitle, value);
    }

    public string? VerdictDetails
    {
        get => _verdictDetails;
        private set => Set(ref _verdictDetails, value);
    }

    public bool PingValve
    {
        get => _pingValve;
        set => Set(ref _pingValve, value);
    }

    public bool DnsCloudflare
    {
        get => _dnsChoice == "cloudflare";
        set
        {
            if (value)
            {
                _dnsChoice = "cloudflare";
            }
        }
    }

    public bool DnsGoogle
    {
        get => _dnsChoice == "google";
        set
        {
            if (value)
            {
                _dnsChoice = "google";
            }
        }
    }

    public bool DnsQuad9
    {
        get => _dnsChoice == "quad9";
        set
        {
            if (value)
            {
                _dnsChoice = "quad9";
            }
        }
    }

    public string MythsText =>
        "Чего здесь намеренно нет: отключение алгоритма Нейгла (TCPNoDelay/TcpAckFrequency), смена MTU «для пинга», «ускорители интернета», " +
        "NetworkThrottlingIndex и прочие правки реестра TCP. Dota 2 работает по UDP через сеть релеев Valve (SDR), и эти настройки на игровой трафик не влияют " +
        "или их эффект не подтверждён тестами. Реально помогают: кабель вместо Wi-Fi, ближайший регион поиска, отсутствие фоновых загрузок, нормальный провайдер.";

    private async Task RunAsync()
    {
        _busy = true;
        _cts = new CancellationTokenSource();
        Results.Clear();
        VerdictTitle = null;
        VerdictDetails = null;
        try
        {
            var adapter = NetworkDiagnostics.ActiveAdapter();
            if (adapter is null)
            {
                AdapterText = "Нет активного подключения к сети с основным шлюзом.";
                return;
            }

            var info = $"Подключение: {adapter.Description} ({(adapter.IsWifi ? "Wi-Fi" : adapter.Type == NetworkInterfaceType.Ethernet ? "кабель" : adapter.Type.ToString())}, {adapter.SpeedMbps} Мбит/с)";
            if (adapter.IsWifi)
            {
                var (signal, ssid, radio) = await Task.Run(() => new NetworkDiagnostics(_s.Commands).WifiInfo());
                info += $"\nWi-Fi: {ssid ?? "?"}, сигнал {(signal is null ? "?" : signal + "%")}, {radio ?? string.Empty}";
                if (signal is < 60)
                {
                    info += " — слабый сигнал, возможны потери и скачки пинга.";
                }
            }

            info += $"\nDNS: {(adapter.DnsIsStatic ? "вручную" : "автоматически")} — {string.Join(", ", adapter.Dns)}";
            AdapterText = info + "\nИзмеряю фоновую нагрузку сети (3 с)…";
            var traffic = await NetworkDiagnostics.SampleTrafficAsync(adapter, TimeSpan.FromSeconds(3), _cts.Token);
            info += $"\nТекущий трафик: ↓ {traffic.DownMbps:0.0} Мбит/с, ↑ {traffic.UpMbps:0.0} Мбит/с";
            if (traffic.DownMbps > 5 || traffic.UpMbps > 2)
            {
                info += " — что-то активно качает/отдаёт в фоне (обновления, торрент, облако). Это повышает пинг.";
            }

            AdapterText = info;

            var targets = new List<(string Name, string Host, TargetKind Kind)>();
            if (adapter.Gateway is not null)
            {
                targets.Add(("Роутер (шлюз)", adapter.Gateway.ToString(), TargetKind.Gateway));
            }

            targets.Add(("Cloudflare DNS", "1.1.1.1", TargetKind.Internet));
            targets.Add(("Google DNS", "8.8.8.8", TargetKind.Internet));

            if (PingValve)
            {
                try
                {
                    var pops = await NetworkDiagnostics.FetchValvePopsAsync(_cts.Token);
                    targets.AddRange(pops.Select(p => ($"Valve: {p.Description}", p.Ipv4!, TargetKind.Valve)));
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
                {
                    AdapterText += "\nСписок серверов Valve получить не удалось — пропускаю (проверьте пинг в игре).";
                }
            }

            // Router and public resolvers first and slower (40 pings), relays in parallel with fewer pings.
            foreach (var t in targets.Where(t => t.Kind != TargetKind.Valve))
            {
                var stats = await NetworkDiagnostics.PingAsync(t.Host, 40, _cts.Token);
                Results.Add(new TargetResult(t.Name, t.Host, t.Kind, stats));
            }

            var valveTasks = targets.Where(t => t.Kind == TargetKind.Valve)
                .Select(async t => new TargetResult(t.Name, t.Host, t.Kind, await NetworkDiagnostics.PingAsync(t.Host, 8, _cts.Token)))
                .ToList();
            foreach (var r in (await Task.WhenAll(valveTasks)).OrderBy(r => r.Stats.AvgMs ?? double.MaxValue))
            {
                Results.Add(r);
            }

            var verdict = NetworkAnalysis.Evaluate(Results.ToList(), adapter.IsWifi);
            VerdictTitle = verdict.Title;
            VerdictDetails = verdict.Details;
            _s.History.Add(new Core.Benchmark.HistoryEntry(DateTime.Now, "Сеть", verdict.Title, ResultsText()));
        }
        catch (OperationCanceledException)
        {
            VerdictTitle = "Тест остановлен";
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task ChangeDnsAsync()
    {
        var adapter = NetworkDiagnostics.ActiveAdapter();
        if (adapter is null)
        {
            Ui.Info("Нет активного подключения.");
            return;
        }

        var (primary, secondary, name) = _dnsChoice switch
        {
            "google" => ("8.8.8.8", "8.8.4.4", "Google"),
            "quad9" => ("9.9.9.9", "149.112.112.112", "Quad9"),
            _ => ("1.1.1.1", "1.0.0.1", "Cloudflare"),
        };
        if (!Ui.Confirm($"Сменить DNS адаптера «{adapter.Name}» на {name} ({primary}, {secondary})?\n\n" +
                        "Честно: DNS не влияет на пинг в матче. Помогает, только если у провайдера медленный или сбойный DNS (долго открываются Steam/сайты). " +
                        "Нужны права администратора. Откат — на вкладке «Откат»."))
        {
            return;
        }

        _busy = true;
        try
        {
            var action = NetworkDiagnostics.PlanDnsChange(adapter, primary, secondary);
            var request = new ElevatedRequest
            {
                Mode = "apply",
                Items = { new ElevatedItem { TweakId = "net.dns", Title = $"DNS → {name}", Actions = { action } } },
            };
            var result = await Task.Run(() => AppServices.RunElevated(request));
            var item = result?.Items.FirstOrDefault();
            Ui.Info(result is null ? "Запрос прав администратора отклонён — ничего не изменено." : item?.Success == true ? $"DNS изменён на {name}." : $"Не удалось: {item?.Error}");
            if (item?.Success == true)
            {
                _s.History.Add(new Core.Benchmark.HistoryEntry(DateTime.Now, "Применено", $"DNS → {name}", action.Description));
                _s.NotifyChangesApplied();
            }
        }
        finally
        {
            _busy = false;
        }
    }

    private void ResetStack()
    {
        if (!Ui.Confirm("Сбросить сетевой стек Windows (netsh winsock reset + netsh int ip reset)?\n\n" +
                        "Помогает при поломанных сетевых настройках после VPN/«ускорителей». Это НЕЛЬЗЯ откатить кнопкой: настройки вернутся к стандартным Windows, " +
                        "статические IP/DNS придётся задать заново. Нужны права администратора и перезагрузка."))
        {
            return;
        }

        var code = NetworkDiagnostics.ResetStack();
        _s.History.Add(new Core.Benchmark.HistoryEntry(DateTime.Now, "Обслуживание", "Сброс сетевого стека", $"Код возврата {code}"));
        Ui.Info(code == 0 ? "Готово. Перезагрузите компьютер." : code == -1 ? "Отменено." : $"Команда вернула код {code}.");
    }

    private string ResultsText() =>
        $"{AdapterText}\n\n" + string.Join("\n", Results.Select(r => $"{r.Name} ({r.Host}): {r.Stats}")) + $"\n\n{VerdictTitle}\n{VerdictDetails}";
}
