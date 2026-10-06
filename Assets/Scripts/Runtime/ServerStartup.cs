using System;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NTG
{
    /// <summary>
    ///
    /// </summary>
    public class ServerStartup : MonoBehaviour
    {
        public const string GameSceneName = "Game";

        [SerializeField] private ushort defaultPort = 7777;

        private void Awake()
        {
            DontDestroyOnLoad(gameObject);
        }

        private void Start()
        {
            Application.targetFrameRate = 60;

            ushort port = GetPort();
            var transport = (UnityTransport)NetworkManager.Singleton.NetworkConfig.NetworkTransport;
            transport.SetConnectionData("0.0.0.0", port);

            NetworkManager.Singleton.OnServerStarted += OnServerStarted;

            if (!NetworkManager.Singleton.StartServer())
                Debug.LogError("[SERVER] StartServer() failed");
        }

        private void OnServerStarted()
        {
            Debug.Log($"[SERVER] Listening on 0.0.0.0:{GetPort()} (udp)");
            NetworkManager.Singleton.SceneManager.LoadScene(GameSceneName, LoadSceneMode.Single);
        }

        private ushort GetPort()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-port" && ushort.TryParse(args[i + 1], out ushort cliPort))
                    return cliPort;

            string env = Environment.GetEnvironmentVariable("PORT");
            if (!string.IsNullOrEmpty(env) && ushort.TryParse(env, out ushort envPort))
                return envPort;

            return defaultPort;
        }
    }
}
