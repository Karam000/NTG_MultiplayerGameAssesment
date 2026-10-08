using Unity.Netcode;
using UnityEngine;

namespace NTG
{
    public class CameraFollow : MonoBehaviour
    {
        [SerializeField] private Vector3 offset = new Vector3(0f, 10f, -8f);
        [SerializeField] private float followSpeed = 10f;

        private Transform _target;

        private void Start()
        {
            transform.rotation = Quaternion.Euler(50f, 0f, 0f); // fixed pitch, no per-frame re-aim
        }

        private void LateUpdate()
        {
            if (_target == null)
            {
                var nm = NetworkManager.Singleton;
                if (nm != null && nm.IsConnectedClient &&
                    nm.LocalClient != null && nm.LocalClient.PlayerObject != null)
                {
                    _target = nm.LocalClient.PlayerObject.transform;
                }
                return;
            }

            Vector3 desired = _target.position + offset;
            transform.position = Vector3.Lerp(transform.position, desired, followSpeed * Time.deltaTime);
        }
    }
}
