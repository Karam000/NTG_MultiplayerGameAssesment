using System.Collections;
using Unity.Netcode;
using UnityEngine;

namespace NTG
{
    public enum MatchState : byte
    {
        WaitingForPlayers = 0,
        Starting = 1,
        Playing = 2,
        Finished = 3
    }

    public class MatchManager : NetworkBehaviour
    {
        public static MatchManager Instance { get; private set; }
        public static event System.Action<string> OnAnnouncement;

        [SerializeField] private int minPlayers = 2;
        [SerializeField] private float countdownSeconds = 3f;
        [SerializeField] private GameObject objectivePrefab;
        [SerializeField] private GameObject ballPrefab;
        [SerializeField] private int objectiveCount = 4;
        [SerializeField] private int objectivesToWin = 2;

        public NetworkVariable<MatchState> State = new NetworkVariable<MatchState>(
            MatchState.WaitingForPlayers, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public NetworkVariable<int> WinningTeam = new NetworkVariable<int>(
            -1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private Coroutine _countdown;
        private bool _objectivesSpawned;
        private int _completedCount;

        public static bool IsPlaying => Instance != null && Instance.State.Value == MatchState.Playing;
        public static bool IsFinished => Instance != null && Instance.State.Value == MatchState.Finished;

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
            EvaluateEliminationVictory(); // disconnects may decide the match (spec 12)
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
            SpawnObjectives();
        }

        private void SpawnObjectives()
        {
            if (_objectivesSpawned || objectivePrefab == null) return;
            _objectivesSpawned = true;

            for (int i = 0; i < objectiveCount; i++)
            {
                float angle = i * Mathf.PI * 2f / objectiveCount;
                Vector3 pos = new Vector3(Mathf.Sin(angle), 0.3f, Mathf.Cos(angle)) * 6f;
                var go = Instantiate(objectivePrefab, pos, Quaternion.identity);
                go.GetComponent<NetworkObject>().Spawn();
                go.GetComponent<ObjectiveObject>().ObjectiveIndex.Value = i;
            }
            Debug.Log($"[SERVER] Spawned {objectiveCount} objectives");

            if (ballPrefab != null)
            {
                var ball = Instantiate(ballPrefab, new Vector3(0f, 0.3f, 0f), Quaternion.identity);
                ball.GetComponent<NetworkObject>().Spawn();
                Debug.Log("[SERVER] Spawned the shared ball");
            }
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

        // ---------- victory conditions (spec 11) ----------

        // Condition A: a required number of objectives completed.
        public void NotifyObjectiveCompleted(int objectiveIndex, ulong clientId)
        {
            if (State.Value != MatchState.Playing) return;

            _completedCount++;
            Debug.Log($"[SERVER] Objective {objectiveIndex} completed ({_completedCount}/{objectivesToWin})");

            if (_completedCount >= objectivesToWin)
                FinishMatch(GetTeamOf(clientId), "objectives completed");
        }

        public void NotifyElimination()
        {
            EvaluateEliminationVictory();
        }

        // Condition B: all active players on the opposing team eliminated (or gone).
        private void EvaluateEliminationVictory()
        {
            if (State.Value != MatchState.Playing) return;

            int aliveA = 0, aliveB = 0, totalA = 0, totalB = 0;
            foreach (var kv in NetworkManager.Singleton.ConnectedClients)
            {
                var po = kv.Value.PlayerObject;
                if (po == null) continue;
                var p = po.GetComponent<PlayerObject>();
                if (p == null || p.TeamIndex.Value < 0) continue;

                if (p.TeamIndex.Value == 0) { totalA++; if (!p.IsEliminated.Value) aliveA++; }
                else { totalB++; if (!p.IsEliminated.Value) aliveB++; }
            }

            // server determines which condition occurred first
            if (totalB > 0 && aliveB == 0) FinishMatch(0, "opposing team eliminated");
            else if (totalA > 0 && aliveA == 0) FinishMatch(1, "opposing team eliminated");
            else if (totalA > 0 && totalB == 0) FinishMatch(0, "opposing team left");
            else if (totalB > 0 && totalA == 0) FinishMatch(1, "opposing team left");
        }

        private void FinishMatch(int team, string reason)
        {
            State.Value = MatchState.Finished;
            WinningTeam.Value = team;
            Debug.Log($"[SERVER] MATCH FINISHED - Team {(team == 0 ? "A" : "B")} wins ({reason})");
            AnnounceClientRpc($"Team {(team == 0 ? "A" : "B")} WINS! ({reason})");
        }

        private int GetTeamOf(ulong clientId)
        {
            if (NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var c) &&
                c.PlayerObject != null)
            {
                var p = c.PlayerObject.GetComponent<PlayerObject>();
                if (p != null) return p.TeamIndex.Value;
            }
            return 0;
        }

        [Rpc(SendTo.NotServer)]
        private void AnnounceClientRpc(string msg)
        {
            OnAnnouncement?.Invoke(msg);
        }
    }
}
