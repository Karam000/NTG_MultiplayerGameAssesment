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
            infoText.text = $"In Game — Client #{NetworkManager.Singleton.LocalClientId}";
            disconnectButton.onClick.AddListener(OnDisconnectClicked);
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
