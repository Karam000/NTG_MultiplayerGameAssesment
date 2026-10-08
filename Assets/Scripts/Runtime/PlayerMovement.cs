using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace NTG
{
    public class PlayerMovement : NetworkBehaviour
    {
        [SerializeField] private float moveSpeed = 5f;
        [SerializeField] private float sendRate = 20f;       // client -> server inputs per second
        [SerializeField] private float broadcastRate = 20f;  // server -> clients states per second
        [SerializeField] private float interpDelay = 0.1f;   // remote render delay (s)
        [SerializeField] private float snapThreshold = 1.5f; // hard teleport beyond this error
        [SerializeField] private float correctionSpeed = 8f; // soft blend-out speed

        // Server-side restriction hook (G4 sets this for the ball carrier).
        public float ServerSpeedMultiplier { get; set; } = 1f;

        // G6 touch seam: virtual joystick writes here; keyboard ignored while non-zero.
        public static Vector2 TouchInputOverride;

        private Vector2 _input;
        public Vector2 CurrentInput => _input;
        private uint _seq;
        private float _sendTimer;
        private float _broadcastTimer;

        // server authoritative state
        private Vector2 _serverInput;
        private Vector3 _serverPos;

        // owner reconciliation
        private struct SentState { public Vector3 PredictedPos; }
        private readonly SentState[] _sent = new SentState[64];
        private Vector3 _correction;

        // remote interpolation buffer
        private struct Snapshot { public float Time; public Vector3 Pos; }
        private readonly System.Collections.Generic.List<Snapshot> _buffer =
            new System.Collections.Generic.List<Snapshot>(32);

        public override void OnNetworkSpawn()
        {
            _serverPos = transform.position;
            _playerObject = GetComponent<PlayerObject>();
        }

        private PlayerObject _playerObject;

        private void Update()
        {
            if (IsOwner) OwnerUpdate();
            else if (!IsServer) RemoteUpdate();
        }

        private void FixedUpdate()
        {
            if (!IsServer) return;

            _serverPos += new Vector3(_serverInput.x, 0f, _serverInput.y)
                          * (moveSpeed * ServerSpeedMultiplier * Time.fixedDeltaTime);
            transform.position = _serverPos;

            _broadcastTimer += Time.fixedDeltaTime;
            if (_broadcastTimer >= 1f / broadcastRate)
            {
                _broadcastTimer = 0f;
                StateClientRpc(_serverPos, _lastProcessedSeq);
            }
        }

        // ---------- owner: predict + send ----------

        private void OwnerUpdate()
        {
            _input = ReadInput();

            // eliminated or match over: no local prediction, no input traffic
            bool frozen = MatchManager.IsFinished ||
                          (_playerObject != null && _playerObject.IsEliminated.Value);
            if (frozen)
                _input = Vector2.zero;

            transform.position += new Vector3(_input.x, 0f, _input.y) * (moveSpeed * Time.deltaTime);

            if (_correction.sqrMagnitude > 0.0001f)
            {
                Vector3 step = _correction * (correctionSpeed * Time.deltaTime);
                if (step.sqrMagnitude > _correction.sqrMagnitude) step = _correction;
                transform.position += step;
                _correction -= step;
            }

            if (frozen) return; // no prediction, no input traffic

            _sendTimer += Time.deltaTime;
            if (_sendTimer >= 1f / sendRate)
            {
                _sendTimer = 0f;
                _sent[_seq % 64] = new SentState { PredictedPos = transform.position };
                InputServerRpc(_input, _seq);
                _seq++;
            }
        }

        private static Vector2 ReadInput()
        {
            if (TouchInputOverride != Vector2.zero) return TouchInputOverride;

            var kb = Keyboard.current;
            if (kb == null) return Vector2.zero;

            Vector2 v = Vector2.zero;
            if (kb.wKey.isPressed || kb.upArrowKey.isPressed) v.y += 1f;
            if (kb.sKey.isPressed || kb.downArrowKey.isPressed) v.y -= 1f;
            if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) v.x -= 1f;
            if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) v.x += 1f;
            return Vector2.ClampMagnitude(v, 1f);
        }

        // ---------- remote: interpolate ----------

        private void RemoteUpdate()
        {
            if (_buffer.Count == 0) return;

            float renderTime = Time.time - interpDelay;
            int newest = _buffer.Count - 1;

            if (renderTime >= _buffer[newest].Time)
            {
                transform.position = _buffer[newest].Pos;
            }
            else
            {
                for (int i = newest; i > 0; i--)
                {
                    if (_buffer[i - 1].Time <= renderTime)
                    {
                        var a = _buffer[i - 1];
                        var b = _buffer[i];
                        float t = (renderTime - a.Time) / Mathf.Max(0.0001f, b.Time - a.Time);
                        transform.position = Vector3.Lerp(a.Pos, b.Pos, t);
                        break;
                    }
                }
            }

            while (_buffer.Count > 2 && _buffer[0].Time < renderTime - 1f)
                _buffer.RemoveAt(0);
        }

        // ---------- RPCs ----------

        private uint _lastProcessedSeq; // server: latest input seq received from the owner

        [Rpc(SendTo.Server, Delivery = RpcDelivery.Unreliable)]
        private void InputServerRpc(Vector2 input, uint seq)
        {
            // frozen players (eliminated / match finished) cannot move - server rejects their input
            if (MatchManager.IsFinished || PlayerObject.IsClientEliminated(OwnerClientId))
            {
                _serverInput = Vector2.zero;
                _lastProcessedSeq = seq; // keep reconciliation consistent
                return;
            }
            // validation: clamp magnitude; client sends intent only, never position,
            // so it can never exceed moveSpeed on the server
            _serverInput = Vector2.ClampMagnitude(input, 1f);
            _lastProcessedSeq = seq;
        }

        [Rpc(SendTo.NotServer, Delivery = RpcDelivery.Unreliable)]
        private void StateClientRpc(Vector3 pos, uint echoSeq)
        {
            if (IsOwner)
            {
                Vector3 d = pos - _sent[echoSeq % 64].PredictedPos;
                if (d.magnitude > snapThreshold)
                {
                    transform.position = pos; // large desync: hard snap to server truth
                    _correction = Vector3.zero;
                }
                else if (d.magnitude > 0.05f)
                {
                    _correction = d; // small drift: blend out over the next frames
                }
            }
            else
            {
                _buffer.Add(new Snapshot { Time = Time.time, Pos = pos });
                if (_buffer.Count > 32) _buffer.RemoveAt(0);
            }
        }
    }
}
