using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace NTG
{
    public class GameHUD : MonoBehaviour
    {
        [SerializeField] private Text infoText;
        [SerializeField] private Button disconnectButton;

        private string _announce = "";
        private float _announceUntil;

        private void Start()
        {
            disconnectButton.onClick.AddListener(OnDisconnectClicked);
            ObjectiveObject.OnAnnouncement += OnAnnouncement;

            // goal zone visual (deterministic position -> each client creates its own)
            var zone = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            zone.name = "GoalZone";
            Destroy(zone.GetComponent<Collider>());
            zone.transform.position = ObjectiveObject.GoalZonePos + Vector3.up * 0.05f;
            zone.transform.localScale = new Vector3(
                ObjectiveObject.GoalZoneRadius * 2f, 0.05f, ObjectiveObject.GoalZoneRadius * 2f);
            zone.GetComponent<Renderer>().material.color = new Color(0.2f, 0.9f, 0.3f);
        }

        private void OnDestroy()
        {
            ObjectiveObject.OnAnnouncement -= OnAnnouncement;
        }

        private void OnAnnouncement(string msg)
        {
            _announce = msg;
            _announceUntil = Time.time + 3f;
        }

        private void Update()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsConnectedClient) return;

            string team = "?";
            var po = nm.LocalClient != null ? nm.LocalClient.PlayerObject : null;
            if (po != null)
            {
                int t = po.GetComponent<PlayerObject>().TeamIndex.Value;
                team = t == 0 ? "A" : t == 1 ? "B" : "?";
            }

            string state = MatchManager.Instance != null
                ? MatchManager.Instance.State.Value.ToString()
                : "-";

            infoText.text = $"Client #{nm.LocalClientId}  |  Team {team}  |  {state}\n{ContextHint(po)}";

            if (Time.time < _announceUntil)
                infoText.text += $"\n>> {_announce}";
        }

        private string ContextHint(NetworkObject po)
        {
            if (po == null) return "";

            ulong me = NetworkManager.Singleton.LocalClientId;
            Vector3 myPos = po.transform.position;

            foreach (var obj in ObjectiveObject.All)
            {
                if (obj.State.Value == ObjectiveState.InProgress && obj.CarrierClientId.Value == me)
                    return "Completing... stay inside the zone!";

                if (obj.State.Value == ObjectiveState.Carried && obj.CarrierClientId.Value == me)
                {
                    return Vector3.Distance(myPos, ObjectiveObject.GoalZonePos) <= ObjectiveObject.GoalZoneRadius
                        ? "Press E to complete (1s - stay in the zone)"
                        : "Carrying objective - take it to the GREEN zone";
                }
            }

            foreach (var obj in ObjectiveObject.All)
            {
                if (obj.State.Value == ObjectiveState.Available &&
                    Vector3.Distance(myPos, obj.transform.position) <= ObjectiveObject.InteractRange)
                    return "Press E to pick up the objective";
            }

            return "";
        }

        private void OnGUI()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsConnectedClient) return;

            foreach (var obj in ObjectiveObject.All)
            {
                if (obj.State.Value != ObjectiveState.InProgress ||
                    obj.CarrierClientId.Value != nm.LocalClientId)
                    continue;

                float remaining = obj.InteractEndServerTime.Value - (float)nm.ServerTime.Time;
                float fill = 1f - Mathf.Clamp01(remaining / ObjectiveObject.InteractDuration);

                var r = new Rect(Screen.width / 2f - 150f, Screen.height * 0.7f, 300f, 24f);
                GUI.Box(r, GUIContent.none);
                GUI.color = Color.green;
                GUI.DrawTexture(new Rect(r.x + 2, r.y + 2, (r.width - 4) * fill, r.height - 4),
                    Texture2D.whiteTexture);
                GUI.color = Color.white;
            }
        }

        private async void OnDisconnectClicked()
        {
            disconnectButton.interactable = false;
            NetworkManager.Singleton.Shutdown();
            await SessionManager.LeaveSession();
            SceneManager.LoadScene("ClientMenu");
        }
    }
}
