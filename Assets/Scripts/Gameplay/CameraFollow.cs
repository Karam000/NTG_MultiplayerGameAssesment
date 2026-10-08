using Unity.Netcode;
using UnityEngine;

namespace NTG
{
    public class CameraFollow : MonoBehaviour
    {
        [SerializeField] private Vector3 offset /*= new Vector3(0f, 10f, -8f)*/;

        private Transform target;
        private NetworkManager networkManager => NetworkManager.Singleton;

        private void LateUpdate()
        {
            if (target == null)
            {
                if (networkManager != null && 
                    networkManager.IsConnectedClient &&
                    networkManager.LocalClient != null &&
                    networkManager.LocalClient.PlayerObject != null)
                {
                    target = networkManager.LocalClient.PlayerObject.transform;
                }
                return;
            }

            transform.position = target.position + offset;
        }
    }
}
