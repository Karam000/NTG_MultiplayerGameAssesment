using Unity.Netcode;
using UnityEngine;

namespace NTG
{
    public class PlayerObject : NetworkBehaviour
    {
        public NetworkVariable<int> TeamIndex = new NetworkVariable<int>(
            -1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public NetworkVariable<bool> IsEliminated = new NetworkVariable<bool>(
            false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public static bool IsClientEliminated(ulong clientId)
        {
            var nm = NetworkManager.Singleton;
            if (nm == null) return false;
            if (!nm.ConnectedClients.TryGetValue(clientId, out var client) || client.PlayerObject == null)
                return false;
            var p = client.PlayerObject.GetComponent<PlayerObject>();
            return p != null && p.IsEliminated.Value;
        }

        private static readonly Color[] TeamColors =
        {
            new Color(0.25f, 0.5f, 1f),  // Team A - blue
            new Color(1f, 0.35f, 0.25f)  // Team B - red
        };

        private Renderer _renderer;

        public override void OnNetworkSpawn()
        {
            _renderer = GetComponentInChildren<Renderer>();
            TeamIndex.OnValueChanged += (_, __) => ApplyColor();
            IsEliminated.OnValueChanged += (_, __) => ApplyColor();
            ApplyColor();

            if (IsOwner)
            {
                var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                marker.name = "LocalMarker";
                Destroy(marker.GetComponent<Collider>());
                marker.transform.SetParent(transform, false);
                marker.transform.localPosition = new Vector3(0f, 1.3f, 0f);
                marker.transform.localScale = Vector3.one * 0.3f;
            }
        }

        private void ApplyColor()
        {
            if (_renderer == null) return;
            if (IsEliminated.Value)
            {
                _renderer.material.color = new Color(0.4f, 0.4f, 0.4f); // grayed out
                return;
            }
            if (TeamIndex.Value < 0) return;
            _renderer.material.color = TeamColors[TeamIndex.Value % TeamColors.Length];
        }
    }
}
