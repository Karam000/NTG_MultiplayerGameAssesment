using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;

namespace NTG
{
    public class PlayerMovement : NetworkBehaviour
    {
        [SerializeField] PlayerObject playerObject;
        [SerializeField] private float moveSpeed = 5f;
        [SerializeField] private float sendRate = 20f;       
        [SerializeField] private float broadcastRate = 20f;  
        [SerializeField] private float interpDelay = 0.1f;   
        [SerializeField] private float snapThreshold = 1.5f; 
        [SerializeField] private float correctionSpeed = 8f; 

        public float ServerSpeedMultiplier { get; set; } = 1f; //for ball carry debuff

        public static Vector2 TouchInputOverride;

        private Vector2 _input;
        public Vector2 CurrentInput => _input;
        private uint _seq;
        private float _sendTimer;
        private float _broadcastTimer;

        // server authoritative state
        private Vector2 _serverInput;
        private Vector3 _serverPos;
        private uint _lastProcessedSeq; 

        #region Reconiliation
        // owner reconciliation
        private struct SentState
        {
            public Vector3 PredictedPos;
        }
        private SentState[] _sent = new SentState[64];
        private Vector3 _correction;
        #endregion

        #region Interpolation
        // remote interpolation buffer
        private struct Snapshot
        {
            public float Time;
            public Vector3 Pos;
        }
        private List<Snapshot> _buffer = new(32); 
        #endregion

        private PlayerObject _playerObject;

        public override void OnNetworkSpawn()
        {
            _serverPos = transform.position;

            if(playerObject == null)
                _playerObject = GetComponent<PlayerObject>();
        }

        private void Update()
        {
            if (IsOwner) 
                OwnerUpdate();

            else if (!IsServer) 
                RemoteUpdate();
        }

        private void FixedUpdate()
        {
            if (!IsServer) 
                return;

            if (!MatchManager.IsFinished)
            {
                CalculateServerMovement();
            }

            _broadcastTimer += Time.fixedDeltaTime;

            if (_broadcastTimer >= 1f / broadcastRate) //if communiation rate passed
            {
                _broadcastTimer = 0f;
                StateClientRpc(_serverPos, _lastProcessedSeq); //send server truth to all clients
            }
        }
        private void OwnerUpdate()
        {
            _input = ReadInput();

            bool frozen = MatchManager.IsFinished ||
                          (_playerObject != null && _playerObject.IsEliminated.Value);
            if (frozen)
                _input = Vector2.zero;

            //Apply immediate client-prediction
            transform.position += new Vector3(_input.x, 0f, _input.y) * (moveSpeed * Time.deltaTime);
            
            if (_correction.sqrMagnitude > 0.0001f) //if correction from server is big enough
            {
                SmoothClientMovementCorrection();
            }

            if (frozen) 
                return; //no prediction, no input send

            _sendTimer += Time.deltaTime;
            if (_sendTimer >= 1f / sendRate) //if send rate passed
            {
                _sendTimer = 0f;
                _sent[_seq % 64] = new SentState { PredictedPos = transform.position };
                InputServerRpc(_input, _seq);
                _seq++;
            }
        }

        #region Movement calculations
        private void CalculateServerMovement() //server accurate movement
        {
            _serverPos += new Vector3(_serverInput.x, 0f, _serverInput.y)
                          * (moveSpeed * ServerSpeedMultiplier * Time.fixedDeltaTime);

            transform.position = _serverPos;
        }
        private void SmoothClientMovementCorrection() //client smoothed movement based on server
        {
            Vector3 step = _correction * (correctionSpeed * Time.deltaTime);

            if (step.sqrMagnitude > _correction.sqrMagnitude)
                step = _correction;

            transform.position += step;
            _correction -= step;
        }

        private void RemoteUpdate() //server movement interpolation for non-owner clients
        {
            if (_buffer.Count == 0)
                return;

            //target time to render
            float renderTime = Time.time - interpDelay;

            //newest snapshot in the buffer
            int newest = _buffer.Count - 1;


            if (renderTime >= _buffer[newest].Time) //if render time is newer than buffer
            {
                transform.position = _buffer[newest].Pos; //use last known snapshot (no interpolation)
            }
            else
            {
                for (int i = newest; i > 0; i--)
                {
                    if (_buffer[i - 1].Time <= renderTime)
                    {
                        var a = _buffer[i - 1]; // older snapshot
                        var b = _buffer[i];     // newer snapshot

                        float t = (renderTime - a.Time) / Mathf.Max(0.0001f, b.Time - a.Time); //range to interpolate

                        transform.position = Vector3.Lerp(a.Pos, b.Pos, t);
                        break;
                    }
                }
            }

            while (_buffer.Count > 2 && _buffer[0].Time < renderTime - 1f)
                _buffer.RemoveAt(0);
        }
        #endregion

        #region Input
        private static Vector2 ReadInput()
        {
            if (TouchInputOverride != Vector2.zero)
                return TouchInputOverride;

            var kb = Keyboard.current;
            if (kb == null)
                return Vector2.zero;

            Vector2 v = Vector2.zero;
            if (kb.wKey.isPressed || kb.upArrowKey.isPressed)
                v.y += 1f;
            if (kb.sKey.isPressed || kb.downArrowKey.isPressed)
                v.y -= 1f;
            if (kb.aKey.isPressed || kb.leftArrowKey.isPressed)
                v.x -= 1f;
            if (kb.dKey.isPressed || kb.rightArrowKey.isPressed)
                v.x += 1f;

            return Vector2.ClampMagnitude(v, 1f);
        }

        #endregion

        #region RPCs

        [Rpc(SendTo.Server, Delivery = RpcDelivery.Unreliable)]
        private void InputServerRpc(Vector2 input, uint seq) //server: receive input from owner
        {
            if (MatchManager.IsFinished || PlayerObject.IsClientEliminated(OwnerClientId))
            {
                _serverInput = Vector2.zero;
                _lastProcessedSeq = seq; // keep reconciliation consistent
                return;
            }
            _serverInput = Vector2.ClampMagnitude(input, 1f);
            _lastProcessedSeq = seq;
        }

        [Rpc(SendTo.NotServer, Delivery = RpcDelivery.Unreliable)]
        private void StateClientRpc(Vector3 pos, uint echoSeq) //server: send server truth to all clients
        {
            if (IsOwner)
            {
                Vector3 d = pos - _sent[echoSeq % 64].PredictedPos;
                if (d.magnitude > snapThreshold) //big change
                {
                    transform.position = pos; //snap to server truth
                    _correction = Vector3.zero;
                }
                else if (d.magnitude > 0.05f) //small change
                {
                    _correction = d; //smooth correction
                }
            }
            else
            {
                _buffer.Add(new Snapshot { Time = Time.time, Pos = pos });
                if (_buffer.Count > 32) _buffer.RemoveAt(0);
            }
        }
    } 
    #endregion
}
