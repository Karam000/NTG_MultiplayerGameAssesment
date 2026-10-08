using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using System;

namespace NTG
{
    public enum ObjectiveState : byte
    {
        Available,
        Carried,
        InProgress,
        Completed
    }

    public class ObjectiveObject : NetworkBehaviour
    {
        public static readonly List<ObjectiveObject> All = new();
        public static event Action<string> OnAnnouncement;

        public const float InteractRange = 2.5f;
        public const float GoalZoneRadius = 2.5f;
        public const float InteractDuration = 1f;
        public static readonly Vector3 GoalZonePos = new(0f, 0f, -5f);

        private const ulong NoCarrier = ulong.MaxValue;

        public NetworkVariable<int> ObjectiveIndex = new(
            -1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public NetworkVariable<ObjectiveState> State = new(
            ObjectiveState.Available, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public NetworkVariable<ulong> CarrierClientId = new(
            NoCarrier, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public NetworkVariable<float> InteractEndServerTime = new(
            0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private Coroutine _interactRoutine;
        private Renderer _renderer;

        private Color[] StateColors =
        {
            Color.yellow, // Available - yellow
            Color.orange,  // Carried - orange
            Color.cyan,  // InProgress - cyan
            Color.green // Completed - green
        };

        NetworkManager networkManager => NetworkManager.Singleton;

        public override void OnNetworkSpawn()
        {
            All.Add(this);
            _renderer = GetComponentInChildren<Renderer>();
            State.OnValueChanged += (_, __) => ApplyColor();
            ApplyColor();
        }

        private void ApplyColor()
        {
            if (_renderer == null) 
                return;

            _renderer.material.color = StateColors[(int)State.Value % StateColors.Length];
        }

        public override void OnNetworkDespawn()
        {
            All.Remove(this);
        }

        private void Update()
        {
            if (!IsServer) 
                return;

            if (State.Value != ObjectiveState.Carried && State.Value != ObjectiveState.InProgress) 
                return;

            if (!TryGetCarrierPos(out Vector3 carrierPos))
            {
                ServerDrop();
                return;
            }

            if (State.Value == ObjectiveState.Carried)
                transform.position = carrierPos + Vector3.up * 1.2f; // just above the head
        }


        #region RPCs
        [Rpc(SendTo.Server, RequireOwnership = false)]
        public void PickupServerRpc(RpcParams rpcParams = default) //server
        {
            ulong sender = rpcParams.Receive.SenderClientId;

            if (!MatchManager.IsPlaying) return;                    // server rejects gameplay outside Playing
            if (PlayerObject.IsClientEliminated(sender)) return; // eliminated players cannot collect
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
        public void InteractServerRpc(RpcParams rpcParams = default) //server
        {
            ulong sender = rpcParams.Receive.SenderClientId;

            if (!MatchManager.IsPlaying) return;
            if (PlayerObject.IsClientEliminated(sender)) return;
            if (State.Value != ObjectiveState.Carried || CarrierClientId.Value != sender) return;
            if (!TryGetPlayerPos(sender, out Vector3 playerPos)) return;
            if (Vector3.Distance(playerPos, GoalZonePos) > GoalZoneRadius) return;

            State.Value = ObjectiveState.InProgress;
            InteractEndServerTime.Value = (float)NetworkManager.Singleton.ServerTime.Time + InteractDuration;
            _interactRoutine = StartCoroutine(InteractRoutine(sender));
            AnnounceClientRpc($"Objective {ObjectiveIndex.Value}: interaction started...");
        }

        [Rpc(SendTo.NotServer)]
        private void AnnounceClientRpc(string msg) //client
        {
            OnAnnouncement?.Invoke(msg);
        }

        #endregion
        private IEnumerator InteractRoutine(ulong clientId)
        {
            float end = Time.time + InteractDuration;
            while (Time.time < end)
            {
                // server cancels if the carrier gets eliminated mid-interaction
                if (PlayerObject.IsClientEliminated(clientId))
                {
                    ServerDrop();
                    AnnounceClientRpc($"Objective {ObjectiveIndex.Value}: interaction cancelled");
                    yield break;
                }
                // carrier disconnected -> drop it; moved away -> back to Carried (spec 12)
                if (!TryGetCarrierPos(out Vector3 pos))
                {
                    ServerDrop();
                    AnnounceClientRpc($"Objective {ObjectiveIndex.Value}: interaction cancelled");
                    yield break;
                }
                if (Vector3.Distance(pos, GoalZonePos) > GoalZoneRadius)
                {
                    State.Value = ObjectiveState.Carried; // invalid -> back to an appropriate state
                    AnnounceClientRpc($"Objective {ObjectiveIndex.Value}: interaction cancelled");
                    yield break;
                }
                yield return null;
            }

            State.Value = ObjectiveState.Completed;
            CarrierClientId.Value = NoCarrier;
            transform.position = GoalZonePos + Vector3.up * 0.3f; // locked at the zone
            Debug.Log($"[SERVER] Objective {ObjectiveIndex.Value} COMPLETED by client {clientId}");
            AnnounceClientRpc($"Objective {ObjectiveIndex.Value} COMPLETED");
            if (MatchManager.Instance != null)
                MatchManager.Instance.NotifyObjectiveCompleted(ObjectiveIndex.Value, clientId);
        }

        public void ServerDrop()
        {
            if (_interactRoutine != null) StopCoroutine(_interactRoutine);
            State.Value = ObjectiveState.Available;
            CarrierClientId.Value = NoCarrier;
            Vector3 p = transform.position;
            transform.position = new Vector3(p.x, 0.3f, p.z);
            Debug.Log($"[SERVER] Objective {ObjectiveIndex.Value} reset to Available");
        }

        
        #region Helpers
        private bool IsCarryingAnything(ulong clientId)
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

        private bool TryGetPlayerPos(ulong clientId, out Vector3 pos)
        {
            pos = default;

            if (networkManager == null)
                return false;

            if (!networkManager.ConnectedClients.TryGetValue(clientId, out var client))
                return false;

            if (client.PlayerObject == null)
                return false;

            pos = client.PlayerObject.transform.position;
            return true;
        } 
        #endregion
    }
}
