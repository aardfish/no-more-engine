using UnityEngine;
using System.Collections.Generic;
using Unity.Entities;

namespace NoMoreEngine.Input
{
    /// <summary>
    /// Deterministic InputProcessor that processes InputPackets without Unity frame dependencies
    /// Provides edge detection (button down/up) by comparing consecutive packets
    /// </summary>
    public class InputProcessor : MonoBehaviour
    {
        [Header("Settings")]
        [SerializeField] private int inputHistorySize = 60;

        // Input source
        private InputSerializer inputSerializer;

        // Per-player input states
        private Dictionary<byte, PlayerInputState> playerStates = new Dictionary<byte, PlayerInputState>();
        
        // Singleton for easy access
        private static InputProcessor instance;
        public static InputProcessor Instance => instance;

        void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }
            instance = this;

            // Find input serializer
            inputSerializer = FindAnyObjectByType<InputSerializer>();
            if (inputSerializer == null)
            {
                Debug.LogError("[InputProcessor] InputSerializer not found!");
            }
        }

        void Start()
        {
            if (inputSerializer != null)
            {
                inputSerializer.OnInputPacketsReady += ProcessInputPackets;
                Debug.Log("[InputProcessor] Connected to InputSerializer");
            }
        }

        void OnDestroy()
        {
            if (inputSerializer != null)
            {
                inputSerializer.OnInputPacketsReady -= ProcessInputPackets;
            }

            if (instance == this)
            {
                instance = null;
            }
        }

        /// <summary>
        /// Process raw input packets from InputSerializer
        /// </summary>
        private void ProcessInputPackets(InputPacket[] packets)
        {
            foreach (var packet in packets)
            {
                // Get or create player state
                if (!playerStates.TryGetValue(packet.playerIndex, out var playerState))
                {
                    playerState = new PlayerInputState(packet.playerIndex, inputHistorySize);
                    playerStates[packet.playerIndex] = playerState;
                }

                // Update with latest packet
                playerState.UpdateFromPacket(packet);
            }
        }

        /// <summary>
        /// Get current button state for a player
        /// </summary>
        public bool GetButton(byte playerIndex, InputButton button)
        {
            if (playerStates.TryGetValue(playerIndex, out var state))
            {
                return state.GetButton(button);
            }
            return false;
        }

        /// <summary>
        /// Get if button was pressed this simulation frame
        /// </summary>
        public bool GetButtonDown(byte playerIndex, InputButton button)
        {
            if (playerStates.TryGetValue(playerIndex, out var state))
            {
                return state.GetButtonDown(button);
            }
            return false;
        }

        /// <summary>
        /// Get if button was released this simulation frame
        /// </summary>
        public bool GetButtonUp(byte playerIndex, InputButton button)
        {
            if (playerStates.TryGetValue(playerIndex, out var state))
            {
                return state.GetButtonUp(button);
            }
            return false;
        }

        /// <summary>
        /// Get motion input for a player
        /// </summary>
        public Vector2 GetMotion(byte playerIndex)
        {
            if (playerStates.TryGetValue(playerIndex, out var state))
            {
                return state.GetMotion();
            }
            return Vector2.zero;
        }

        /// <summary>
        /// Get view input for a player
        /// </summary>
        public Vector2 GetView(byte playerIndex)
        {
            if (playerStates.TryGetValue(playerIndex, out var state))
            {
                return state.GetView();
            }
            return Vector2.zero;
        }

        /// <summary>
        /// Get pad input for a player
        /// </summary>
        public Vector2 GetPad(byte playerIndex)
        {
            if (playerStates.TryGetValue(playerIndex, out var state))
            {
                return state.GetPad();
            }
            return Vector2.zero;
        }

        /// <summary>
        /// Get the latest raw input packet for a player
        /// </summary>
        public InputPacket GetLatestPacket(byte playerIndex)
        {
            if (playerStates.TryGetValue(playerIndex, out var state))
            {
                return state.GetLatestPacket();
            }
            return default;
        }

        /// <summary>
        /// Register player entity (for compatibility)
        /// </summary>
        public void RegisterPlayerEntity(Entity entity, byte playerIndex)
        {
            if (!playerStates.ContainsKey(playerIndex))
            {
                playerStates[playerIndex] = new PlayerInputState(playerIndex, inputHistorySize);
            }
            Debug.Log($"[InputProcessor] Registered player {playerIndex}");
        }
    }

    /// <summary>
    /// Deterministic player input state using packet comparison for edge detection
    /// </summary>
    public class PlayerInputState
    {
        private byte playerIndex;
        private int maxHistorySize;

        // Current and previous packets for edge detection
        private InputPacket currentPacket;
        private InputPacket previousPacket;
        private bool hasReceivedInput = false;
        private bool hasPreviousPacket = false;

        // Input history for replay/rollback support
        private Queue<InputPacket> packetHistory;

        public PlayerInputState(byte playerIndex, int historySize)
        {
            this.playerIndex = playerIndex;
            this.maxHistorySize = historySize;
            this.packetHistory = new Queue<InputPacket>(historySize);
        }

        /// <summary>
        /// Update state from new input packet
        /// </summary>
        public void UpdateFromPacket(InputPacket packet)
        {
            // Only update previous if we had a current packet
            if (hasReceivedInput)
            {
                previousPacket = currentPacket;
                hasPreviousPacket = true;
            }

            currentPacket = packet;
            hasReceivedInput = true;

            // Add to history
            packetHistory.Enqueue(packet);
            while (packetHistory.Count > maxHistorySize)
            {
                packetHistory.Dequeue();
            }
        }

        // Public accessors
        public bool GetButton(InputButton button)
        {
            if (!hasReceivedInput) return false;
            return currentPacket.GetButton(button);
        }

        public bool GetButtonDown(InputButton button)
        {
            if (!hasReceivedInput || !hasPreviousPacket) return false;
            
            // Button is pressed this packet but wasn't pressed last packet
            bool currentPressed = currentPacket.GetButton(button);
            bool previousPressed = previousPacket.GetButton(button);
            
            return currentPressed && !previousPressed;
        }

        public bool GetButtonUp(InputButton button)
        {
            if (!hasReceivedInput || !hasPreviousPacket) return false;
            
            // Button was pressed last packet but isn't pressed this packet
            bool currentPressed = currentPacket.GetButton(button);
            bool previousPressed = previousPacket.GetButton(button);
            
            return !currentPressed && previousPressed;
        }

        public Vector2 GetMotion()
        {
            if (!hasReceivedInput) return Vector2.zero;
            return currentPacket.GetMotionVector();
        }

        public Vector2 GetView()
        {
            if (!hasReceivedInput) return Vector2.zero;
            return currentPacket.GetViewVector();
        }

        public Vector2 GetPad()
        {
            if (!hasReceivedInput) return Vector2.zero;
            return currentPacket.GetPadVector();
        }

        public InputPacket GetLatestPacket()
        {
            return currentPacket;
        }

        /// <summary>
        /// Get input history for replay/rollback
        /// </summary>
        public InputPacket[] GetHistory()
        {
            return packetHistory.ToArray();
        }

        /// <summary>
        /// Reset state (useful for snapshot restoration)
        /// </summary>
        public void Reset()
        {
            hasReceivedInput = false;
            hasPreviousPacket = false;
            currentPacket = default;
            previousPacket = default;
            packetHistory.Clear();
        }
    }
}