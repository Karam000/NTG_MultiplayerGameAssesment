using System.Text;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Multiplayer;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace NTG
{
    public class LobbyUI : MonoBehaviour
    {
        [SerializeField] private Text rosterText;
        [SerializeField] private Text statusText;
        [SerializeField] private Button startButton;
        [SerializeField] private Button readyButton;
        [SerializeField] private Button leaveButton;

        private ISession _session;
        private bool _busy;       // leader: allocation in progress
        private bool _connecting; // endpoint known, NGO connect in progress

        private void Start()
        {
            if (!SessionManager.InSession)
            {
                SceneManager.LoadScene("ClientMenu");
                return;
            }

            _session = SessionManager.CurrentSession;
            _session.Changed += Refresh;
            _session.Deleted += OnSessionGone;
            _session.RemovedFromSession += OnSessionGone;

            startButton.onClick.AddListener(OnStartClicked);
            readyButton.onClick.AddListener(OnReadyClicked);
            leaveButton.onClick.AddListener(OnLeaveClicked);

            var nm = NetworkManager.Singleton;
            if (nm != null)
            {
                nm.OnClientDisconnectCallback += OnConnectFailed;
                nm.OnTransportFailure += OnTransportFailed;
            }

            Refresh();
        }

        private void OnDestroy()
        {
            if (_session != null)
            {
                _session.Changed -= Refresh;
                _session.Deleted -= OnSessionGone;
                _session.RemovedFromSession -= OnSessionGone;
            }

            var nm = NetworkManager.Singleton;
            if (nm != null)
            {
                nm.OnClientDisconnectCallback -= OnConnectFailed;
                nm.OnTransportFailure -= OnTransportFailed;
            }
        }

        private void Refresh()
        {
            if (_session == null) return;

            var sb = new StringBuilder();
            foreach (var p in _session.Players)
            {
                bool leader = p.Id == _session.Host;
                bool you = p.Id == SessionManager.LocalPlayerId;
                bool ready = SessionManager.IsPlayerReady(p);

                sb.Append(leader ? "* " : "  ");
                sb.Append("Player-");
                sb.Append(p.Id.Length > 8 ? p.Id.Substring(0, 8) : p.Id);
                if (you) sb.Append(" (you)");
                if (leader) sb.Append(" [leader]");
                if (ready) sb.Append(" [ready]");
                sb.AppendLine();
            }
            rosterText.text = sb.ToString();

            bool isLeader = SessionManager.IsLeader;
            startButton.gameObject.SetActive(isLeader);
            readyButton.gameObject.SetActive(!isLeader);

            if (isLeader)
                startButton.interactable = !_busy && !_connecting && EveryoneElseReady();
            else
                readyButton.GetComponentInChildren<Text>().text = LocalReady() ? "CANCEL READY" : "READY";

            if (!_busy && !_connecting)
                statusText.text = $"Players {_session.PlayerCount}/{SessionManager.MaxPlayers}";

            TryConnect();
        }

        private bool LocalReady()
        {
            return SessionManager.IsPlayerReady(_session.CurrentPlayer);
        }

        private bool EveryoneElseReady()
        {
            foreach (var p in _session.Players)
                if (p.Id != _session.Host && !SessionManager.IsPlayerReady(p))
                    return false;
            return true;
        }

        private void OnReadyClicked()
        {
            _ = SessionManager.SetReady(!LocalReady());
        }

        private async void OnStartClicked()
        {
            if (!SessionManager.IsLeader || _busy) return;

            _busy = true;
            startButton.interactable = false;
            statusText.text = "Starting server...";

            var (ip, port) = await ServerAllocator.AllocateAsync();
            if (ip == null)
            {
                statusText.text = "Server allocation failed";
                _busy = false;
                return;
            }

            bool published = await SessionManager.SetServerEndpoint(ip, port);
            if (!published)
            {
                statusText.text = $"Publish failed: {SessionManager.LastError}";
                _busy = false;
            }
            // Success: endpoint is now a session property -> Refresh() -> TryConnect() on every client.
        }

        private void TryConnect()
        {
            if (_connecting || _session == null) return;
            if (!SessionManager.TryGetServerEndpoint(out string ip, out ushort port)) return;

            var nm = NetworkManager.Singleton;
            if (nm == null)
            {
                statusText.text = "NetworkManager missing";
                return;
            }
            if (nm.IsListening) return;

            _connecting = true;
            statusText.text = $"Connecting to {ip}:{port}...";
            ((UnityTransport)nm.NetworkConfig.NetworkTransport).SetConnectionData(ip, port);
            nm.StartClient();
        }

        private void OnConnectFailed(ulong clientId)
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || clientId != nm.LocalClientId) return;
            if (!_connecting) return;

            _connecting = false;
            _busy = false;
            statusText.text = "Connection failed (server unreachable?)";
        }

        private void OnTransportFailed()
        {
            _connecting = false;
            _busy = false;
            statusText.text = "Transport failure (server unreachable?)";
        }

        private async void OnLeaveClicked()
        {
            leaveButton.interactable = false;
            var nm = NetworkManager.Singleton;
            if (nm != null && nm.IsListening) nm.Shutdown();
            await SessionManager.LeaveSession();
            SceneManager.LoadScene("ClientMenu");
        }

        private void OnSessionGone()
        {
            SceneManager.LoadScene("ClientMenu");
        }
    }
}
