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

        private void Start()
        {
            disconnectButton.onClick.AddListener(OnDisconnectClicked);
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

            infoText.text = $"Client #{nm.LocalClientId}  |  Team {team}  |  {state}";
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
