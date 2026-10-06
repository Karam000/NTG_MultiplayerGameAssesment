using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace NTG
{
    public class SessionMenuUI : MonoBehaviour
    {
        [SerializeField] private Button findMatchButton;
        [SerializeField] private Text statusText;

        private void Start()
        {
            findMatchButton.onClick.AddListener(OnFindMatchClicked);
            findMatchButton.interactable = false;
            statusText.text = "Signing in...";
            _ = Init();
        }

        private async Task Init()
        {
            bool ok = await SessionManager.Initialize();
            statusText.text = ok ? "Signed in." : $"Sign-in failed: {SessionManager.LastError}";
            findMatchButton.interactable = ok;
        }

        private async void OnFindMatchClicked()
        {
            findMatchButton.interactable = false;
            statusText.text = "Finding match...";

            bool ok = await SessionManager.QuickJoin();
            if (ok)
            {
                SceneManager.LoadScene("Lobby");
            }
            else
            {
                statusText.text = $"Quick join failed: {SessionManager.LastError}";
                findMatchButton.interactable = true;
            }
        }
    }
}
