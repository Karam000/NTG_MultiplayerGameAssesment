using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace NTG
{
    public enum ObjectiveState : byte
    {
        Available = 0,
        Carried = 1,
        InProgress = 2,
        Completed = 3
    }

    public class ObjectiveObject : NetworkBehaviour
    {
        public static readonly List<ObjectiveObject> All = new List<ObjectiveObject>();
        public static event System.Action<string> OnAnnouncement;

        public const float InteractRange = 2.5f;
        public const float GoalZoneRadius = 2.5f;
        public const float InteractDuration = 1f;
        public static readonly Vector3 GoalZonePos = new Vector3(0f, 0f, -5f);

        private const ulong NoCarrier = ulong.MaxValue;

        public NetworkVariable<int> ObjectiveIndex = new NetworkVariable<int>(
            -1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public NetworkVariable<ObjectiveState> State = new NetworkVariable<ObjectiveState>(
            ObjectiveState.Available, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public NetworkVariable<ulong> CarrierClientId = new NetworkVariable<ulong>(
            NoCarrier, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public NetworkVariable<float> InteractEndServerTime = new NetworkVariable<float>(
            0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private Coroutine _interactRoutine;
        private Renderer _renderer;

        private static readonly Color[] StateColors =
        {
            new Color(1f, 0.85f, 0.1f), // Available - yellow
            new Color(1f, 0.5f, 0.1f),  // Carried - orange
            new Color(0.2f, 0.9f, 1f),  // InProgress - cyan
            new Color(0.2f, 0.9f, 0.3f) // Completed - green
        };

        public override void OnNetworkSpawn()
        {
            All.Add(this);
            _renderer = GetComponentInChildren<Renderer>();
            State.OnValueChanged += (_, __) => ApplyColor();
            ApplyColor();
        }

        private void ApplyColor()
        {
            if (_renderer == null) return;
            _renderer.material.color = StateColors[(int)State.Value % StateColors.Length];
        }

        public override void OnNetworkDespawn()
        {
            All.Remove(this);
        }

        private void Update()
        {
            if (!IsServer) return;
            if (State.Value != ObjectiveState.Carried && State.Value != ObjectiveState.InProgress) return;

            if (!TryGetCarrierPos(out Vector3 carrierPos))
            {
                // carrier disconnected -> drop where it is, back to Available (G5 formalizes this)
                ServerResetToAvailable();
                return;
            }

            if (State.Value == ObjectiveState.Carried)
                transform.position = carrierPos + Vector3.up * 1.2f; // just above the head
        }

        // ---------- server RPCs ----------

        [Rpc(SendTo.Server, RequireOwnership = false)]
        public void PickupServerRpc(RpcParams rpcParams = default)
        {
            ulong sender = rpcParams.Receive.SenderClientId;

            // race resolution: first validated request wins, everyone else sees State != Available
            if (State.Value != ObjectiveState.Available) return;
            if (IsCarryingAnything(sender)) return;
            if (!TryGetPlayerPos(sender, out Vector3 playerPos)) return;
            if (Vector3.Distance(playerPos, transform.position) > InteractRange) return;

            State.Value = ObjectiveState.Carried;
            CarrierClientId.Value = sender;
            Debug.Log($"[SERVER] Objective {ObjectiveIndex.Value} picked up by client {sender}");
            AnnounceClientRpc($"Objective {ObjectiveIndex.Value} picked up by client {sender}");
        }

        [Rpc(SendTo.Server, RequireOwnership = false)]
        public void InteractServerRpc(RpcParams rpcParams = default)
        {
            ulong sender = rpcParams.Receive.SenderClientId;

            if (State.Value != ObjectiveState.Carried || CarrierClientId.Value != sender) return;
            if (!TryGetPlayerPos(sender, out Vector3 playerPos)) return;
            if (Vector3.Distance(playerPos, GoalZonePos) > GoalZoneRadius) return;

            State.Value = ObjectiveState.InProgress;
            InteractEndServerTime.Value = (float)NetworkManager.Singleton.ServerTime.Time + InteractDuration;
            _interactRoutine = StartCoroutine(InteractRoutine(sender));
            AnnounceClientRpc($"Objective {ObjectiveIndex.Value}: interaction started...");
        }

        private IEnumerator InteractRoutine(ulong clientId)
        {
            float end = Time.time + InteractDuration;
            while (Time.time < end)
            {
                // server cancels if the carrier moved away (or dropped/disconnected)
                if (!TryGetCarrierPos(out Vector3 pos) ||
                    Vector3.Distance(pos, GoalZonePos) > GoalZoneRadius)
                {
                    State.Value = ObjectiveState.Carried; // invalid -> back to an appropriate state
                    AnnounceClientRpc($"Objective {ObjectiveIndex.Value}: interaction cancelled");
                    yield break;
                }
                // G4: also cancel if the carrier becomes eliminated
                yield return null;
            }

            State.Value = ObjectiveState.Completed;
            CarrierClientId.Value = NoCarrier;
            transform.position = GoalZonePos + Vector3.up * 0.3f; // locked at the zone
            Debug.Log($"[SERVER] Objective {ObjectiveIndex.Value} COMPLETED by client {clientId}");
            AnnounceClientRpc($"Objective {ObjectiveIndex.Value} COMPLETED");
            if (MatchManager.Instance != null)
                MatchManager.Instance.NotifyObjectiveCompleted(ObjectiveIndex.Value);
        }

        private void ServerResetToAvailable()
        {
            if (_interactRoutine != null) StopCoroutine(_interactRoutine);
            State.Value = ObjectiveState.Available;
            CarrierClientId.Value = NoCarrier;
            Vector3 p = transform.position;
            transform.position = new Vector3(p.x, 0.3f, p.z);
            Debug.Log($"[SERVER] Objective {ObjectiveIndex.Value} reset to Available");
        }

        // ---------- helpers ----------

        private static bool IsCarryingAnything(ulong clientId)
        {
            foreach (var obj in All)
                if (obj.CarrierClientId.Value == clientId &&
                    (obj.State.Value == ObjectiveState.Carried || obj.State.Value == ObjectiveState.InProgress))
                    return true;
            return false;
        }

        private bool TryGetCarrierPos(out Vector3 pos)
        {
            return TryGetPlayerPos(CarrierClientId.Value, out pos);
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
