using System.Net.Sockets;
using System.Text.Encodings.Web;
using System.Text.Json;
using Signal.ModemEmulator.Models;
using Signal.ModemEmulator.Services;

const string ConfigFileName = "modem_config.json";

var jsonOptions = new JsonSerializerOptions
{
    PropertyNameCaseInsensitive = true,
    WriteIndented = true,
    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
};

ModemConfig config;
if (File.Exists(ConfigFileName))
{
    var raw = await File.ReadAllTextAsync(ConfigFileName);
    config = JsonSerializer.Deserialize<ModemConfig>(raw, jsonOptions) ?? new ModemConfig();
}
else
{
    config = new ModemConfig();
    await File.WriteAllTextAsync(ConfigFileName, JsonSerializer.Serialize(config, jsonOptions));
    Console.WriteLine($"[КОНФИГ] Создан файл конфигурации по умолчанию: {ConfigFileName}");
}

// Поддержка переопределения параметров через аргументы CLI: --port 23 --ip 127.0.0.1 --mode nolock
string? initialMode = null;
for (int i = 0; i < args.Length - 1; i++)
{
    if (args[i].Equals("--port", StringComparison.OrdinalIgnoreCase) && int.TryParse(args[i + 1], out int parsedPort))
        config.Port = parsedPort;
    else if (args[i].Equals("--ip", StringComparison.OrdinalIgnoreCase))
        config.ListenIp = args[i + 1];
    else if (args[i].Equals("--login", StringComparison.OrdinalIgnoreCase))
        config.Login = args[i + 1];
    else if (args[i].Equals("--password", StringComparison.OrdinalIgnoreCase))
        config.Password = args[i + 1];
    else if (args[i].Equals("--mode", StringComparison.OrdinalIgnoreCase))
        initialMode = args[i + 1];
}

var stateService = new ModemStateService(config);
if (!string.IsNullOrWhiteSpace(initialMode))
    ApplyModeByName(stateService, initialMode);

var server = new TelnetModemServer(config, stateService);
using var cts = new CancellationTokenSource();

Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

var serverTask = Task.Run(async () =>
{
    try
    {
        await server.StartAsync(cts.Token);
    }
    catch (SocketException ex)
    {
        Console.WriteLine($"[ОШИБКА] Не удалось открыть порт {config.Port}: {ex.Message}");
        Console.WriteLine("[ПОДСКАЗКА] На Linux/macOS порт 23 требует sudo, либо запустите с другим портом: dotnet run -- --port 2323");
        cts.Cancel();
    }
});

PrintHelp();

// Интерактивная консоль управления эмулятором
while (!cts.Token.IsCancellationRequested)
{
    var line = Console.ReadLine();
    if (line is null) break;

    var parts = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
    if (parts.Length == 0) continue;

    var cmd = parts[0].ToLowerInvariant();
    switch (cmd)
    {
        case "help":
            PrintHelp();
            break;

        case "status":
            Console.WriteLine($"--- Режим: {stateService.Mode} | Автодрейф: {(stateService.AutoDrift ? "ON" : "OFF")} ---");
            foreach (var kv in stateService.GetSnapshot())
                Console.WriteLine($"  <{stateService.ModemAddress}/{kv.Key}?  =>  >{stateService.ModemAddress}/{kv.Key}={kv.Value}");
            break;

        case "mode" when parts.Length >= 2:
            if (ApplyModeByName(stateService, parts[1]))
                Console.WriteLine($"[РЕЖИМ] Установлен режим: {stateService.Mode}");
            else
                Console.WriteLine("Доступные режимы: normal, nolock, warning, overheat");
            break;

        case "set" when parts.Length >= 3:
            var metricKey = parts[1].ToUpperInvariant();
            var metricVal = string.Join(' ', parts.Skip(2));
            stateService.SetMetric(metricKey, metricVal);
            Console.WriteLine($"[МЕТРИКА] {metricKey} зафиксирована = {metricVal}");
            break;

        case "clear" when parts.Length >= 2:
            stateService.ClearOverride(parts[1]);
            Console.WriteLine($"[МЕТРИКА] Ручное переопределение для {parts[1].ToUpperInvariant()} сброшено.");
            break;

        case "auto" when parts.Length >= 2:
            stateService.AutoDrift = parts[1].Equals("on", StringComparison.OrdinalIgnoreCase);
            Console.WriteLine($"[АВТОДРЕЙФ] {(stateService.AutoDrift ? "Включён" : "Выключен")}");
            break;

        case "exit":
        case "quit":
            cts.Cancel();
            break;

        default:
            Console.WriteLine("Неизвестная команда. Введите 'help' для списка команд.");
            break;
    }
}

await serverTask;

static bool ApplyModeByName(ModemStateService service, string name)
{
    switch (name.Trim().ToLowerInvariant())
    {
        case "normal":
            service.SetMode(ModemSimulationMode.Normal);
            return true;
        case "nolock":
            service.SetMode(ModemSimulationMode.NoLock);
            return true;
        case "warning":
            service.SetMode(ModemSimulationMode.Warning);
            return true;
        case "overheat":
            service.SetMode(ModemSimulationMode.Overheat);
            return true;
        default:
            return false;
    }
}

static void PrintHelp()
{
    Console.WriteLine("Команды управления эмулятором:");
    Console.WriteLine("  status                  - показать текущие ответы модема на команды");
    Console.WriteLine("  mode normal             - штатный режим (все показатели в норме)");
    Console.WriteLine("  mode nolock             - потеря сигнала (EBN=99.9, BER=99.9)");
    Console.WriteLine("  mode warning            - деградация сигнала (TMP=+34, EBN=5.2)");
    Console.WriteLine("  mode overheat           - перегрев (TMP=+39, ошибка и алерт)");
    Console.WriteLine("  set <CMD> <VAL>         - задать значение вручную (пример: set EBN 99.9)");
    Console.WriteLine("  clear <CMD>             - вернуть автогенерацию для метрики");
    Console.WriteLine("  auto on|off             - включить/выключить плавное изменение метрик");
    Console.WriteLine("  exit                    - остановить эмулятор");
}