using System.Net;
using System.Net.Sockets;
using System.Text;
using Signal.ModemEmulator.Models;

namespace Signal.ModemEmulator.Services
{
    public class TelnetModemServer(ModemConfig config, ModemStateService stateService)
    {
        private const string CliPrompt = "(Q=Quit) Telnet-->";
        private readonly ModemConfig _config = config;
        private readonly ModemStateService _stateService = stateService;
        private TcpListener? _listener;

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            var listenAddress = IPAddress.TryParse(_config.ListenIp, out var parsedIp)
                ? parsedIp
                : IPAddress.Any;

            _listener = new TcpListener(listenAddress, _config.Port);
            _listener.Start();

            Console.WriteLine($"[TELNET] Эмулятор спутникового модема запущен на {listenAddress}:{_config.Port}");
            Console.WriteLine($"[TELNET] Адрес модема: {_stateService.ModemAddress} | Логин: {_config.Login} | Пароль: {_config.Password}");

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    var tcpClient = await _listener.AcceptTcpClientAsync(cancellationToken);
                    _ = Task.Run(() => HandleClientAsync(tcpClient, cancellationToken), cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                // Корректное завершение работы сервера
            }
            finally
            {
                _listener.Stop();
            }
        }

        private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
        {
            var remoteEndPoint = client.Client.RemoteEndPoint?.ToString() ?? "unknown";
            Console.WriteLine($"[ПОДКЛЮЧЕНИЕ] Клиент подключился: {remoteEndPoint}");

            try
            {
                using (client)
                using (var stream = client.GetStream())
                {
                    var reader = new TelnetLineReader(stream);

                    // 1. Приветствие (без символа '=', чтобы не мешать парсерам)
                    await WriteAsciiAsync(stream, "Comtech CDM-625 Satellite Modem Emulator\r\n", cancellationToken);

                    // 2. Авторизация (строго по протоколу Signal.agent: "Enter name:" -> "Enter password:")
                    bool authenticated = false;
                    for (int attempt = 0; attempt < 3; attempt++)
                    {
                        await WriteAsciiAsync(stream, "Enter name: ", cancellationToken);
                        var login = await reader.ReadLineAsync(echo: _config.EchoInput, cancellationToken);
                        if (login is null) return;

                        await WriteAsciiAsync(stream, "Enter password: ", cancellationToken);
                        var password = await reader.ReadLineAsync(echo: false, cancellationToken);
                        if (password is null) return;

                        await WriteAsciiAsync(stream, "\r\n", cancellationToken);

                        if (string.Equals(login.Trim(), _config.Login, StringComparison.Ordinal) &&
                            string.Equals(password.Trim(), _config.Password, StringComparison.Ordinal))
                        {
                            authenticated = true;
                            break;
                        }

                        await WriteAsciiAsync(stream, "Invalid login or password\r\n", cancellationToken);
                        Console.WriteLine($"[АВТОРИЗАЦИЯ] Неудачная попытка входа от {remoteEndPoint} (login='{login.Trim()}')");
                    }

                    if (!authenticated)
                    {
                        await WriteAsciiAsync(stream, "Too many failed attempts. Closing connection.\r\n", cancellationToken);
                        return;
                    }

                    Console.WriteLine($"[АВТОРИЗАЦИЯ] Успешный вход: {remoteEndPoint}");

                    // 3. Основной цикл обработки команд Comtech CLI
                    while (!cancellationToken.IsCancellationRequested && client.Connected)
                    {
                        await WriteAsciiAsync(stream, CliPrompt, cancellationToken);
                        var command = await reader.ReadLineAsync(echo: _config.EchoInput, cancellationToken);
                        if (command is null) break;

                        command = command.Trim();
                        if (string.IsNullOrEmpty(command))
                        {
                            await WriteAsciiAsync(stream, "\r\n", cancellationToken);
                            continue;
                        }

                        if (command.Equals("Q", StringComparison.OrdinalIgnoreCase) ||
                            command.Equals("QUIT", StringComparison.OrdinalIgnoreCase) ||
                            command.Equals("EXIT", StringComparison.OrdinalIgnoreCase))
                        {
                            await WriteAsciiAsync(stream, "\r\nSession closed.\r\n", cancellationToken);
                            break;
                        }

                        string reply = _stateService.HandleComtechCommand(command);
                        Console.WriteLine($"[CMD] {remoteEndPoint} | Запрос: {command} -> Ответ: {reply}");

                        // Формат ответа строго как на скриншоте:
                        // (Q=Quit) Telnet--><0000/EBN?
                        // >0000/EBN=99.9
                        await WriteAsciiAsync(stream, $"{reply}\r\n", cancellationToken);
                    }
                }
            }
            catch (IOException)
            {
                // Клиент закрыл соединение
            }
            catch (OperationCanceledException)
            {
                // Остановка сервера
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ОШИБКА] Сессия {remoteEndPoint}: {ex.Message}");
            }
            finally
            {
                Console.WriteLine($"[ОТКЛЮЧЕНИЕ] Клиент отключился: {remoteEndPoint}");
            }
        }

        private static async Task WriteAsciiAsync(NetworkStream stream, string text, CancellationToken cancellationToken)
        {
            byte[] bytes = Encoding.ASCII.GetBytes(text);
            await stream.WriteAsync(bytes, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }

        private sealed class TelnetLineReader(NetworkStream stream)
        {
            private readonly NetworkStream _stream = stream;
            private readonly byte[] _singleByte = new byte[1];
            private bool _skipNextLfOrNul;

            public async Task<string?> ReadLineAsync(bool echo, CancellationToken cancellationToken)
            {
                var sb = new StringBuilder();

                while (!cancellationToken.IsCancellationRequested)
                {
                    int read = await _stream.ReadAsync(_singleByte, 0, 1, cancellationToken);
                    if (read == 0)
                        return sb.Length > 0 ? sb.ToString() : null;

                    byte b = _singleByte[0];

                    if (b == 0xFF)
                    {
                        await SkipTelnetIacAsync(cancellationToken);
                        continue;
                    }

                    if (b == '\r')
                    {
                        _skipNextLfOrNul = true;
                        if (echo)
                            await WriteAsciiAsync(_stream, "\r\n", cancellationToken);
                        return sb.ToString();
                    }

                    if (b == '\n')
                    {
                        if (_skipNextLfOrNul)
                        {
                            _skipNextLfOrNul = false;
                            continue;
                        }

                        if (echo)
                            await WriteAsciiAsync(_stream, "\r\n", cancellationToken);
                        return sb.ToString();
                    }

                    if (b == '\0')
                    {
                        _skipNextLfOrNul = false;
                        continue;
                    }

                    _skipNextLfOrNul = false;

                    if (b == 0x08 || b == 0x7F)
                    {
                        if (sb.Length > 0)
                        {
                            sb.Length--;
                            if (echo)
                                await WriteAsciiAsync(_stream, "\b \b", cancellationToken);
                        }
                        continue;
                    }

                    if (b >= 0x20 && b <= 0x7E)
                    {
                        char ch = (char)b;
                        sb.Append(ch);
                        if (echo)
                            await _stream.WriteAsync(_singleByte, 0, 1, cancellationToken);
                    }
                }

                return null;
            }

            private async Task SkipTelnetIacAsync(CancellationToken cancellationToken)
            {
                int cmdRead = await _stream.ReadAsync(_singleByte, 0, 1, cancellationToken);
                if (cmdRead == 0) return;

                byte cmd = _singleByte[0];

                // WILL (251), WONT (252), DO (253), DONT (254) имеют 1 дополнительный байт опции
                if (cmd is 0xFB or 0xFC or 0xFD or 0xFE)
                {
                    await _stream.ReadAsync(_singleByte, 0, 1, cancellationToken);
                }
                // SB (250) — читаем до IAC SE (255, 240)
                else if (cmd == 0xFA)
                {
                    bool prevWasIac = false;
                    while (!cancellationToken.IsCancellationRequested)
                    {
                        int n = await _stream.ReadAsync(_singleByte, 0, 1, cancellationToken);
                        if (n == 0) break;

                        byte cur = _singleByte[0];
                        if (prevWasIac && cur == 0xF0)
                            break;

                        prevWasIac = cur == 0xFF;
                    }
                }
            }
        }
    }
}