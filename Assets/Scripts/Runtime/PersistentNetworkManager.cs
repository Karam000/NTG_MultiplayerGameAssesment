using UnityEngine;

namespace NTG
{
    [RequireComponent(typeof(Unity.Netcode.NetworkManager))]
    public class PersistentNetworkManager : MonoBehaviour
    {
        private void Awake()
        {
            DontDestroyOnLoad(gameObject);
        }
    }
}
