using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace NTG
{
    public class PlayerInteraction : NetworkBehaviour
    {
        public static PlayerInteraction Local { get; private set; }

        private PlayerMovement _movement;
        private Vector2 _aimDir = new Vector2(0f, 1f);

        public override void OnNetworkSpawn()
        {
            _movement = GetComponent<PlayerMovement>();
            if (IsOwner) Local = this;
        }

        public override void OnNetworkDespawn()
        {
            if (Local == this) Local = null;
        }

        private void Update()
        {
            if (!IsOwner) return;
            if (MatchManager.IsFinished) return; // match over: no interactions

            // aim follows current move input, keeps last non-zero direction
            if (_movement != null && _movement.CurrentInput != Vector2.zero)
                _aimDir = _movement.CurrentInput;

            var kb = Keyboard.current;
            if (kb == null) return;

            if (kb.eKey.wasPressedThisFrame)
                TryInteract();
            if (kb.spaceKey.wasPressedThisFrame)
                TryThrow();
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
                return;
            }

            // ball pickup has priority when standing next to it
            var ball = SharedBall.Instance;
            if (ball != null && ball.State.Value == BallState.OnGround &&
                Vector3.Distance(myPos, ball.transform.position) <= ObjectiveObject.InteractRange)
            {
                ball.PickupServerRpc();
                return;
            }

            if (nearest != null && nearestDist <= ObjectiveObject.InteractRange
                && nearest.State.Value == ObjectiveState.Available)
            {
                nearest.PickupServerRpc();
            }
        }

        // G6 touch throw button calls this same method.
        public void TryThrow()
        {
            ulong me = NetworkManager.Singleton.LocalClientId;
            var ball = SharedBall.Instance;
            if (ball == null) return;
            if (ball.State.Value != BallState.Carried || ball.PossessorClientId.Value != me) return;

            ball.ThrowServerRpc(_aimDir);
        }
    }
}
