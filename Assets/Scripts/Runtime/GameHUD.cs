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
            SharedBall.OnAnnouncement += OnAnnouncement;
            MatchManager.OnAnnouncement += OnAnnouncement;

            // rect is sized for one line; hints/announcements add lines 2-3
            infoText.verticalOverflow = VerticalWrapMode.Overflow;
            infoText.horizontalOverflow = HorizontalWrapMode.Overflow;
            infoText.alignment = TextAnchor.UpperCenter;

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
            SharedBall.OnAnnouncement -= OnAnnouncement;
            MatchManager.OnAnnouncement -= OnAnnouncement;
        }

        private void OnAnnouncement(string msg)
        {
            _announce = msg;
            _announceUntil = Time.time + 3f;
        }

        private bool _endpointClearRequested;

        private void Update()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsConnectedClient) return;

            bool finished = MatchManager.IsFinished;

            // leader clears the stale endpoint so nobody auto-reconnects to a dead server
            if (finished && !_endpointClearRequested &&
                SessionManager.IsLeader && SessionManager.TryGetServerEndpoint(out _, out _))
            {
                _endpointClearRequested = true;
                _ = SessionManager.ClearServerEndpoint();
            }

            var label = disconnectButton.GetComponentInChildren<Text>();
            if (label != null)
                label.text = finished ? "BACK TO LOBBY" : "DISCONNECT";

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

            if (MatchManager.Instance != null && MatchManager.Instance.State.Value == MatchState.Finished)
            {
                int w = MatchManager.Instance.WinningTeam.Value;
                return $"MATCH FINISHED - Team {(w == 0 ? "A" : "B")} wins!";
            }

            var player = po.GetComponent<PlayerObject>();
            if (player != null && player.IsEliminated.Value)
                return "ELIMINATED - spectating";

            ulong me = NetworkManager.Singleton.LocalClientId;
            Vector3 myPos = po.transform.position;

            var ball = SharedBall.Instance;
            if (ball != null && ball.State.Value == BallState.Carried && ball.PossessorClientId.Value == me)
                return "SPACE: throw the ball (you are slowed)";

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

            if (ball != null && ball.State.Value == BallState.OnGround &&
                Vector3.Distance(myPos, ball.transform.position) <= ObjectiveObject.InteractRange)
                return "Press E to pick up the BALL";

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
            bool finished = MatchManager.IsFinished;
            NetworkManager.Singleton.Shutdown();

            if (finished)
            {
                // match over: back to the session lobby (session is still active)
                SceneManager.LoadScene("Lobby");
            }
            else
            {
                await SessionManager.LeaveSession();
                SceneManager.LoadScene("ClientMenu");
            }
        }
    }
}
