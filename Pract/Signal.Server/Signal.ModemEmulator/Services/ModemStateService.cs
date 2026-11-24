using System.Globalization;
using Signal.ModemEmulator.Models;

namespace Signal.ModemEmulator.Services
{
    public enum ModemSimulationMode
    {
        Normal,
        NoLock,
        Warning,
        Overheat
    }

    public class ModemStateService
    {
        private readonly object _sync = new();
        private readonly Random _random = new();
        private readonly Dictionary<string, string> _manualOverrides = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _staticMetrics = new(StringComparer.OrdinalIgnoreCase);

        private double _ebn = 12.4;
        private double _rsl = -47.6;
        private double _tmp = 30.0;
        private double _txp = -10.0;
        private double _berMantissa = 1.4;

        public string ModemAddress { get; }
        public bool AutoDrift { get; set; }
        public ModemSimulationMode Mode { get; private set; } = ModemSimulationMode.Normal;

        public ModemStateService(ModemConfig config)
        {
            ModemAddress = string.IsNullOrWhiteSpace(config.ModemAddress) ? "0000" : config.ModemAddress.Trim();
            AutoDrift = config.AutoDrift;

            // Базовые параметры модема Comtech (соответствуют reading_type из pract_db.sql + доп. команды)
            _staticMetrics["TXO"] = "1";
            _staticMetrics["TTC"] = "HDB3";
            _staticMetrics["FLT"] = "00000000";
            _staticMetrics["LKS"] = "1";
            _staticMetrics["TXF"] = "14250.000";
            _staticMetrics["RXF"] = "11450.000";
            _staticMetrics["SNM"] = "CDM-EMU-0001";
            _staticMetrics["CID"] = "Comtech CDM-625 Emulator";

            if (config.CustomMetrics != null)
            {
                foreach (var kv in config.CustomMetrics)
                {
                    if (!string.IsNullOrWhiteSpace(kv.Key))
                        _manualOverrides[kv.Key.Trim().ToUpperInvariant()] = kv.Value;
                }
            }
        }

        public void SetMode(ModemSimulationMode mode)
        {
            lock (_sync)
            {
                Mode = mode;
                _manualOverrides.Clear();

                switch (mode)
                {
                    case ModemSimulationMode.Normal:
                        _ebn = 12.4;
                        _rsl = -47.6;
                        _tmp = 30.0;
                        _txp = -10.0;
                        _berMantissa = 1.4;
                        _staticMetrics["TXO"] = "1";
                        _staticMetrics["TTC"] = "HDB3";
                        _staticMetrics["FLT"] = "00000000";
                        _staticMetrics["LKS"] = "1";
                        break;

                    case ModemSimulationMode.NoLock:
                        // Состояние без захвата несущей (как на скриншоте: >0000/EBN=99.9)
                        _ebn = 99.9;
                        _rsl = -47.8;
                        _tmp = 30.0;
                        _staticMetrics["TXO"] = "0";
                        _staticMetrics["FLT"] = "00010000";
                        _staticMetrics["LKS"] = "0";
                        break;

                    case ModemSimulationMode.Warning:
                        _ebn = 5.2;
                        _rsl = -62.5;
                        _tmp = 34.0;
                        _staticMetrics["TXO"] = "1";
                        _staticMetrics["FLT"] = "00000100";
                        _staticMetrics["LKS"] = "1";
                        break;

                    case ModemSimulationMode.Overheat:
                        _ebn = 8.1;
                        _rsl = -51.2;
                        _tmp = 39.0;
                        _staticMetrics["TXO"] = "0";
                        _staticMetrics["FLT"] = "10000000";
                        _staticMetrics["LKS"] = "1";
                        break;
                }
            }
        }

        public void SetMetric(string commandCode, string value)
        {
            lock (_sync)
            {
                var key = commandCode.Trim().ToUpperInvariant();
                _manualOverrides[key] = value.Trim();
            }
        }

        public void ClearOverride(string commandCode)
        {
            lock (_sync)
            {
                _manualOverrides.Remove(commandCode.Trim().ToUpperInvariant());
            }
        }

        public string HandleComtechCommand(string rawCommand)
        {
            var trimmed = rawCommand.Trim();
            if (string.IsNullOrEmpty(trimmed))
                return string.Empty;

            // Поддерживаем как полный формат "<0000/EBN?", так и сокращённый "0000/EBN?" или "EBN?"
            var cleaned = trimmed.TrimStart('<', '>');
            string address = ModemAddress;
            string body = cleaned;

            int slashIdx = cleaned.IndexOf('/');
            if (slashIdx > 0)
            {
                address = cleaned[..slashIdx].Trim();
                body = cleaned[(slashIdx + 1)..].Trim();
            }

            // Команда записи вида <0000/EBN=14.2
            int eqIdx = body.IndexOf('=');
            if (eqIdx > 0)
            {
                string cmd = body[..eqIdx].Trim().ToUpperInvariant();
                string newVal = body[(eqIdx + 1)..].Trim();
                SetMetric(cmd, newVal);
                return $">{address}/{cmd}={newVal}";
            }

            // Команда чтения вида <0000/EBN?
            string queryCmd = body.TrimEnd('?').Trim().ToUpperInvariant();
            string value = GetMetricValue(queryCmd);
            return $">{address}/{queryCmd}={value}";
        }

        public string GetMetricValue(string commandCode)
        {
            lock (_sync)
            {
                var key = commandCode.Trim().ToUpperInvariant();

                if (_manualOverrides.TryGetValue(key, out var overrideVal))
                    return overrideVal;

                if (AutoDrift)
                    ApplySmallDriftLocked();

                return key switch
                {
                    "EBN" => Mode == ModemSimulationMode.NoLock
                        ? "99.9"
                        : _ebn.ToString("0.0", CultureInfo.InvariantCulture),

                    "BER" => Mode == ModemSimulationMode.NoLock
                        ? "99.9"
                        : $"{_berMantissa.ToString("0.0", CultureInfo.InvariantCulture)}E-8",

                    "TMP" => FormatSignedTemp(_tmp),

                    "RSL" => _rsl.ToString("0.0", CultureInfo.InvariantCulture),

                    "TXP" => _txp.ToString("0.0", CultureInfo.InvariantCulture),

                    _ when _staticMetrics.TryGetValue(key, out var staticVal) => staticVal,

                    _ => "n/a"
                };
            }
        }

        public Dictionary<string, string> GetSnapshot()
        {
            var keys = new[] { "EBN", "BER", "TMP", "RSL", "TXO", "TTC", "LKS", "FLT", "TXP", "TXF", "RXF" };
            var snapshot = new Dictionary<string, string>();

            lock (_sync)
            {
                bool prevDrift = AutoDrift;
                AutoDrift = false;
                try
                {
                    foreach (var key in keys)
                        snapshot[key] = GetMetricValue(key);

                    foreach (var extra in _manualOverrides)
                    {
                        if (!snapshot.ContainsKey(extra.Key))
                            snapshot[extra.Key] = extra.Value;
                    }
                }
                finally
                {
                    AutoDrift = prevDrift;
                }
            }

            return snapshot;
        }

        private void ApplySmallDriftLocked()
        {
            switch (Mode)
            {
                case ModemSimulationMode.Normal:
                    _ebn = Math.Clamp(_ebn + NextDelta(-0.2, 0.2), 11.2, 13.8);
                    _rsl = Math.Clamp(_rsl + NextDelta(-0.2, 0.2), -48.5, -46.8);
                    _tmp = Math.Clamp(_tmp + NextDelta(-0.35, 0.35), 28.5, 31.5);
                    _berMantissa = Math.Clamp(_berMantissa + NextDelta(-0.2, 0.2), 1.0, 3.5);
                    break;

                case ModemSimulationMode.NoLock:
                    _rsl = Math.Clamp(_rsl + NextDelta(-0.2, 0.2), -48.5, -47.0);
                    _tmp = Math.Clamp(_tmp + NextDelta(-0.25, 0.25), 29.0, 31.5);
                    break;

                case ModemSimulationMode.Warning:
                    _ebn = Math.Clamp(_ebn + NextDelta(-0.3, 0.3), 4.2, 6.2);
                    _rsl = Math.Clamp(_rsl + NextDelta(-0.4, 0.4), -64.0, -61.0);
                    _tmp = Math.Clamp(_tmp + NextDelta(-0.3, 0.3), 33.2, 35.0);
                    break;

                case ModemSimulationMode.Overheat:
                    _ebn = Math.Clamp(_ebn + NextDelta(-0.3, 0.3), 7.0, 9.5);
                    _rsl = Math.Clamp(_rsl + NextDelta(-0.3, 0.3), -52.5, -50.0);
                    _tmp = Math.Clamp(_tmp + NextDelta(-0.3, 0.5), 37.5, 42.0);
                    break;
            }
        }

        private double NextDelta(double min, double max)
            => min + _random.NextDouble() * (max - min);

        private static string FormatSignedTemp(double temp)
        {
            int rounded = (int)Math.Round(temp);
            return rounded >= 0
                ? $"+{rounded.ToString(CultureInfo.InvariantCulture)}"
                : rounded.ToString(CultureInfo.InvariantCulture);
        }
    }
}