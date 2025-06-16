using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;
using System.Linq;

namespace NoMoreEngine.Input
{
    /// <summary>
    /// Input Serializer - Converts Unity Input System data into deterministic input packets
    /// Handles input buffering, device management, and context switching
    /// </summary>
    public class InputSerializer : MonoBehaviour
    {
        [Header("Input Settings")]
        [SerializeField] private InputActionAsset inputActionAsset;
        [SerializeField] private InputContext currentContext = InputContext.InGame;
        [SerializeField] private int maxBufferedSamples = 5;
        [SerializeField] private bool debugLogging = false;

        [Header("View Axis Settings")]
        [SerializeField] private float mouseSensitivity = 0.1f;  // Much lower for mouse delta
        [SerializeField] private float gamepadSensitivity = 1.0f;
        [SerializeField] private short maxViewAxisValue = 32767;
        [SerializeField] private bool debugMouseInput = false;

        // Input device management
        private UnityEngine.InputSystem.PlayerInput primaryPlayerInput;
        private List<UnityEngine.InputSystem.PlayerInput> localPlayers = new List<UnityEngine.InputSystem.PlayerInput>();

        // Input buffering
        private Dictionary<byte, Queue<InputSample>> playerInputBuffers = new Dictionary<byte, Queue<InputSample>>();
        private Dictionary<byte, InputPacket> lastKnownInput = new Dictionary<byte, InputPacket>();

        // Frame tracking
        private uint currentFrameNumber = 0;
        private float lastSimulationTime = 0f;
        private const float SIMULATION_FIXED_TIMESTEP = 1f / 60f; // 60fps

        // Events
        public System.Action<InputPacket[]> OnInputPacketsReady;

        void Awake()
        {
            // Load Input Actions asset if not assigned
            if (inputActionAsset == null)
            {
                inputActionAsset = Resources.Load<InputActionAsset>("GameInputActions");
                if (inputActionAsset == null)
                {
                    Debug.LogError("[InputSerializer] No Input Actions asset assigned and couldn't load 'GameInputActions' from Resources!");
                    return;
                }
            }

            // Initialize primary player
            primaryPlayerInput = GetComponent<UnityEngine.InputSystem.PlayerInput>();
            if (primaryPlayerInput == null)
            {
                primaryPlayerInput = gameObject.AddComponent<UnityEngine.InputSystem.PlayerInput>();
            }

            // Assign the input actions asset
            primaryPlayerInput.actions = inputActionAsset;

            SetupPlayerInput(primaryPlayerInput, 0);
            RegisterPlayer(primaryPlayerInput, 0);

            // Set initial context
            SwitchActionMap(currentContext);
        }

        void Update()
        {
            // Sample input every Unity frame
            SampleAllPlayerInput();

            // Check if it's time for a simulation tick
            if (Time.time >= lastSimulationTime + SIMULATION_FIXED_TIMESTEP)
            {
                GenerateInputPacketsForSimulation();
                lastSimulationTime = Time.time;
                currentFrameNumber++;
            }
        }

        /// <summary>
        /// Setup input action references for a player
        /// </summary>
        private void SetupPlayerInput(UnityEngine.InputSystem.PlayerInput playerInput, byte playerIndex)
        {
            if (inputActionAsset != null)
            {
                playerInput.actions = inputActionAsset;
                playerInput.actions.Enable();
            }

            if (debugLogging)
                Debug.Log($"[InputSerializer] Setup player input for player {playerIndex}");
        }

        /// <summary>
        /// Register a new local player
        /// </summary>
        public void RegisterPlayer(UnityEngine.InputSystem.PlayerInput playerInput, byte playerIndex)
        {
            if (!localPlayers.Contains(playerInput))
            {
                localPlayers.Add(playerInput);
            }

            if (!playerInputBuffers.ContainsKey(playerIndex))
            {
                playerInputBuffers[playerIndex] = new Queue<InputSample>();
                lastKnownInput[playerIndex] = new InputPacket(0, 0, 0, 0, 0, currentFrameNumber, playerIndex);
            }

            if (debugLogging)
                Debug.Log($"[InputSerializer] Registered player {playerIndex}");
        }

        /// <summary>
        /// Sample input from all registered players
        /// </summary>
        private void SampleAllPlayerInput()
        {
            for (int i = 0; i < localPlayers.Count; i++)
            {
                if (localPlayers[i] != null)
                {
                    var inputPacket = SamplePlayerInput(localPlayers[i], (byte)i);
                    BufferInputSample(inputPacket, Time.time);
                }
            }
        }

        /// <summary>
        /// Sample input from a specific player and convert to InputPacket
        /// </summary>
        private InputPacket SamplePlayerInput(UnityEngine.InputSystem.PlayerInput playerInput, byte playerIndex)
        {
            // Motion Axis (WASD/Left Stick)
            Vector2 motionInput = Vector2.zero;
            if (playerInput.actions != null)
            {
                var motionAction = playerInput.actions["Motion"];
                if (motionAction != null)
                    motionInput = motionAction.ReadValue<Vector2>();
            }
            byte motionAxis = InputPacket.Vector2ToNumpad(motionInput);

            // View Axis (Mouse/Right Stick) 
            Vector2 viewInput = Vector2.zero;
            if (playerInput.actions != null)
            {
                var viewAction = playerInput.actions["View"];
                if (viewAction != null)
                {
                    Vector2 rawInput = viewAction.ReadValue<Vector2>();

                    // Debug raw input if enabled
                    if (debugMouseInput && (rawInput.x != 0 || rawInput.y != 0))
                    {
                        Debug.Log($"[InputSerializer] Raw view input: ({rawInput.x:F4}, {rawInput.y:F4})");
                    }

                    // Apply sensitivity based on device
                    var device = playerInput.devices.FirstOrDefault();
                    if (device is Mouse)
                    {
                        // For mouse delta, apply sensitivity BEFORE scaling to maxViewAxisValue
                        viewInput = rawInput * mouseSensitivity;

                        if (debugMouseInput && (viewInput.x != 0 || viewInput.y != 0))
                        {
                            Debug.Log($"[InputSerializer] Mouse after sensitivity: ({viewInput.x:F4}, {viewInput.y:F4})");
                        }
                    }
                    else if (device is Gamepad)
                    {
                        // Gamepad is already -1 to 1, just apply sensitivity
                        viewInput = rawInput * gamepadSensitivity;
                    }
                    else
                    {
                        // Fallback: treat as mouse input if device detection fails
                        viewInput = rawInput * mouseSensitivity;
                    }
                }
            }

            // Convert to fixed precision - viewInput should now be in reasonable range
            short viewAxisX = (short)Mathf.Clamp(viewInput.x * maxViewAxisValue, -maxViewAxisValue, maxViewAxisValue);
            short viewAxisY = (short)Mathf.Clamp(viewInput.y * maxViewAxisValue, -maxViewAxisValue, maxViewAxisValue);

            // Pad (D-pad/Arrow Keys)
            Vector2 padInput = Vector2.zero;
            if (playerInput.actions != null)
            {
                var padAction = playerInput.actions["Pad"];
                if (padAction != null)
                    padInput = padAction.ReadValue<Vector2>();
            }
            byte pad = InputPacket.Vector2ToNumpad(padInput);

            // Buttons
            ushort buttons = 0;
            for (int i = 0; i < 12; i++)
            {
                InputButton button = (InputButton)i;
                bool pressed = IsButtonPressed(playerInput, button);
                if (pressed)
                {
                    buttons |= (ushort)(1 << i);
                }
            }

            return new InputPacket(motionAxis, viewAxisX, viewAxisY, pad, buttons, currentFrameNumber, playerIndex);
        }

        /// <summary>
        /// Check if a specific button is pressed for a player
        /// </summary>
        private bool IsButtonPressed(UnityEngine.InputSystem.PlayerInput playerInput, InputButton button)
        {
            if (playerInput.actions == null) return false;

            string actionName = GetActionNameForButton(button);
            var action = playerInput.actions[actionName];

            return action != null && action.IsPressed();
        }

        /// <summary>
        /// Get input action name for a button based on current context
        /// </summary>
        private string GetActionNameForButton(InputButton button)
        {
            // Maps button enum to actual action names in the Input Actions asset
            // Different names for InGame vs Menu action maps
            return button switch
            {
                InputButton.Action1 => currentContext == InputContext.InGame ? "Action1" : "Confirm",
                InputButton.Action2 => currentContext == InputContext.InGame ? "Action2" : "Cancel",
                InputButton.Action3 => currentContext == InputContext.InGame ? "Action3" : "Option",
                InputButton.Action4 => currentContext == InputContext.InGame ? "Action4" : "Info",
                InputButton.Action5 => currentContext == InputContext.InGame ? "Action5" : "TabLeft",
                InputButton.Action6 => currentContext == InputContext.InGame ? "Action6" : "TabRight",
                InputButton.Trigger2 => currentContext == InputContext.InGame ? "Trigger2" : "MenuLeft",
                InputButton.Trigger1 => currentContext == InputContext.InGame ? "Trigger1" : "MenuRight",
                InputButton.Aux1 => "Aux1",
                InputButton.Aux2 => "Aux2",
                InputButton.Menu1 => currentContext == InputContext.InGame ? "Menu1" : "Exit",
                InputButton.Menu2 => currentContext == InputContext.InGame ? "Menu2" : "SubMenu",
                _ => ""
            };
        }

        /// <summary>
        /// Buffer an input sample for later consumption by simulation
        /// </summary>
        private void BufferInputSample(InputPacket packet, float timestamp)
        {
            var buffer = playerInputBuffers[packet.playerIndex];
            buffer.Enqueue(new InputSample(packet, timestamp));

            // Keep buffer size manageable
            while (buffer.Count > maxBufferedSamples)
            {
                buffer.Dequeue();
            }
        }

        /// <summary>
        /// Generate input packets for simulation consumption (called at 60fps)
        /// </summary>
        private void GenerateInputPacketsForSimulation()
        {
            var inputPackets = new List<InputPacket>();

            foreach (var kvp in playerInputBuffers)
            {
                byte playerIndex = kvp.Key;
                var buffer = kvp.Value;

                // Get the most recent unconsumed input
                InputSample? latestSample = null;
                var samples = buffer.ToArray();

                for (int i = samples.Length - 1; i >= 0; i--)
                {
                    if (!samples[i].consumed)
                    {
                        latestSample = samples[i];
                        break;
                    }
                }

                InputPacket packetToUse;
                if (latestSample.HasValue)
                {
                    packetToUse = latestSample.Value.packet;
                    packetToUse.frameNumber = currentFrameNumber;

                    // Mark this sample as consumed
                    var tempSamples = buffer.ToArray();
                    buffer.Clear();
                    foreach (var sample in tempSamples)
                    {
                        var updatedSample = sample;
                        if (sample.timestamp == latestSample.Value.timestamp)
                            updatedSample.consumed = true;
                        buffer.Enqueue(updatedSample);
                    }
                }
                else
                {
                    // No new input, use last known input
                    packetToUse = lastKnownInput[playerIndex];
                    packetToUse.frameNumber = currentFrameNumber;
                }

                lastKnownInput[playerIndex] = packetToUse;
                inputPackets.Add(packetToUse);
            }

            // Send to simulation
            OnInputPacketsReady?.Invoke(inputPackets.ToArray());

            if (debugLogging)
                Debug.Log($"[InputSerializer] Generated {inputPackets.Count} input packets for frame {currentFrameNumber}");
        }

        /// <summary>
        /// Switch input context (game vs menu)
        /// </summary>
        public void SetInputContext(InputContext context)
        {
            currentContext = context;
            SwitchActionMap(context);

            if (debugLogging)
                Debug.Log($"[InputSerializer] Switched to {context} context");
        }

        /// <summary>
        /// Switch the active action map for all players
        /// </summary>
        private void SwitchActionMap(InputContext context)
        {
            string actionMapName = context == InputContext.InGame ? "InGame" : "Menu";

            foreach (var playerInput in localPlayers)
            {
                if (playerInput != null && playerInput.actions != null)
                {
                    // Disable current action map
                    playerInput.actions.Disable();

                    // Switch to the new action map
                    playerInput.SwitchCurrentActionMap(actionMapName);

                    // Re-enable actions
                    playerInput.actions.Enable();
                }
            }

            if (debugLogging)
                Debug.Log($"[InputSerializer] Switched all players to '{actionMapName}' action map");
        }

        /// <summary>
        /// Get the latest input packet for a specific player (for debugging)
        /// </summary>
        public InputPacket GetLatestInputForPlayer(byte playerIndex)
        {
            return lastKnownInput.ContainsKey(playerIndex) ? lastKnownInput[playerIndex] : new InputPacket();
        }

        /// <summary>
        /// Public API to get current frame number
        /// </summary>
        public uint GetCurrentFrameNumber() => currentFrameNumber;
    }
}