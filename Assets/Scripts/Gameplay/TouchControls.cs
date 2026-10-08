using UnityEngine;
using UnityEngine.UI;

namespace NTG
{
    public class TouchControls : MonoBehaviour
    {
        [SerializeField] private Joystick joystick;
        [SerializeField] private Button interactButton;
        [SerializeField] private Button throwButton;

        private void Start()
        {
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
