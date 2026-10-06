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
            Debug.LogWarning("[ALLOC] Stub endpoint 127.0.0.1:7777 (Cloud Code module not deployed yet)");
            await Task.Delay(500); // simulate request latency
            return ("127.0.0.1", 7777);
        }
    }
}
