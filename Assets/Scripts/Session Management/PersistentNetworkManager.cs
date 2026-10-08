using Unity.Netcode;
using UnityEngine;

namespace NTG
{
    [RequireComponent(typeof(NetworkManager))]
    [DefaultExecutionOrder(-1000)] // must run before NetworkManager.Awake so duplicates never become the singleton
    public class PersistentNetworkManager : MonoBehaviour
    {
        private void Awake()
        {
            var nm = GetComponent<NetworkManager>();
            if (NetworkManager.Singleton != null && NetworkManager.Singleton != nm)
            {
                Destroy(gameObject); // a NetworkManager already exists (e.g. re-entering ClientMenu)
                return;
            }
            DontDestroyOnLoad(gameObject);
        }
    }
}
