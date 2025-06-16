using UnityEngine;
using System.Collections.Generic;
using Unity.Entities;

namespace NoMoreEngine.Input
{
    /// <summary>
    /// Enhanced InputProcessor with Unity frame-based edge detection
    /// Processes raw InputPackets and provides frame-accurate button states
    /// </summary>
    public class InputProcessor : MonoBehaviour
    {
        [Header("Settings")]
        [SerializeField] private int inputHistorySize = 60;
        //[SerializeField] private bool debugLogging = false;

        // Input source
        private InputSerializer inputSerializer;

        // Per-player input states
        private Dictionary<byte, PlayerInputState> playerStates = new Dictionary<byte, PlayerInputState>();

        // Unity frame tracking
        private int lastProcessedFrame = -1;
        
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

        void Update()
        {
            // Process Unity frame edge detection
            if (Time.frameCount != lastProcessedFrame)
            {
                ProcessUnityFrameEdgeDetection();
                lastProcessedFrame = Time.frameCount;
            }
        }

        /// <summary>
        /// Process Unity frame-based edge detection
        /// This runs once per Unity frame to ensure accurate button up/down detection
        /// </summary>
        private void ProcessUnityFrameEdgeDetection()
        {
            foreach (var playerState in playerStates.Values)
            {
                playerState.ProcessUnityFrame();
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
        /// Get if button was pressed this Unity frame
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
        /// Get if button was released this Unity frame
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
    /// Enhanced player input state with Unity frame tracking
    /// </summary>
    public class PlayerInputState
    {
        private byte playerIndex;
        private int maxHistorySize;

        // Current input state (latest from packets)
        private InputPacket currentPacket;
        private bool hasReceivedInput = false;

        // Unity frame state tracking
        private int lastUnityFrame = -1;
        private ushort lastFrameButtons = 0;
        private ushort buttonsPressed = 0;
        private ushort buttonsReleased = 0;

        // Input history
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
            currentPacket = packet;
            hasReceivedInput = true;

            // Add to history
            packetHistory.Enqueue(packet);
            while (packetHistory.Count > maxHistorySize)
            {
                packetHistory.Dequeue();
            }
        }

        /// <summary>
        /// Process edge detection for current Unity frame
        /// </summary>
        public void ProcessUnityFrame()
        {
            if (!hasReceivedInput) return;

            int currentFrame = Time.frameCount;
            
            // First frame with this state
            if (lastUnityFrame != currentFrame - 1)
            {
                // Gap in frames, treat as fresh state
                buttonsPressed = 0;
                buttonsReleased = 0;
            }
            else
            {
                // Calculate edge detection
                buttonsPressed = (ushort)(~lastFrameButtons & currentPacket.buttons);
                buttonsReleased = (ushort)(lastFrameButtons & ~currentPacket.buttons);
            }

            lastFrameButtons = currentPacket.buttons;
            lastUnityFrame = currentFrame;
        }

        // Public accessors
        public bool GetButton(InputButton button)
        {
            if (!hasReceivedInput) return false;
            return currentPacket.GetButton(button);
        }

        public bool GetButtonDown(InputButton button)
        {
            if (!hasReceivedInput) return false;
            int buttonBit = 1 << (int)button;
            return (buttonsPressed & buttonBit) != 0;
        }

        public bool GetButtonUp(InputButton button)
        {
            if (!hasReceivedInput) return false;
            int buttonBit = 1 << (int)button;
            return (buttonsReleased & buttonBit) != 0;
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
    }
}