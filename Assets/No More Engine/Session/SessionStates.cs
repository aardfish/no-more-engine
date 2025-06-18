using UnityEngine;
using System;
using NoMoreEngine.Input;
using NoMoreEngine.Simulation.Bridge;

namespace NoMoreEngine.Session
{
    /// <summary>
    /// Base class for session states with proper separation of input and visuals
    /// Input is handled at simulation tick rate, visuals at Unity frame rate
    /// </summary>
    public abstract class SessionStateBase : ISessionState
    {
        protected SessionContext context;
        protected float stateTime;

        public virtual void Initialize(SessionContext context)
        {
            this.context = context;
        }

        public virtual void OnEnter()
        {
            stateTime = 0f;
        }

        public virtual void OnExit()
        {
            // Clean exit
        }

        // Called at simulation tick rate (60Hz)
        public abstract void HandleInput();

        // Called at Unity frame rate (for animations, interpolation, etc.)
        public virtual void UpdateVisuals(float deltaTime)
        {
            stateTime += deltaTime;
            // Override in derived classes if needed for visual updates
        }
    }

    /// <summary>
    /// Main Menu State - Entry point
    /// </summary>
    public class MainMenuState : SessionStateBase
    {
        public override void HandleInput()
        {
            // Check for any player wanting to start
            if (NoMoreInput.AnyButtonDown(InputButton.Action1) || 
                NoMoreInput.AnyButtonDown(InputButton.Menu1))
            {
                StartQuickMatch();
            }
        }

        private void StartQuickMatch()
        {
            Debug.Log("[MainMenu] Going to mission lobby...");

            // Clear any existing players
            context.gameConfig.ClearAllPlayers();

            // Go to mission lobby
            context.coordinator.TransitionTo(SessionStateType.MissionLobby);
        }
    }

    /// <summary>
    /// Mission Lobby State - Simple single-player mission configuration
    /// </summary>
    public class MissionLobbyState : SessionStateBase
    {
        // Available stages (hardcoded for now)
        private readonly string[] availableStages =
        {
            "TestArena",
            "SmallBox",
            "LargePlatform"
        };

        private int selectedStageIndex = 0;
        private float readyCountdown = -1f; // -1 = not counting down
        private const float COUNTDOWN_DURATION = 3f;

        // Navigation repeat handling (at tick rate)
        private int ticksSinceLastNavigation = 0;
        private const int NAVIGATION_REPEAT_TICKS = 9; // ~150ms at 60Hz

        public override void OnEnter()
        {
            base.OnEnter();

            Debug.Log("[MissionLobby] Entered mission lobby");

            // Ensure we have at least one player
            if (context.gameConfig.GetActivePlayerCount() == 0)
            {
                context.gameConfig.AddPlayer(PlayerType.Local, 0);
            }

            // Reset state
            readyCountdown = -1f;
            ticksSinceLastNavigation = 0;
            selectedStageIndex = 0;
            UpdateGameConfig();
        }

        public override void HandleInput()
        {
            var player = NoMoreInput.Player1;

            // Cancel countdown on any button press
            if (readyCountdown >= 0f && player.PressedAny())
            {
                Debug.Log("[MissionLobby] Countdown cancelled");
                readyCountdown = -1f;
                return;
            }

            // Handle navigation with tick-based repeat prevention
            ticksSinceLastNavigation++;
            
            if (ticksSinceLastNavigation >= NAVIGATION_REPEAT_TICKS)
            {
                // Use convenience navigation properties
                if (player.NavigateUp && selectedStageIndex > 0)
                {
                    selectedStageIndex--;
                    UpdateGameConfig();
                    ticksSinceLastNavigation = 0;
                    Debug.Log($"[MissionLobby] Selected: {availableStages[selectedStageIndex]}");
                }
                else if (player.NavigateDown && selectedStageIndex < availableStages.Length - 1)
                {
                    selectedStageIndex++;
                    UpdateGameConfig();
                    ticksSinceLastNavigation = 0;
                    Debug.Log($"[MissionLobby] Selected: {availableStages[selectedStageIndex]}");
                }
            }

            // Start mission using convenience property
            if (player.Confirm)
            {
                StartCountdown();
            }

            // Back to menu using convenience property
            if (player.Cancel)
            {
                context.coordinator.TransitionTo(SessionStateType.MainMenu);
            }
        }

        public override void UpdateVisuals(float deltaTime)
        {
            base.UpdateVisuals(deltaTime);

            // Update countdown timer
            if (readyCountdown >= 0f)
            {
                readyCountdown -= deltaTime;

                if (readyCountdown <= 0f)
                {
                    LaunchMission();
                }
            }
        }

        private void UpdateGameConfig()
        {
            context.gameConfig.stageName = availableStages[selectedStageIndex];
            context.gameConfig.gameMode = GameMode.Mission;
            context.gameConfig.maxActivePlayers = 1;
        }

        private void StartCountdown()
        {
            Debug.Log($"[MissionLobby] Starting countdown for: {context.gameConfig.stageName}");
            readyCountdown = COUNTDOWN_DURATION;
        }

        private void LaunchMission()
        {
            Debug.Log($"[MissionLobby] Launching mission: {context.gameConfig.stageName}");
            context.coordinator.TransitionTo(SessionStateType.InGame);
        }

        // Public API for UI
        public float GetCountdown() => readyCountdown;
        public bool IsCountingDown => readyCountdown >= 0f;
        public string GetSelectedStage() => availableStages[selectedStageIndex];
        public int GetSelectedStageIndex() => selectedStageIndex;
        public string[] GetAvailableStages() => availableStages;
    }

    /// <summary>
    /// In-Game State - Active gameplay
    /// </summary>
    public class InGameState : SessionStateBase
    {
        private SimulationInitializer simulationInit;
        private SimulationController simulationControl;
        private InputRecorder inputRecorder;
        private bool simulationActive = false;
        private bool createdBridgeObjects = false;

        public override void OnEnter()
        {
            base.OnEnter();

            // Get input recorder reference
            inputRecorder = InputRecorder.Instance;

            // Create simulation bridge components
            CreateSimulationBridge();

            // Start the simulation
            StartSimulation();

            // start recording input
            inputRecorder?.StartRecording();
        }

        public override void OnExit()
        {
            // Stop recording and get the recording data
            var recording = inputRecorder?.StopRecording();

            // Store recording for Results state to handle
            if (recording != null && recording.FrameCount > 0)
            {
                context.lastRecording = recording;
            }

            // Stop simulation
            if (simulationActive)
            {
                StopSimulation();
            }

            // Clean up bridge objects only if we created them
            if (createdBridgeObjects)
            {
                if (simulationInit != null)
                {
                    GameObject.Destroy(simulationInit.gameObject);
                    simulationInit = null;
                }

                if (simulationControl != null)
                {
                    GameObject.Destroy(simulationControl.gameObject);
                    simulationControl = null;
                }

                createdBridgeObjects = false;
            }
            else
            {
                // Just clear references if we didn't create them
                simulationInit = null;
                simulationControl = null;
            }

            base.OnExit();
        }

        public override void HandleInput()
        {
            // Check all players for pause menu
            if (NoMoreInput.AnyButtonDown(InputButton.Menu1))
            {
                EndGame();
            }
        }

        private void CreateSimulationBridge()
        {
            // Find existing components first
            if (simulationInit == null)
            {
                simulationInit = GameObject.FindAnyObjectByType<SimulationInitializer>();
            }

            if (simulationControl == null)
            {
                simulationControl = GameObject.FindAnyObjectByType<SimulationController>();
            }

            // Only create if not found
            if (simulationInit == null)
            {
                Debug.Log("[InGame] Creating SimulationInitializer");
                var initObject = new GameObject("SimulationInitializer");
                simulationInit = initObject.AddComponent<SimulationInitializer>();
                createdBridgeObjects = true;
            }

            if (simulationControl == null)
            {
                Debug.Log("[InGame] Creating SimulationController");
                var controlObject = new GameObject("SimulationController");
                simulationControl = controlObject.AddComponent<SimulationController>();
                createdBridgeObjects = true;
            }
        }

        private void StartSimulation()
        {
            Debug.Log("[InGame] Starting simulation...");

            // Initialize the match
            if (simulationInit.InitializeMatch(context.gameConfig))
            {
                simulationActive = true;
                simulationControl.StartSimulation();
            }
            else
            {
                Debug.LogError("[InGame] Failed to initialize match!");
                context.coordinator.TransitionTo(SessionStateType.MainMenu);
            }
        }

        private void StopSimulation()
        {
            Debug.Log("[InGame] Stopping simulation...");

            simulationControl.StopSimulation();
            simulationInit.CleanupMatch();
            simulationActive = false;
        }

        private void EndGame()
        {
            Debug.Log("[InGame] Game ended by player");
            context.coordinator.TransitionTo(SessionStateType.Results);
        }
    }

    /// <summary>
    /// Results State - Post-game screen
    /// </summary>
    public class ResultsState : SessionStateBase
    {
        private float minDisplayTime = 1f; // Minimum time before allowing exit
        private bool showingSavePrompt = false;
        private InputRecording pendingRecording;

        public override void OnEnter()
        {
            base.OnEnter();
            Debug.Log("[Results] Showing results screen");

            // Check if we have a recording to save
            pendingRecording = context.lastRecording;
            context.lastRecording = null;

            if (pendingRecording != null && pendingRecording.FrameCount > 0)
            {
                showingSavePrompt = true;
                Debug.Log($"[Results] Have recording with {pendingRecording.FrameCount} frames");
            }
        }

        public override void HandleInput()
        {
            // Wait a minimum time before allowing exit
            if (stateTime < minDisplayTime) return;

            if (showingSavePrompt && pendingRecording != null)
            {
                if (NoMoreInput.Player1.GetButtonDown(InputButton.Action1))
                {
                    SaveRecording();
                }
                else if (NoMoreInput.Player1.GetButtonDown(InputButton.Action2))
                {
                    DiscardRecording();
                }
            }
            else
            {
                // Check any player wanting to continue
                if (NoMoreInput.AnyButtonDown(InputButton.Action1) ||
                    NoMoreInput.AnyButtonDown(InputButton.Action2) ||
                    NoMoreInput.AnyButtonDown(InputButton.Menu1))
                {
                    ReturnToMenu();
                }
            }
        }

        private void SaveRecording()
        {
            string filename = $"Replay_{DateTime.Now:yyyyMMdd_HHmmss}";
            InputRecorder.SaveRecording(pendingRecording, filename);
            showingSavePrompt = false;
            pendingRecording = null;
        }

        private void DiscardRecording()
        {
            Debug.Log($"[Results] Discarded recording ({pendingRecording.FrameCount} frames)");
            showingSavePrompt = false;
            pendingRecording = null;
        }

        // Public getter for UI
        public bool HasPendingRecording => showingSavePrompt && pendingRecording != null;
        public int PendingRecordingFrames => pendingRecording?.FrameCount ?? 0;
        public float PendingRecordingDuration => pendingRecording?.DurationSeconds ?? 0f;

        private void ReturnToMenu()
        {
            Debug.Log("[Results] Returning to main menu");
            context.coordinator.TransitionTo(SessionStateType.MainMenu);
        }
    }

    /// <summary>
    /// Pause State - Game paused
    /// </summary>
    public class PauseState : SessionStateBase
    {
        private SessionStateType previousState;

        public void SetPreviousState(SessionStateType state)
        {
            previousState = state;
        }

        public override void OnEnter()
        {
            base.OnEnter();
            Debug.Log("[Pause] Game paused");
            
            // Pause the simulation
            var simControl = GameObject.FindAnyObjectByType<SimulationController>();
            simControl?.PauseSimulation();
        }

        public override void OnExit()
        {
            base.OnExit();
            
            // Resume the simulation
            var simControl = GameObject.FindAnyObjectByType<SimulationController>();
            simControl?.ResumeSimulation();
        }

        public override void HandleInput()
        {
            var player = NoMoreInput.Player1;

            // Resume on menu button or cancel
            if (player.GetButtonDown(InputButton.Menu1) || player.Cancel)
            {
                Resume();
            }

            // Quit to menu on specific button
            if (player.GetButtonDown(InputButton.Menu2))
            {
                QuitToMenu();
            }
        }

        private void Resume()
        {
            Debug.Log("[Pause] Resuming game");
            context.coordinator.TransitionTo(previousState);
        }

        private void QuitToMenu()
        {
            Debug.Log("[Pause] Quitting to main menu");
            context.coordinator.TransitionTo(SessionStateType.MainMenu);
        }
    }

    /// <summary>
    /// Versus Lobby State - Multiplayer setup
    /// </summary>
    public class VersusLobbyState : SessionStateBase
    {
        private int[] playerJoinTickTimers = new int[4];
        private const int JOIN_COOLDOWN_TICKS = 30; // 0.5s at 60Hz

        public override void OnEnter()
        {
            base.OnEnter();
            Debug.Log("[VersusLobby] Entered versus lobby");
            
            // Reset timers
            for (int i = 0; i < 4; i++)
            {
                playerJoinTickTimers[i] = 0;
            }
        }

        public override void HandleInput()
        {
            // Increment cooldown timers
            for (int i = 0; i < 4; i++)
            {
                if (playerJoinTickTimers[i] > 0)
                    playerJoinTickTimers[i]--;
            }

            // Check each player for join/leave
            for (byte i = 0; i < 4; i++)
            {
                var player = NoMoreInput.GetPlayer(i);
                var slot = context.gameConfig.playerSlots[i];
                
                if (playerJoinTickTimers[i] == 0)
                {
                    if (slot.IsEmpty)
                    {
                        // Join with Action1
                        if (player.GetButtonDown(InputButton.Action1))
                        {
                            JoinPlayer(i);
                            playerJoinTickTimers[i] = JOIN_COOLDOWN_TICKS;
                        }
                    }
                    else if (slot.IsLocal)
                    {
                        // Leave with Action2
                        if (player.GetButtonDown(InputButton.Action2))
                        {
                            LeavePlayer(i);
                            playerJoinTickTimers[i] = JOIN_COOLDOWN_TICKS;
                        }
                        
                        // Toggle ready with Action1
                        if (player.GetButtonDown(InputButton.Action1))
                        {
                            ToggleReady(i);
                            playerJoinTickTimers[i] = JOIN_COOLDOWN_TICKS;
                        }
                    }
                }
            }
            
            // P1 can start if all ready
            if (NoMoreInput.Player1.GetButtonDown(InputButton.Menu1))
            {
                if (context.gameConfig.AreAllPlayersReady())
                {
                    StartMatch();
                }
            }
            
            // P1 can go back
            if (NoMoreInput.Player1.Cancel)
            {
                context.coordinator.TransitionTo(SessionStateType.MainMenu);
            }
        }

        private void JoinPlayer(byte playerIndex)
        {
            Debug.Log($"[VersusLobby] Player {playerIndex + 1} joined");
            context.gameConfig.AddPlayer(PlayerType.Local, playerIndex);
        }

        private void LeavePlayer(byte playerIndex)
        {
            Debug.Log($"[VersusLobby] Player {playerIndex + 1} left");
            context.gameConfig.RemovePlayer(playerIndex);
        }

        private void ToggleReady(byte playerIndex)
        {
            var slot = context.gameConfig.playerSlots[playerIndex];
            slot.isReady = !slot.isReady;
            Debug.Log($"[VersusLobby] Player {playerIndex + 1} ready: {slot.isReady}");
        }

        private void StartMatch()
        {
            Debug.Log("[VersusLobby] Starting versus match");
            context.coordinator.TransitionTo(SessionStateType.InGame);
        }
    }
}