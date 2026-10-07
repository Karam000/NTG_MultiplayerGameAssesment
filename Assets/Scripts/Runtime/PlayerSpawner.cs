using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NTG
{
    public class PlayerSpawner : MonoBehaviour
    {
        [SerializeField] private GameObject playerPrefab;
        [SerializeField] private GameObject matchManagerPrefab;

        private void EnsureMatchManager()
        {
            if (MatchManager.Instance != null) return;
            var go = Instantiate(matchManagerPrefab);
            go.GetComponent<NetworkObject>().Spawn();
            Debug.Log("[SERVER] MatchManager spawned");
        }

        private void Start()
        {
            // NetworkManager.SceneManager only exists after StartServer() -> subscribe post-startup.
            NetworkManager.Singleton.OnServerStarted += OnServerStarted;
        }

        private void OnServerStarted()
        {
            var nm = NetworkManager.Singleton;
            nm.SceneManager.OnLoadEventCompleted += OnLoadEventCompleted;
            nm.OnClientConnectedCallback += OnClientConnected;
        }

        private void OnDestroy()
        {
            if (NetworkManager.Singleton == null) return;
            NetworkManager.Singleton.OnServerStarted -= OnServerStarted;
            if (NetworkManager.Singleton.SceneManager != null)
                NetworkManager.Singleton.SceneManager.OnLoadEventCompleted -= OnLoadEventCompleted;
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
        }

        private void OnLoadEventCompleted(string sceneName, LoadSceneMode mode,
            List<ulong> clientsCompleted, List<ulong> clientsTimedOut)
        {
            if (sceneName != ServerStartup.GameSceneName) return;

            EnsureMatchManager();

            foreach (ulong clientId in NetworkManager.Singleton.ConnectedClientsIds)
                SpawnFor(clientId);
        }

        private void OnClientConnected(ulong clientId)
        {
            // Late joiner while a match is already running.
            if (SceneManager.GetActiveScene().name == ServerStartup.GameSceneName)
                SpawnFor(clientId);
        }

        private void SpawnFor(ulong clientId)
        {
            var nm = NetworkManager.Singleton;
            if (nm.ConnectedClients[clientId].PlayerObject != null) return;

            int index = nm.ConnectedClients.Count - 1;
            Vector3 pos = new Vector3((index % 4) * 2f - 3f, 1f, (index / 4) * 2f - 1f);

            var go = Instantiate(playerPrefab, pos, Quaternion.identity);
            go.GetComponent<NetworkObject>().SpawnAsPlayerObject(clientId);
            int team = MatchManager.Instance.ServerAssignTeam();
            go.GetComponent<PlayerObject>().TeamIndex.Value = team;
            Debug.Log($"[SERVER] Spawned player object for client {clientId}, team {team}");
        }
    }
}
