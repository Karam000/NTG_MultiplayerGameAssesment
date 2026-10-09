using UnityEngine;
using UnityEngine.UI;

namespace NTG
{
    public class TouchControls : MonoBehaviour
    {
        [SerializeField] private Joystick joystick;
        [SerializeField] private Button interactButton;
        [SerializeField] private Button throwButton;
        [SerializeField] private bool showInEditor = false; // preview touch UI in the editor

        private void Start()
        {
            bool show = Application.isMobilePlatform;
#if UNITY_EDITOR
            show = show || showInEditor;
#endif
            if (joystick != null) joystick.gameObject.SetActive(show);
            if (interactButton != null) interactButton.gameObject.SetActive(show);
            if (throwButton != null) throwButton.gameObject.SetActive(show);

            if (!show)
            {
                enabled = false; // stop feeding the input override; keyboard only
                return;
            }

            if (interactButton != null)
                interactButton.onClick.AddListener(() => PlayerInteraction.Local?.TryInteract());

            if (throwButton != null)
                throwButton.onClick.AddListener(() => PlayerInteraction.Local?.TryThrow());
        }

        private void Update()
        {
            PlayerMovement.TouchInputOverride = joystick != null ? joystick.Direction : Vector2.zero;
        }

        private void OnDestroy()
        {
            PlayerMovement.TouchInputOverride = Vector2.zero;
        }
    }
}
