using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace NTG
{
    public class PlayerInteraction : NetworkBehaviour
    {
        [SerializeField] private PlayerMovement movement;
        public static PlayerInteraction Local { get; private set; }

        private Vector2 _aimDir = new(0f, 1f);
        SharedBall sharedBall => SharedBall.Instance;
        ulong myClient => NetworkManager.Singleton.LocalClientId;
        Vector3 myPos => transform.position;
        public override void OnNetworkSpawn()
        {
            if (movement == null)
                movement = GetComponent<PlayerMovement>();

            if (IsOwner)
                Local = this;
        }

        public override void OnNetworkDespawn()
        {
            if (Local == this)
                Local = null;
        }

        private void Update()
        {
            if (!IsOwner)
                return;

            if (MatchManager.IsFinished)
                return;

            if (movement != null && movement.CurrentInput != Vector2.zero) //if moving
                _aimDir = movement.CurrentInput; //aim at move dir

            DetectInputAndTryAction();
        }

        private void DetectInputAndTryAction()
        {
            var kb = Keyboard.current;
            if (kb == null)
                return;

            if (kb.eKey.wasPressedThisFrame)
                TryInteract();

            if (kb.spaceKey.wasPressedThisFrame)
                TryThrow();
        }

        public void TryInteract()
        {
           

            //determine nearest object and if carrying anything
            float nearestDist = DetermineNearestObject(myClient, myPos, out ObjectiveObject carried, out ObjectiveObject nearest);

            #region Zone drop
            if (carried != null) //if carrying
            {
                if (Vector3.Distance(myPos, ObjectiveObject.GoalZonePos) <= ObjectiveObject.GoalZoneRadius) //in in zone
                    carried.InteractServerRpc(); //start drop objective RPC
                return;
            }
            #endregion

            #region Ball pickup
            if (sharedBall != null &&
                    sharedBall.State.Value == BallState.OnGround && //ball is free
                    Vector3.Distance(myPos, sharedBall.transform.position) <= ObjectiveObject.InteractRange) //ball is in my range
            {
                sharedBall.PickupServerRpc(); //start pickup ball RPC
                return;
            }
            #endregion

            #region Nearest cube pickup

            if (nearest != null && 
                nearestDist <= ObjectiveObject.InteractRange && //nearest is in range
                nearest.State.Value == ObjectiveState.Available) //nearest is available
            {
                nearest.PickupServerRpc(); //start pickup cube RPC
            } 
            #endregion
        }

        private float DetermineNearestObject(ulong myClient, Vector3 myPos, out ObjectiveObject carried, out ObjectiveObject nearest)
        {
            carried = null;
            nearest = null;
            float nearestDist = float.MaxValue;

            foreach (var obj in ObjectiveObject.All)
            {
                if (obj.State.Value == ObjectiveState.Carried && obj.CarrierClientId.Value == myClient)
                    carried = obj;

                float d = Vector3.Distance(myPos, obj.transform.position);
                if (d < nearestDist)
                {
                    nearestDist = d;
                    nearest = obj;
                }
            }

            return nearestDist;
        }

        public void TryThrow()
        {
            if (sharedBall == null) 
                return;

            if (sharedBall.State.Value != BallState.Carried || //ball is not carried
                sharedBall.PossessorClientId.Value != myClient) //ball is not carried by me
                return;

            sharedBall.ThrowServerRpc(_aimDir); //start throw ball RPC
        }
    }
}