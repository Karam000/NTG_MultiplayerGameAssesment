using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace NTG
{
    public class PlayerInteraction : NetworkBehaviour
    {
        private void Update()
        {
            if (!IsOwner) return;
            var kb = Keyboard.current;
            if (kb != null && kb.eKey.wasPressedThisFrame)
                TryInteract();
        }

        // Context-sensitive interact. G6 touch button calls this same method.
        public void TryInteract()
        {
            ulong me = NetworkManager.Singleton.LocalClientId;
            Vector3 myPos = transform.position;

            ObjectiveObject carried = null;
            ObjectiveObject nearest = null;
            float nearestDist = float.MaxValue;

            foreach (var obj in ObjectiveObject.All)
            {
                if (obj.State.Value == ObjectiveState.Carried && obj.CarrierClientId.Value == me)
                    carried = obj;

                float d = Vector3.Distance(myPos, obj.transform.position);
                if (d < nearestDist)
                {
                    nearestDist = d;
                    nearest = obj;
                }
            }

            if (carried != null)
            {
                if (Vector3.Distance(myPos, ObjectiveObject.GoalZonePos) <= ObjectiveObject.GoalZoneRadius)
                    carried.InteractServerRpc();
            }
            else if (nearest != null && nearestDist <= ObjectiveObject.InteractRange
                     && nearest.State.Value == ObjectiveState.Available)
            {
                nearest.PickupServerRpc();
            }
        }
    }
}
