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

        // UI arrow above the local player (assign sprite on the prefab)
        [SerializeField] private Sprite localArrowSprite;
        private UnityEngine.UI.Image _arrowImage;
        private Camera _cam;

        public override void OnNetworkSpawn()
        {
            _renderer = GetComponentInChildren<Renderer>();
            TeamIndex.OnValueChanged += (_, __) => ApplyColor();
            IsEliminated.OnValueChanged += (_, __) => ApplyColor();
            ApplyColor();

            if (IsOwner && localArrowSprite != null)
            {
                var canvas = FindFirstObjectByType<Canvas>();
                if (canvas != null)
                {
                    var go = new GameObject("LocalArrow", typeof(RectTransform), typeof(UnityEngine.UI.Image));
                    go.transform.SetParent(canvas.transform, false);
                    _arrowImage = go.GetComponent<UnityEngine.UI.Image>();
                    _arrowImage.sprite = localArrowSprite;
                    ((RectTransform)go.transform).sizeDelta = new Vector2(48f, 48f);
                    _cam = Camera.main;
                }
            }
        }

        public override void OnNetworkDespawn()
        {
            if (_arrowImage != null)
                Destroy(_arrowImage.gameObject);
        }

        private void Update()
        {
            if (_arrowImage == null) return;
            if (_cam == null) _cam = Camera.main;
            if (_cam == null) return;

            Vector3 sp = _cam.WorldToScreenPoint(transform.position + Vector3.up * 1.6f);
            _arrowImage.enabled = sp.z > 0f;
            _arrowImage.transform.position = new Vector3(sp.x, sp.y + 40f, 0f);
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
