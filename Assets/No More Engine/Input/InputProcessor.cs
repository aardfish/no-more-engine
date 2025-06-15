using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using Unity.Entities;


namespace NoMoreEngine.Input
{
    /// <summary>
    /// InputProcessor - Centralized service for processing raw InputPackets
    /// Provides edge detection, input buffering, and other interpretation features
    /// Sits between InputSerializer and all input consumers
    /// </summary>
    public class InputProcessor : MonoBehaviour
    {
        [Header("Settings")]
        [SerializeField] private int inputHistorySize = 60; // 1 second at 60fps
        [SerializeField] private bool debugLogging = false;

        // Input source
        private InputSerializer inputSerializer;

        // Player input states
        private Dictionary<byte, PlayerInputState> playerStates = new Dictionary<byte, PlayerInputState>();

        // Events
        public System.Action<PlayerInputFrame[]> OnInputFramesReady;

        // Singleton for easy access
        private static InputProcessor instance;
        public static InputProcessor Instance => instance;

        void Awake()
        {
            // Simple singleton
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
        /// Process raw input packets and generate interpreted frames
        /// </summary>
        private void ProcessInputPackets(InputPacket[] packets)
        {
            var interpretedFrames = new List<PlayerInputFrame>();

            foreach (var packet in packets)
            {
                // Get or create player state
                if (!playerStates.ContainsKey(packet.playerIndex))
                {
                    playerStates[packet.playerIndex] = new PlayerInputState(packet.playerIndex, inputHistorySize);
                }

                var playerState = playerStates[packet.playerIndex];

                // Process the packet to generate interpreted frame
                var frame = playerState.ProcessPacket(packet);
                interpretedFrames.Add(frame);

                if (debugLogging && frame.HasAnyButtonPress())
                {
                    Debug.Log($"[InputProcessor] P{packet.playerIndex} pressed: {GetButtonNames(frame.ButtonsPressed)}");
                }
            }

            // Notify consumers
            OnInputFramesReady?.Invoke(interpretedFrames.ToArray());
        }

        /// <summary>
        /// Get the current input state for a specific player
        /// </summary>
        public PlayerInputFrame GetCurrentInput(byte playerIndex)
        {
            if (playerStates.ContainsKey(playerIndex))
            {
                return playerStates[playerIndex].CurrentFrame;
            }
            return new PlayerInputFrame();
        }

        /// <summary>
        /// Get input history for a specific player
        /// </summary>
        public InputPacket[] GetInputHistory(byte playerIndex, int frameCount)
        {
            if (playerStates.ContainsKey(playerIndex))
            {
                return playerStates[playerIndex].GetHistory(frameCount);
            }
            return new InputPacket[0];
        }

        /// <summary>
        /// Check if a button was pressed within a time window (for input buffering)
        /// </summary>
        public bool WasButtonPressedRecently(byte playerIndex, InputButton button, float timeWindow)
        {
            if (!playerStates.ContainsKey(playerIndex))
                return false;

            int framesToCheck = Mathf.CeilToInt(timeWindow * 60f); // Assuming 60fps
            return playerStates[playerIndex].WasButtonPressedInLastFrames(button, framesToCheck);
        }

        // Remove these fields - we don't need to track entities here
        // private Dictionary<byte, Entity> playerEntities = new Dictionary<byte, Entity>();
        // private Dictionary<Entity, byte> entityToPlayer = new Dictionary<Entity, byte>();

        // Simplify RegisterPlayerEntity to just handle input state
        public void RegisterPlayerEntity(Entity entity, byte playerIndex)
        {
            if (!playerStates.ContainsKey(playerIndex))
            {
                Debug.Log($"[InputProcessor] Creating new input state for player {playerIndex}");
                playerStates[playerIndex] = new PlayerInputState(playerIndex, inputHistorySize);
            }
            
            Debug.Log($"[InputProcessor] Input state ready for player {playerIndex}");
        }   

        private string GetButtonNames(ushort buttonMask)
        {
            var names = new List<string>();
            for (int i = 0; i < 12; i++)
            {
                if ((buttonMask & (1 << i)) != 0)
                {
                    names.Add(((InputButton)i).ToString());
                }
            }
            return string.Join(", ", names);
        }
    }

    /// <summary>
    /// Interpreted input frame with edge detection and other processed data
    /// </summary>
    public struct PlayerInputFrame
    {
        public byte PlayerIndex;
        public uint FrameNumber;

        // Raw input data
        public byte MotionAxis;
        public short ViewAxisX;
        public short ViewAxisY;
        public byte Pad;
        public ushort Buttons;

        // Edge detection
        public ushort ButtonsPressed;
        public ushort ButtonsReleased;

        // Axis changes
        public bool MotionChanged;
        public bool PadChanged;
        public bool ViewChanged;

        // Convenience methods
        public Vector2 GetMotionVector() => InputPacket.NumpadToVector2(MotionAxis);
        public Vector2 GetPadVector() => InputPacket.NumpadToVector2(Pad);
        public Vector2 GetViewVector() => new Vector2(ViewAxisX / 32767f, ViewAxisY / 32767f);

        public bool GetButton(InputButton button) => (Buttons & (1 << (int)button)) != 0;
        public bool GetButtonDown(InputButton button) => (ButtonsPressed & (1 << (int)button)) != 0;
        public bool GetButtonUp(InputButton button) => (ButtonsReleased & (1 << (int)button)) != 0;

        public bool HasAnyButtonPress() => ButtonsPressed != 0;
        public bool HasAnyButtonRelease() => ButtonsReleased != 0;
        public bool HasAnyInput() => MotionAxis != 5 || Pad != 5 || Buttons != 0 || ViewAxisX != 0 || ViewAxisY != 0;
    }

    /// <summary>
    /// Per-player input state tracking
    /// </summary>
    public class PlayerInputState
    {
        private byte playerIndex;
        private Queue<InputPacket> history;
        private int maxHistorySize;
        private InputPacket previousPacket;
        private bool hasReceivedInput = false;

        public PlayerInputFrame CurrentFrame { get; private set; }

        public PlayerInputState(byte playerIndex, int historySize)
        {
            this.playerIndex = playerIndex;
            this.maxHistorySize = historySize;
            this.history = new Queue<InputPacket>(historySize);
        }

        public PlayerInputFrame ProcessPacket(InputPacket packet)
        {
            // Create interpreted frame
            var frame = new PlayerInputFrame
            {
                PlayerIndex = packet.playerIndex,
                FrameNumber = packet.frameNumber,
                MotionAxis = packet.motionAxis,
                ViewAxisX = packet.viewAxisX,
                ViewAxisY = packet.viewAxisY,
                Pad = packet.pad,
                Buttons = packet.buttons
            };

            // Calculate edge detection
            if (hasReceivedInput)
            {
                // Button edges
                frame.ButtonsPressed = (ushort)(~previousPacket.buttons & packet.buttons);
                frame.ButtonsReleased = (ushort)(previousPacket.buttons & ~packet.buttons);

                // Axis changes
                frame.MotionChanged = packet.motionAxis != previousPacket.motionAxis;
                frame.PadChanged = packet.pad != previousPacket.pad;
                frame.ViewChanged = packet.viewAxisX != previousPacket.viewAxisX || 
                                   packet.viewAxisY != previousPacket.viewAxisY;
            }
            else
            {
                // First frame - treat held buttons as pressed
                frame.ButtonsPressed = packet.buttons;
                frame.MotionChanged = packet.motionAxis != 5;
                frame.PadChanged = packet.pad != 5;
                frame.ViewChanged = packet.viewAxisX != 0 || packet.viewAxisY != 0;
            }

            // Update history
            history.Enqueue(packet);
            while (history.Count > maxHistorySize)
            {
                history.Dequeue();
            }

            // Update state
            previousPacket = packet;
            hasReceivedInput = true;
            CurrentFrame = frame;

            return frame;
        }

        public InputPacket[] GetHistory(int frameCount)
        {
            var historyArray = history.ToArray();
            int startIndex = Mathf.Max(0, historyArray.Length - frameCount);
            int length = Mathf.Min(frameCount, historyArray.Length);

            var result = new InputPacket[length];
            System.Array.Copy(historyArray, startIndex, result, 0, length);
            return result;
        }

        public bool WasButtonPressedInLastFrames(InputButton button, int frameCount)
        {
            var historyArray = history.ToArray();
            int startIndex = Mathf.Max(0, historyArray.Length - frameCount);

            for (int i = startIndex; i < historyArray.Length; i++)
            {
                // Check for press edge
                if (i > 0)
                {
                    bool prevPressed = historyArray[i - 1].GetButton(button);
                    bool currPressed = historyArray[i].GetButton(button);
                    if (!prevPressed && currPressed)
                        return true;
                }
                else if (historyArray[i].GetButton(button))
                {
                    // First frame in our check, count as pressed if held
                    return true;
                }
            }

            return false;
        }
    }
}