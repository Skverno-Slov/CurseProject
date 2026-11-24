using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Signal.ModemEmulator.Models
{
    public class ModemConfig
    {
        [JsonPropertyName("listenIp")]
        public string ListenIp { get; set; } = "0.0.0.0";

        [JsonPropertyName("port")]
        public int Port { get; set; } = 23;

        [JsonPropertyName("modemAddress")]
        public string ModemAddress { get; set; } = "0000";

        [JsonPropertyName("login")]
        public string Login { get; set; } = "comtech";

        [JsonPropertyName("password")]
        public string Password { get; set; } = "comtech";

        [JsonPropertyName("echoInput")]
        public bool EchoInput { get; set; } = true;

        [JsonPropertyName("autoDrift")]
        public bool AutoDrift { get; set; } = true;

        [JsonPropertyName("customMetrics")]
        public Dictionary<string, string> CustomMetrics { get; set; } = new();
    }
}