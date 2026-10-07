using System.Collections;
using Unity.Netcode;
using UnityEngine;

namespace NTG
{
    public enum MatchState
    {
        WaitingForPlayers = 0,
        Starting = 1,
        Playing = 2,
        Finished = 3
    }

    public class MatchManager : NetworkBehaviour
    {
        public static MatchManager Instance { get; private set; }

        [SerializeField] private int minPlayers = 2;
        [SerializeField] private float countdownSeconds = 3f;

        public NetworkVariable<MatchState> State = new NetworkVariable<MatchState>(
            MatchState.WaitingForPlayers, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private Coroutine _countdown;

        private void Awake()
        {
            Instance = this;
        }

        public override void OnNetworkSpawn()
        {
            Instance = this;
            if (!IsServer) return;
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientCountChanged;
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientCountChanged;
            EvaluateStartConditions();
        }

        public override void OnNetworkDespawn()
        {
            if (Instance == this) Instance = null;
            if (!IsServer) return;
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientCountChanged;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientCountChanged;
        }

        private void OnClientCountChanged(ulong _)
        {
            EvaluateStartConditions();
        }

        private void EvaluateStartConditions()
        {
            int players = NetworkManager.Singleton.ConnectedClients.Count;

            if (State.Value == MatchState.WaitingForPlayers && players >= minPlayers)
            {
                State.Value = MatchState.Starting;
                _countdown = StartCoroutine(StartCountdown());
                Debug.Log($"[SERVER] {players} players, match starting in {countdownSeconds}s");
            }
            else if (State.Value == MatchState.Starting && players < minPlayers)
            {
                if (_countdown != null) StopCoroutine(_countdown);
                State.Value = MatchState.WaitingForPlayers;
                Debug.Log("[SERVER] Player lost during countdown, back to WaitingForPlayers");
            }
        }

        private IEnumerator StartCountdown()
        {
            yield return new WaitForSeconds(countdownSeconds);
            State.Value = MatchState.Playing;
            Debug.Log("[SERVER] Match state: Playing");
        }

        // Server-side team balancing. The not-yet-assigned player (TeamIndex -1) is ignored.
        public int ServerAssignTeam()
        {
            int a = 0, b = 0;
            foreach (var kv in NetworkManager.Singleton.ConnectedClients)
            {
                var po = kv.Value.PlayerObject;
                if (po == null) continue;
                var p = po.GetComponent<PlayerObject>();
                if (p == null) continue;
                if (p.TeamIndex.Value == 0) a++;
                else if (p.TeamIndex.Value == 1) b++;
            }
            return a <= b ? 0 : 1;
        }
    }
}
