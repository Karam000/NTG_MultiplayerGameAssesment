using System.Collections;
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

        private string _baseStatus = "";

        private void Start()
        {
            findMatchButton.onClick.AddListener(OnFindMatchClicked);
            findMatchButton.interactable = false;
            SetStatus("Signing in");
            StartCoroutine(AnimateDots());
            _ = Init();
        }

        private void SetStatus(string baseText)
        {
            _baseStatus = baseText;
            statusText.text = baseText;
        }

        private IEnumerator AnimateDots()
        {
            int dots = 0;
            while (true)
            {
                statusText.text = _baseStatus + new string('.', dots);
                dots = (dots + 1) % 4;
                yield return new WaitForSeconds(0.4f);
            }
        }

        private async Task Init()
        {
            bool ok = await SessionManager.Initialize();
            StopAllCoroutines();
            statusText.text = ok
                ? "Signed in."
                : $"Sign-in failed: {SessionManager.LastError}";
            findMatchButton.interactable = ok;
        }

        private async void OnFindMatchClicked()
        {
            findMatchButton.interactable = false;
            SetStatus("Finding match");
            StartCoroutine(AnimateDots());

            bool ok = await SessionManager.QuickJoin();
            StopAllCoroutines();

            if (ok)
            {
                statusText.text = "Match found! Entering lobby...";
                await Task.Delay(800);
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
