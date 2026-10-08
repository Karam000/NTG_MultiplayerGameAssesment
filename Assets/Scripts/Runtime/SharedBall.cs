using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace NTG
{
    public enum BallState : byte
    {
        OnGround = 0,
        Carried = 1,
        Thrown = 2
    }

    public class SharedBall : NetworkBehaviour
    {
        public static SharedBall Instance { get; private set; }
        public static event System.Action<string> OnAnnouncement;

        private const ulong NoPossessor = ulong.MaxValue;

        [SerializeField] private float throwSpeed = 11f;
        [SerializeField] private float throwUpward = 4f;   // arc: initial upward velocity
        [SerializeField] private float gravity = 20f;      // gamey gravity for the arc
        [SerializeField] private float hitRadius = 1.2f;   // horizontal hit tolerance
        [SerializeField] private float hitHeightTolerance = 1.5f; // vertical tolerance
        [SerializeField] private float throwCooldown = 1.5f;
        [SerializeField] private float carrierSpeedMultiplier = 0.6f;
        [SerializeField] private float maxFlightTime = 3f; // safety net

        public NetworkVariable<BallState> State = new NetworkVariable<BallState>(
            BallState.OnGround, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public NetworkVariable<ulong> PossessorClientId = new NetworkVariable<ulong>(
            NoPossessor, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private static readonly Color[] StateColors =
        {
            Color.white,                 // OnGround
            new Color(1f, 0.5f, 0.1f),   // Carried - orange
            Color.red                    // Thrown
        };

        private Vector3 _velocity;
        private ulong _throwerId = NoPossessor;
        private float _flightTimer;
        private float _closestApproach;
        private ulong _closestClient;
        private readonly Dictionary<ulong, float> _lastThrowTime = new Dictionary<ulong, float>();
        private Renderer _renderer;

        public override void OnNetworkSpawn()
        {
            Instance = this;
            _renderer = GetComponentInChildren<Renderer>();
            State.OnValueChanged += (_, __) => ApplyColor();
            ApplyColor();
        }

        public override void OnNetworkDespawn()
        {
            if (Instance == this) Instance = null;
        }

        private void ApplyColor()
        {
            if (_renderer == null) return;
            _renderer.material.color = StateColors[(int)State.Value % StateColors.Length];
        }

        private void Update()
        {
            if (!IsServer) return;

            if (State.Value == BallState.Carried)
            {
                if (!TryGetPlayerPos(PossessorClientId.Value, out Vector3 pos))
                {
                    Land(); // possessor disconnected -> ball is recoverable
                    return;
                }
                transform.position = pos + Vector3.up * 1.2f;
            }
            else if (State.Value == BallState.Thrown)
            {
                // parabolic flight, simulated only on the server
                _velocity.y -= gravity * Time.deltaTime;
                transform.position += _velocity * Time.deltaTime;
                _flightTimer += Time.deltaTime;

                if (TryFindVictim(out ulong victim))
                {
                    Eliminate(victim);
                    Land();
                    return;
                }

                if ((transform.position.y <= 0.3f && _velocity.y < 0f) || _flightTimer >= maxFlightTime)
                    Land();
            }
        }

        [Rpc(SendTo.Server, RequireOwnership = false)]
        public void PickupServerRpc(RpcParams rpcParams = default)
        {
            ulong sender = rpcParams.Receive.SenderClientId;

            if (!MatchManager.IsPlaying) return;
            if (State.Value != BallState.OnGround) return;                    // possession race: first request wins
            if (PlayerObject.IsClientEliminated(sender)) return;              // player state check
            if (!TryGetPlayerPos(sender, out Vector3 pos)) return;
            if (Vector3.Distance(pos, transform.position) > ObjectiveObject.InteractRange) return;

            State.Value = BallState.Carried;
            PossessorClientId.Value = sender;
            SetSpeedMultiplier(sender, carrierSpeedMultiplier);               // movement restriction, server-enforced
            Debug.Log($"[SERVER] Ball picked up by client {sender}");
            AnnounceClientRpc($"Client {sender} has the BALL");
        }

        [Rpc(SendTo.Server, RequireOwnership = false)]
        public void ThrowServerRpc(Vector2 direction, RpcParams rpcParams = default)
        {
            ulong sender = rpcParams.Receive.SenderClientId;

            if (!MatchManager.IsPlaying) return;
            if (State.Value != BallState.Carried || PossessorClientId.Value != sender)
            {
                Debug.Log($"[SERVER] Throw rejected for client {sender}: not the possessor (state={State.Value}, possessor={PossessorClientId.Value})");
                return;
            }
            if (PlayerObject.IsClientEliminated(sender))
            {
                Debug.Log($"[SERVER] Throw rejected for client {sender}: eliminated");
                return;
            }
            if (_lastThrowTime.TryGetValue(sender, out float t) && Time.time - t < throwCooldown)
            {
                Debug.Log($"[SERVER] Throw rejected for client {sender}: cooldown ({Time.time - t:F2}s)");
                return;
            }
            if (direction.sqrMagnitude < 0.01f)
            {
                Debug.Log($"[SERVER] Throw rejected for client {sender}: zero direction");
                return;
            }

            _lastThrowTime[sender] = Time.time;
            _throwerId = sender;
            _flightTimer = 0f;
            _closestApproach = float.MaxValue;

            Vector3 dir = new Vector3(direction.x, 0f, direction.y).normalized;
            _velocity = dir * throwSpeed + Vector3.up * throwUpward;
            if (TryGetPlayerPos(sender, out Vector3 pos))
                transform.position = pos + Vector3.up * 0.8f;

            State.Value = BallState.Thrown;
            PossessorClientId.Value = NoPossessor;
            SetSpeedMultiplier(sender, 1f);
            Debug.Log($"[SERVER] Client {sender} threw the ball from {transform.position} dir {dir}");
            AnnounceClientRpc($"Client {sender} threw the ball!");
        }

        private bool TryFindVictim(out ulong victim)
        {
            victim = 0;
            var nm = NetworkManager.Singleton;

            int throwerTeam = -1;
            if (nm.ConnectedClients.TryGetValue(_throwerId, out var throwerClient) &&
                throwerClient.PlayerObject != null)
            {
                var thrower = throwerClient.PlayerObject.GetComponent<PlayerObject>();
                if (thrower != null) throwerTeam = thrower.TeamIndex.Value;
            }

            Vector3 ballPos = transform.position;

            foreach (var kv in nm.ConnectedClients)
            {
                ulong id = kv.Key;
                if (id == _throwerId) continue;

                var po = kv.Value.PlayerObject;
                if (po == null) continue;

                var player = po.GetComponent<PlayerObject>();
                if (player == null || player.IsEliminated.Value) continue;
                if (player.TeamIndex.Value == throwerTeam) continue; // teammates immune

                // horizontal ring + vertical tolerance (ball arcs over y, player pivot is capsule center)
                Vector3 pp = po.transform.position;
                float dx = pp.x - ballPos.x;
                float dz = pp.z - ballPos.z;
                float horizontal = Mathf.Sqrt(dx * dx + dz * dz);

                if (horizontal < _closestApproach)
                {
                    _closestApproach = horizontal;
                    _closestClient = id;
                }

                if (horizontal <= hitRadius && Mathf.Abs(pp.y - ballPos.y) <= hitHeightTolerance)
                {
                    victim = id;
                    return true;
                }
            }
            return false;
        }

        private void Eliminate(ulong clientId)
        {
            var po = NetworkManager.Singleton.ConnectedClients[clientId].PlayerObject;
            po.GetComponent<PlayerObject>().IsEliminated.Value = true;
            po.GetComponent<PlayerMovement>().ServerSpeedMultiplier = 0f;

            // eliminated player drops any carried objective
            foreach (var obj in ObjectiveObject.All)
                if (obj.CarrierClientId.Value == clientId)
                    obj.ServerDrop();

            Debug.Log($"[SERVER] Client {clientId} ELIMINATED by ball");
            AnnounceClientRpc($"Client {clientId} was ELIMINATED!");
            if (MatchManager.Instance != null)
                MatchManager.Instance.NotifyElimination();
        }

        private void Land()
        {
            if (_closestApproach < float.MaxValue)
                Debug.Log($"[SERVER] Ball landed at {transform.position} - closest approach: {_closestApproach:F2} to client {_closestClient}");

            State.Value = BallState.OnGround;
            PossessorClientId.Value = NoPossessor;
            _throwerId = NoPossessor;
            _velocity = Vector3.zero;
            transform.position = new Vector3(transform.position.x, 0.3f, transform.position.z);
        }

        private static void SetSpeedMultiplier(ulong clientId, float mult)
        {
            var nm = NetworkManager.Singleton;
            if (!nm.ConnectedClients.TryGetValue(clientId, out var client) || client.PlayerObject == null) return;
            var pm = client.PlayerObject.GetComponent<PlayerMovement>();
            if (pm != null) pm.ServerSpeedMultiplier = mult;
        }

        private static bool TryGetPlayerPos(ulong clientId, out Vector3 pos)
        {
            pos = default;
            var nm = NetworkManager.Singleton;
            if (nm == null) return false;
            if (!nm.ConnectedClients.TryGetValue(clientId, out var client)) return false;
            if (client.PlayerObject == null) return false;
            pos = client.PlayerObject.transform.position;
            return true;
        }

        [Rpc(SendTo.NotServer)]
        private void AnnounceClientRpc(string msg)
        {
            OnAnnouncement?.Invoke(msg);
        }
    }
}
