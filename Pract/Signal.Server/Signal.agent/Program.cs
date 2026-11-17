using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Signal.agent.Models;
using Signal.Server.Data.Dtos;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

class Program
{
    static async Task Main(string[] args)
    {
        Console.Write("Введите IP-адрес или домен API сервера: ");
        string apiHost = Console.ReadLine()?.Trim() ?? "localhost";
        if (string.IsNullOrWhiteSpace(apiHost)) apiHost = "localhost";

        Console.Write("Введите порт API сервера: ");
        string apiPort = Console.ReadLine()?.Trim() ?? "5000";
        if (string.IsNullOrWhiteSpace(apiPort)) apiPort = "5000";

        string baseApiUrl = $"http://{apiHost}:{apiPort}/";

        var httpClient = new HttpClient
        {
            BaseAddress = new Uri(baseApiUrl),
            Timeout = TimeSpan.FromSeconds(10)
        };

        var configManager = new AgentConfigurationManager(httpClient);
        var offlineQueue = new OfflineTelemetryQueue("telemetry_queue.jsonl");

        // Поддержка CLI-аргументов или интерактивного выбора режима синхронизации конфигурации
        if (args.Contains("--pull-config"))
        {
            await configManager.PullConfigFromServerAsync("devices.json");
        }
        else if (args.Contains("--push-config"))
        {
            await configManager.PushConfigToServerAsync("devices.json");
        }
        else if (args.Length >= 2 && args[0] == "--import-file")
        {
            await configManager.ImportExternalConfigAsync(args[1], "devices.json");
        }
        else if (args.Length >= 2 && args[0] == "--export-file")
        {
            await configManager.ExportConfigToFileAsync("devices.json", args[1]);
        }
        else
        {
            Console.WriteLine("Выберите действие при запуске: [Enter] Обычный запуск | [1] Загрузить конфиг с сервера | [2] Выгрузить локальный конфиг на сервер | [3] Экспорт в файл | [4] Импорт из файла");
            string? choice = Console.ReadLine()?.Trim();
            if (choice == "1")
                await configManager.PullConfigFromServerAsync("devices.json");
            else if (choice == "2")
                await configManager.PushConfigToServerAsync("devices.json");
            else if (choice == "3")
            {
                Console.Write("Укажите путь для экспорта JSON: ");
                var target = Console.ReadLine()?.Trim();
                if (!string.IsNullOrEmpty(target))
                    await configManager.ExportConfigToFileAsync("devices.json", target);
            }
            else if (choice == "4")
            {
                Console.Write("Укажите путь к импортируемому JSON: ");
                var source = Console.ReadLine()?.Trim();
                if (!string.IsNullOrEmpty(source))
                    await configManager.ImportExternalConfigAsync(source, "devices.json");
            }
        }

        IHostBuilder hostBuilder = Host.CreateDefaultBuilder(args)
            .ConfigureServices((hostContext, services) =>
            {
                services.AddSingleton(httpClient);
                services.AddSingleton(configManager);
                services.AddSingleton(offlineQueue);
                services.AddHostedService<NetworkPollingService>();
            });

        await hostBuilder.RunConsoleAsync();
    }
}

public class AgentConfigurationManager(HttpClient httpClient)
{
    private readonly HttpClient _httpClient = httpClient;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public async Task<List<DeviceConfig>> LoadLocalConfigAsync(string filePath = "devices.json")
    {
        try
        {
            if (!File.Exists(filePath))
            {
                Console.WriteLine($"[КОНФИГ] Локальный файл {filePath} не найден. Пробуем получить с сервера...");
                var pulled = await PullConfigFromServerAsync(filePath);
                return pulled;
            }

            string jsonString = await File.ReadAllTextAsync(filePath);
            var devices = JsonSerializer.Deserialize<List<DeviceConfig>>(jsonString, _jsonOptions);
            return devices ?? new List<DeviceConfig>();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ОШИБКА] Ошибка чтения конфигурации {filePath}: {ex.Message}");
            return new List<DeviceConfig>();
        }
    }

    public async Task<List<DeviceConfig>> PullConfigFromServerAsync(string targetFilePath = "devices.json")
    {
        try
        {
            var serverDevices = await _httpClient.GetFromJsonAsync<List<DeviceConfig>>("api/equipment/agent-config", _jsonOptions);
            if (serverDevices != null && serverDevices.Count > 0)
            {
                // Сохраняем локальные логины/пароли Telnet, если они уже были в локальном файле
                if (File.Exists(targetFilePath))
                {
                    var existing = JsonSerializer.Deserialize<List<DeviceConfig>>(await File.ReadAllTextAsync(targetFilePath), _jsonOptions) ?? new();
                    foreach (var dev in serverDevices)
                    {
                        var match = existing.FirstOrDefault(x => x.Ip == dev.Ip);
                        if (match != null)
                        {
                            dev.Login ??= match.Login;
                            dev.Password ??= match.Password;
                        }
                    }
                }

                string json = JsonSerializer.Serialize(serverDevices, _jsonOptions);
                await File.WriteAllTextAsync(targetFilePath, json);
                Console.WriteLine($"[КОНФИГ] Конфигурация ({serverDevices.Count} устр.) успешно загружена с сервера в {targetFilePath}");
                return serverDevices;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ОШИБКА] Не удалось получить конфигурацию с сервера: {ex.Message}");
        }
        return new List<DeviceConfig>();
    }

    public async Task<bool> PushConfigToServerAsync(string sourceFilePath = "devices.json")
    {
        try
        {
            if (!File.Exists(sourceFilePath))
            {
                Console.WriteLine($"[ОШИБКА] Файл {sourceFilePath} не существует для выгрузки.");
                return false;
            }

            var devices = await LoadLocalConfigAsync(sourceFilePath);
            var response = await _httpClient.PostAsJsonAsync("api/equipment/agent-config", devices, _jsonOptions);
            if (response.IsSuccessStatusCode)
            {
                Console.WriteLine($"[КОНФИГ] Локальная конфигурация ({devices.Count} устр.) успешно выгружена в БД сервера (equipment_command).");
                return true;
            }
            Console.WriteLine($"[ОШИБКА] Сервер вернул статус {response.StatusCode} при выгрузке конфигурации.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ОШИБКА] Не удалось выгрузить конфигурацию на сервер: {ex.Message}");
        }
        return false;
    }

    public async Task ExportConfigToFileAsync(string sourceFilePath, string exportFilePath)
    {
        var devices = await LoadLocalConfigAsync(sourceFilePath);
        var dir = Path.GetDirectoryName(exportFilePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(exportFilePath, JsonSerializer.Serialize(devices, _jsonOptions));
        Console.WriteLine($"[КОНФИГ] Конфигурация экспортирована в файл: {exportFilePath}");
    }

    public async Task ImportExternalConfigAsync(string importFilePath, string targetFilePath = "devices.json")
    {
        if (!File.Exists(importFilePath))
        {
            Console.WriteLine($"[ОШИБКА] Импортируемый файл не найден: {importFilePath}");
            return;
        }
        string raw = await File.ReadAllTextAsync(importFilePath);
        var devices = JsonSerializer.Deserialize<List<DeviceConfig>>(raw, _jsonOptions) ?? new List<DeviceConfig>();
        await File.WriteAllTextAsync(targetFilePath, JsonSerializer.Serialize(devices, _jsonOptions));
        Console.WriteLine($"[КОНФИГ] Импортировано устройств: {devices.Count} в {targetFilePath}");
        await PushConfigToServerAsync(targetFilePath);
    }
}

public class OfflineTelemetryQueue(string queueFilePath = "telemetry_queue.jsonl")
{
    private readonly string _queueFilePath = queueFilePath;
    private readonly SemaphoreSlim _fileLock = new(1, 1);

    public async Task EnqueueAsync(CreateReadingLogDto dto)
    {
        dto.WriteDateTime ??= DateTime.UtcNow;
        await _fileLock.WaitAsync();
        try
        {
            string line = JsonSerializer.Serialize(dto);
            await File.AppendAllTextAsync(_queueFilePath, line + Environment.NewLine, Encoding.UTF8);
            Console.WriteLine($"[ОФЛАЙН-КЭШ] Метрика сохранена локально: IP={dto.IpAddress}, Param={dto.ReadingTypeIdentifier}, Val={dto.Value}, Time={dto.WriteDateTime:O}");
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task FlushIfAvailableAsync(HttpClient httpClient, int batchSize = 50)
    {
        if (!File.Exists(_queueFilePath)) return;

        await _fileLock.WaitAsync();
        try
        {
            var lines = (await File.ReadAllLinesAsync(_queueFilePath, Encoding.UTF8))
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .ToList();

            if (lines.Count == 0)
            {
                File.Delete(_queueFilePath);
                return;
            }

            var allCached = new List<CreateReadingLogDto>();
            foreach (var line in lines)
            {
                try
                {
                    var item = JsonSerializer.Deserialize<CreateReadingLogDto>(line);
                    if (item != null) allCached.Add(item);
                }
                catch { }
            }

            // Сортируем строго по хронологии снятия показаний
            allCached = allCached.OrderBy(x => x.WriteDateTime ?? DateTime.MinValue).ToList();

            int sentCount = 0;
            while (sentCount < allCached.Count)
            {
                var batch = allCached.Skip(sentCount).Take(batchSize).ToList();
                var response = await httpClient.PostAsJsonAsync("api/equipment/logs/batch", batch);
                if (!response.IsSuccessStatusCode)
                {
                    // Сохраняем неотправленный остаток обратно на диск
                    var remaining = allCached.Skip(sentCount).Select(x => JsonSerializer.Serialize(x));
                    await File.WriteAllLinesAsync(_queueFilePath, remaining, Encoding.UTF8);
                    return;
                }
                sentCount += batch.Count;
            }

            File.Delete(_queueFilePath);
            Console.WriteLine($"[СИНХРОНИЗАЦИЯ] Успешно дослано {sentCount} закэшированных записей телеметрии на сервер.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ОФЛАЙН-КЭШ] Сервер пока недоступен для досылки кэша: {ex.Message}");
        }
        finally
        {
            _fileLock.Release();
        }
    }
}

public class NetworkPollingService(
    HttpClient httpClient,
    AgentConfigurationManager configManager,
    OfflineTelemetryQueue offlineQueue) : BackgroundService
{
    private readonly HttpClient _httpClient = httpClient;
    private readonly AgentConfigurationManager _configManager = configManager;
    private readonly OfflineTelemetryQueue _offlineQueue = offlineQueue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            // Перед новым циклом опроса пробуем дослать накопленный офлайн-кэш
            await _offlineQueue.FlushIfAvailableAsync(_httpClient);

            List<DeviceConfig> devices = await _configManager.LoadLocalConfigAsync("devices.json");

            foreach (var device in devices)
            {
                if (stoppingToken.IsCancellationRequested) break;
                await PollDeviceAsync(device);
            }

            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
        }
    }

    private async Task PollDeviceAsync(DeviceConfig device)
    {
        using var tcpClient = new TcpClient();
        try
        {
            tcpClient.ReceiveTimeout = 5000;
            tcpClient.SendTimeout = 5000;

            var connectTask = tcpClient.ConnectAsync(device.Ip, 23);
            if (await Task.WhenAny(connectTask, Task.Delay(5000)) != connectTask)
            {
                Console.WriteLine($"[ОШИБКА] Таймаут подключения к IP {device.Ip}");
                return;
            }

            using var stream = tcpClient.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII);
            using var writer = new StreamWriter(stream, Encoding.ASCII) { AutoFlush = true };

            string prompt = "(Q=Quit) Telnet-->";

            await ReadUntilTimeoutOrTokenAsync(reader, "Enter name:");
            await writer.WriteLineAsync(device.Login);

            await ReadUntilTimeoutOrTokenAsync(reader, "Enter password:");
            await writer.WriteLineAsync(device.Password);

            string response = await ReadUntilTimeoutOrTokenAsync(reader, prompt);
            if (!response.Contains(prompt))
            {
                Console.WriteLine($"[ОШИБКА] Не удалось войти в CLI на {device.Ip}. Неверный логин/пароль");
                return;
            }

            foreach (var param in device.Params)
            {
                await writer.WriteLineAsync(param.Command);

                string rawResult = await ReadUntilTimeoutOrTokenAsync(reader, prompt);
                string cleanedResult = ExtractValueFromResponse(rawResult);
                string finalValue = ProcessValue(cleanedResult, param);

                Console.WriteLine($"IP: {device.Ip} | Команда: {param.Command} | Итог: {finalValue}");

                var logDto = new CreateReadingLogDto
                {
                    IpAddress = device.Ip,
                    ReadingTypeIdentifier = param.Name,
                    Value = finalValue,
                    WriteDateTime = DateTime.UtcNow
                };

                try
                {
                    var apiResponse = await _httpClient.PostAsJsonAsync("api/equipment/logs", logDto);
                    if (!apiResponse.IsSuccessStatusCode)
                    {
                        await _offlineQueue.EnqueueAsync(logDto);
                    }
                    else
                    {
                        // Если связь есть, попутно досылаем старые пакеты из очереди
                        await _offlineQueue.FlushIfAvailableAsync(_httpClient);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[СЕТЬ НЕДОСТУПНА] IP: {device.Ip} | Перевод пакета {param.Name}={finalValue} в локальную очередь ({ex.Message})");
                    await _offlineQueue.EnqueueAsync(logDto);
                }
            }

            await writer.WriteLineAsync("Q");
            Console.WriteLine("------");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ОШИБКА] Исключение при опросе {device.Ip}: {ex.Message}");
        }
    }

    private async Task<string> ReadUntilTimeoutOrTokenAsync(StreamReader reader, string token)
    {
        var sb = new StringBuilder();
        char[] buffer = new char[1];
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        try
        {
            while (!cts.Token.IsCancellationRequested)
            {
                int readCount = await reader.ReadAsync(buffer, 0, 1);
                if (readCount == 0) break;
                sb.Append(buffer[0]);
                if (sb.ToString().Contains(token)) break;
            }
        }
        catch (ObjectDisposedException) { }
        catch (IOException) { }

        return sb.ToString();
    }

    private string ExtractValueFromResponse(string rawResponse)
    {
        if (string.IsNullOrEmpty(rawResponse)) return string.Empty;
        int equalSignIndex = rawResponse.IndexOf('=');
        if (equalSignIndex == -1) return rawResponse.Trim();

        string valuePart = rawResponse.Substring(equalSignIndex + 1);
        string[] lines = valuePart.Split(new[] { "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        return lines.Length > 0 ? lines[0].Trim() : valuePart.Trim();
    }

    private string ProcessValue(string value, ParamConfig param)
    {
        if (param.Uncorrect != null && value.Equals(param.Uncorrect, StringComparison.OrdinalIgnoreCase))
            return "n/a";

        if (param.Annotations != null && param.Annotations.Count > 0)
        {
            if (param.Annotations.TryGetValue(value, out var interpretedValue))
                return interpretedValue;
            return "n/a";
        }

        return value;
    }
}