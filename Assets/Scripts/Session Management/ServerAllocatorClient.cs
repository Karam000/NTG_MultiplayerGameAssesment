using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Services.CloudCode;
using UnityEngine;
using UnityEngine.Networking;

namespace NTG
{
    // Calls the NTGServerAllocator Cloud Code module: requests a fresh Edgegap
    // deployment, polls its status client-side, and stops it after the match.
    public static class ServerAllocatorClient
    {
        private const string ModuleName = "NTGMultiplayer_ServerAllocator";

        public static string LastRequestId { get; private set; }

        [Serializable]
        private class RequestResponse
        {
            public string requestId;
        }

        [Serializable]
        private class StatusResponse
        {
            public bool running;
            public string errorDetail;
            public string ip;
            public string fqdn;
            public int port;
            public string requestId;
        }

        [Serializable]
        private class StopResponse
        {
            public bool stopped;
        }

        // Creates a deployment and polls until ready (client-side polling,
        // so no Cloud Code execution is held open).
        public static async Task<(string address, ushort port)> AllocateAsync()
        {
            string playerIp = await GetPublicIp();

            var reqRes = await CloudCodeService.Instance.CallModuleEndpointAsync<RequestResponse>(
                ModuleName, "Request", new Dictionary<string, object> { { "playerIp", playerIp } });
            LastRequestId = reqRes.requestId;
            Debug.Log($"[ALLOC] Deployment requested: {LastRequestId}");

            var statusArgs = new Dictionary<string, object> { { "requestId", LastRequestId } };
            for (int i = 0; i < 30; i++) // ~90s worst case
            {
                await Task.Delay(3000);

                var st = await CloudCodeService.Instance.CallModuleEndpointAsync<StatusResponse>(
                    ModuleName, "Status", statusArgs);

                if (!string.IsNullOrEmpty(st.errorDetail))
                    throw new Exception($"Deployment error: {st.errorDetail}");

                if (st.running)
                {
                    string address = !string.IsNullOrEmpty(st.fqdn) ? st.fqdn : st.ip;
                    Debug.Log($"[ALLOC] Deployment ready -> {address}:{st.port}");
                    return (address, (ushort)st.port);
                }
            }

            throw new Exception($"Deployment {LastRequestId} timed out (90s)");
        }

        // LAN fallback: points clients at a locally running Windows dedicated server
        // (leader's machine, UDP 7777). No Cloud Code / Edgegap involved.
        public static async Task<(string address, ushort port)> AllocateLocalAsync()
        {
            string ip = "127.0.0.1";
            try
            {
                foreach (var addr in System.Net.Dns.GetHostAddresses(System.Net.Dns.GetHostName()))
                    if (addr.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    {
                        ip = addr.ToString();
                        break;
                    }
            }
            catch { }

            Debug.LogWarning($"[ALLOC] Local server mode -> {ip}:7777 (expecting a local dedicated server)");
            await Task.Delay(300); // simulate allocation latency
            return (ip, 7777);
        }

        // Stops the last requested deployment. Call when the match is finished.
        public static async Task StopLastDeployment()
        {
            if (string.IsNullOrEmpty(LastRequestId)) return;
            try
            {
                var res = await CloudCodeService.Instance.CallModuleEndpointAsync<StopResponse>(
                    ModuleName, "Stop", new Dictionary<string, object> { { "requestId", LastRequestId } });
                Debug.Log($"[ALLOC] Deployment {LastRequestId} stop: {res.stopped}");
                LastRequestId = null;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ALLOC] Stop failed: {e.Message}");
            }
        }

        // Edgegap uses the caller's public IP to pick a nearby location.
        private static async Task<string> GetPublicIp()
        {
            try
            {
                using var req = UnityWebRequest.Get("https://api.ipify.org");
                req.timeout = 3;
                var op = req.SendWebRequest();
                while (!op.isDone) await Task.Yield();
                if (req.result == UnityWebRequest.Result.Success)
                    return req.downloadHandler.text.Trim();
            }
            catch { }
            return "0.0.0.0";
        }
    }
}
