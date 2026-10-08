using System.Threading.Tasks;
using UnityEngine;

namespace NTG
{
    // Step 6 replaces the stub body with a real Cloud Code call that creates
    // an Edgegap deployment and returns its public endpoint.
    public static class ServerAllocator
    {
        public static async Task<(string ip, ushort port)> AllocateAsync()
        {
            // LAN fallback so physical devices can reach the local test server too
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

            Debug.LogWarning($"[ALLOC] Stub endpoint {ip}:7777 (LAN fallback; Cloud Code module not deployed yet)");
            await Task.Delay(500); // simulate request latency
            return (ip, 7777);
        }
    }
}
