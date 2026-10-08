using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Unity.Services.CloudCode.Apis;
using Unity.Services.CloudCode.Core;

namespace NTG.ServerAllocator
{
    public class RequestResponse
    {
        [JsonPropertyName("requestId")] public string RequestId { get; set; }
    }

    public class StatusResponse
    {
        [JsonPropertyName("running")] public bool Running { get; set; }
        [JsonPropertyName("errorDetail")] public string ErrorDetail { get; set; }
        [JsonPropertyName("ip")] public string Ip { get; set; }
        [JsonPropertyName("fqdn")] public string Fqdn { get; set; }
        [JsonPropertyName("port")] public int Port { get; set; }
        [JsonPropertyName("requestId")] public string RequestId { get; set; }
    }

    public class StopResponse
    {
        [JsonPropertyName("stopped")] public bool Stopped { get; set; }
    }

    public class ServerAllocatorModule
    {
        // Identify your Edgegap application/version (created once via the Edgegap
        // Unity plugin upload). These are not secrets - but they must match exactly.
        private const string EdgegapAppName = "NTGMultiplayerAssesment";
        private const string EdgegapAppVersion = "NTG_MultiplayerAssesment_V1";
        private const int InternalPort = 7777; // UDP port the server listens on
        private const string SecretName = "EDGEGAP_API_TOKEN";

        [CloudCodeFunction("Request")]
        public async Task<RequestResponse> Request(
            IExecutionContext context, IGameApiClient gameApiClient, string playerIp)
        {
            using var http = await CreateClient(context, gameApiClient);

            var deployBody = new
            {
                application = EdgegapAppName,
                version = EdgegapAppVersion,
                users = new[]
                {
                    new { user_type = "ip_address", user_data = new { ip_address = playerIp } }
                },
                tags = new[] { "ntg-match" }
            };

            var res = await http.PostAsync("/v2/deployments",
                new StringContent(JsonSerializer.Serialize(deployBody), Encoding.UTF8, "application/json"));
            string json = await res.Content.ReadAsStringAsync();
            if (!res.IsSuccessStatusCode)
                throw new Exception($"Edgegap deploy failed: {(int)res.StatusCode} {json}");

            string requestId = JsonDocument.Parse(json).RootElement.GetProperty("request_id").GetString();
            return new RequestResponse { RequestId = requestId };
        }

        [CloudCodeFunction("Status")]
        public async Task<StatusResponse> Status(
            IExecutionContext context, IGameApiClient gameApiClient, string requestId)
        {
            using var http = await CreateClient(context, gameApiClient);

            var res = await http.GetAsync($"/v1/status/{requestId}");
            string json = await res.Content.ReadAsStringAsync();
            if (!res.IsSuccessStatusCode)
                throw new Exception($"Edgegap status failed: {(int)res.StatusCode} {json}");

            var root = JsonDocument.Parse(json).RootElement;

            if (root.TryGetProperty("error", out var errProp) && errProp.GetBoolean())
            {
                string detail = root.TryGetProperty("error_detail", out var d) ? d.GetString() : "unknown";
                return new StatusResponse { Running = false, ErrorDetail = detail, RequestId = requestId };
            }

            if (!root.TryGetProperty("running", out var running) || !running.GetBoolean())
                return new StatusResponse { Running = false, RequestId = requestId };

            // external port mapping for the internal UDP 7777 listener
            int externalPort = 0;
            foreach (var portProp in root.GetProperty("ports").EnumerateObject())
            {
                var port = portProp.Value;
                bool internalMatch = port.TryGetProperty("internal", out var i) && i.GetInt32() == InternalPort;
                bool udpMatch = !port.TryGetProperty("protocol", out var proto) ||
                                string.Equals(proto.GetString(), "UDP", StringComparison.OrdinalIgnoreCase);
                if (internalMatch && udpMatch)
                {
                    externalPort = port.GetProperty("external").GetInt32();
                    break;
                }
            }
            if (externalPort == 0)
                throw new Exception($"Deployment {requestId} is running but no UDP mapping for internal port {InternalPort} was found");

            return new StatusResponse
            {
                Running = true,
                Ip = root.TryGetProperty("public_ip", out var ipProp) ? ipProp.GetString() : null,
                Fqdn = root.TryGetProperty("fqdn", out var fqdnProp) ? fqdnProp.GetString() : null,
                Port = externalPort,
                RequestId = requestId
            };
        }

        [CloudCodeFunction("Stop")]
        public async Task<StopResponse> Stop(
            IExecutionContext context, IGameApiClient gameApiClient, string requestId)
        {
            using var http = await CreateClient(context, gameApiClient);

            var res = await http.DeleteAsync($"/v1/stop/{requestId}");
            // 200/202: stopped (or stopping); 410: already terminated - all fine
            return new StopResponse { Stopped = res.IsSuccessStatusCode || (int)res.StatusCode == 410 };
        }

        private static async Task<HttpClient> CreateClient(IExecutionContext context, IGameApiClient gameApiClient)
        {
            var secret = await gameApiClient.SecretManager.GetSecret(context, SecretName);

            var http = new HttpClient { BaseAddress = new Uri("https://api.edgegap.com") };
            http.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", secret.Value);
            return http;
        }
    }
}

