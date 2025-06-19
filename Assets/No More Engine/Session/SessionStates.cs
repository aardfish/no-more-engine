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
            // Quick start
            if (NoMoreInput.AnyButtonDown(InputButton.Action1) || 
                NoMoreInput.AnyButtonDown(InputButton.Menu1))
            {
                StartQuickMatch();
            }
            
            // Replay mode with Action3 (X/E)
            if (NoMoreInput.Player1.GetButtonDown(InputButton.Action3))
            {
                GoToReplayMode();
            }
        }

        private void StartQuickMatch()
        {
            Debug.Log("[MainMenu] Going to mission lobby...");
            context.gameConfig.ClearAllPlayers();
            context.coordinator.TransitionTo(SessionStateType.MissionLobby);
        }
        
        private void GoToReplayMode()
        {
            Debug.Log("[MainMenu] Going to replay mode...");
            context.coordinator.TransitionTo(SessionStateType.ReplayMode);
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
    /// In-Game State - Active gameplay (including replay mode)
    /// </summary>
    public class InGameState : SessionStateBase
    {
        private SimulationInitializer simulationInit;
        private SimulationController simulationControl;
        private InputRecorder inputRecorder;
        private NoMoreEngine.DevTools.InputReplayer inputReplayer;
        private bool simulationActive = false;
        private bool createdBridgeObjects = false;
        private bool createdReplayObjects = false;

        public override void OnEnter()
        {
            base.OnEnter();

            // Check if we're in replay mode
            if (context.isReplayActive)
            {
                Debug.Log($"[InGameState] Entering in REPLAY mode with {context.replayToPlay?.FrameCount ?? 0} frames");
                SetupReplayMode();
            }
            else
            {
                Debug.Log("[InGameState] Entering in NORMAL gameplay mode");
                // Get input recorder reference for normal gameplay
                inputRecorder = InputRecorder.Instance;
            }

            // Create simulation bridge components
            CreateSimulationBridge();

            // Start the simulation
            StartSimulation();

            // Start recording input only in normal mode
            if (!context.isReplayActive && inputRecorder != null)
            {
                inputRecorder.StartRecording();
            }
        }

        public override void OnExit()
        {
            if (context.isReplayActive)
            {
                // Clean up replay mode
                CleanupReplayMode();
            }
            else
            {
                // Stop recording and get the recording data
                var recording = inputRecorder?.StopRecording();

                Debug.Log($"[InGameState] OnExit - InputRecorder exists: {inputRecorder != null}");
                Debug.Log($"[InGameState] OnExit - Recording: {recording != null}, Frames: {recording?.FrameCount ?? 0}");

                // Store recording for Results state to handle
                if (recording != null && recording.FrameCount > 0)
                {
                    context.lastRecording = recording;
                    Debug.Log($"[InGameState] Stored recording in context with {recording.FrameCount} frames");
                }
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

            // Reset replay context flags
            context.isReplayActive = false;
            context.replayToPlay = null;
            context.captureForDeterminism = false;

            base.OnExit();
        }

        public override void HandleInput()
        {
            // In replay mode, check if replay has finished
            if (context.isReplayActive && inputReplayer != null && !inputReplayer.IsReplaying)
            {
                Debug.Log("[InGameState] Replay finished, returning to results");
                context.coordinator.TransitionTo(SessionStateType.Results);
                return;
            }

            // Check all players for pause menu (only in normal gameplay)
            if (!context.isReplayActive && NoMoreInput.AnyButtonDown(InputButton.Menu1))
            {
                EndGame();
            }
        }

        private void SetupReplayMode()
        {
            // Find or create InputReplayer
            inputReplayer = GameObject.FindAnyObjectByType<NoMoreEngine.DevTools.InputReplayer>();
            if (inputReplayer == null)
            {
                Debug.Log("[InGameState] Creating InputReplayer for replay mode");
                var replayerObject = new GameObject("InputReplayer");
                inputReplayer = replayerObject.AddComponent<NoMoreEngine.DevTools.InputReplayer>();
                createdReplayObjects = true;
            }

            // IMPORTANT: Ensure InputSerializer has registered players for replay
            // The InputSerializer needs to generate packets for the InputReplayer to override
            var inputSerializer = GameObject.FindAnyObjectByType<InputSerializer>();
            if (inputSerializer != null && context.replayToPlay != null && context.replayToPlay.FrameCount > 0)
            {
                // Get the first frame to see how many players we need
                var firstFrame = context.replayToPlay.frames[0];
                if (firstFrame.packets != null)
                {
                    Debug.Log($"[InGameState] Registering {firstFrame.packets.Length} dummy players for replay");
                    
                    // Register dummy players so InputSerializer generates packets
                    for (int i = 0; i < firstFrame.packets.Length; i++)
                    {
                        byte playerIndex = (byte)i;
                        inputSerializer.RegisterDummyPlayer(playerIndex);
                    }
                }
            }

            // Start the replay
            if (context.replayToPlay != null && inputReplayer != null)
            {
                inputReplayer.StartReplay(context.replayToPlay);
                Debug.Log($"[InGameState] Started replay with {context.replayToPlay.FrameCount} frames");
            }
            else
            {
                Debug.LogError("[InGameState] No recording available for replay!");
            }
        }

        private void CleanupReplayMode()
        {
            // Stop the replay
            if (inputReplayer != null)
            {
                inputReplayer.StopReplay();
                
                // Destroy if we created it
                if (createdReplayObjects)
                {
                    GameObject.Destroy(inputReplayer.gameObject);
                    createdReplayObjects = false;
                }
                
                inputReplayer = null;
            }
            
            // Clear dummy players from InputSerializer
            var inputSerializer = GameObject.FindAnyObjectByType<InputSerializer>();
            if (inputSerializer != null)
            {
                inputSerializer.ClearDummyPlayers();
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

            Debug.Log($"[Results] Pending recording: {pendingRecording != null}, Frames: {pendingRecording?.FrameCount ?? 0}");

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
    
    /// <summary>
    /// Replay Mode - Browse, play, and test replays for determinism
    /// </summary>
    public class ReplayModeState : SessionStateBase
    {
        // Replay browser
        private string[] availableReplays;
        private int selectedReplayIndex = 0;
        private InputRecording loadedRecording;
        
        // Test modes
        public enum ReplayTestMode
        {
            ViewOnly,
            DeterminismTest,
            CompareRuns
        }
        private ReplayTestMode currentMode = ReplayTestMode.ViewOnly;
        
        // Determinism test state
        private NoMoreEngine.DevTools.DeterminismTester determinismTester;
        private bool testInProgress = false;
        
        public override void OnEnter()
        {
            base.OnEnter();
            Debug.Log("[ReplayMode] Entered replay mode");
            
            // Load available replays
            RefreshReplayList();
            
            // Create determinism tester if needed
            if (determinismTester == null)
            {
                var tester = new GameObject("DeterminismTester");
                determinismTester = tester.AddComponent<NoMoreEngine.DevTools.DeterminismTester>();
            }
        }
        
        public override void OnExit()
        {
            // Clean up tester
            if (determinismTester != null && determinismTester.gameObject != null)
            {
                GameObject.Destroy(determinismTester.gameObject);
                determinismTester = null;
            }
            
            base.OnExit();
        }
        
        public override void HandleInput()
        {
            var player = NoMoreInput.Player1;
            
            // Back to main menu
            if (player.Cancel)
            {
                context.coordinator.TransitionTo(SessionStateType.MainMenu);
                return;
            }
            
            // Navigate replays
            if (availableReplays != null && availableReplays.Length > 0)
            {
                if (player.NavigateUp && selectedReplayIndex > 0)
                {
                    selectedReplayIndex--;
                    LoadReplayInfo();
                }
                else if (player.NavigateDown && selectedReplayIndex < availableReplays.Length - 1)
                {
                    selectedReplayIndex++;
                    LoadReplayInfo();
                }
            }
            
            // Mode selection
            if (player.GetButtonDown(InputButton.Action3)) // X/E
            {
                currentMode = (ReplayTestMode)(((int)currentMode + 1) % 3);
                Debug.Log($"[ReplayMode] Switched to {currentMode}");
            }
            
            // Start replay/test
            if (player.Confirm && !testInProgress && loadedRecording != null)
            {
                StartReplayTest();
            }
        }
        
        private void RefreshReplayList()
        {
            availableReplays = InputRecorder.GetAvailableReplays();
            if (availableReplays.Length > 0)
            {
                selectedReplayIndex = 0;
                LoadReplayInfo();
            }
            else
            {
                loadedRecording = null;
            }
        }
        
        private void LoadReplayInfo()
        {
            if (availableReplays == null || selectedReplayIndex >= availableReplays.Length)
                return;
            
            try
            {
                string replayPath = System.IO.Path.Combine(
                    Application.persistentDataPath, 
                    "Replays", 
                    availableReplays[selectedReplayIndex] + ".nmr"
                );
                
                using (var stream = System.IO.File.OpenRead(replayPath))
                using (var reader = new System.IO.BinaryReader(stream))
                {
                    loadedRecording = InputRecording.Deserialize(reader);
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[ReplayMode] Failed to load replay info: {e.Message}");
                loadedRecording = null;
            }
        }
        
        private void StartReplayTest()
        {
            testInProgress = true;
            
            switch (currentMode)
            {
                case ReplayTestMode.ViewOnly:
                    // Just play the replay
                    LaunchReplay(false);
                    break;
                    
                case ReplayTestMode.DeterminismTest:
                    // Play twice and compare
                    determinismTester.StartDeterminismTest(loadedRecording);
                    break;
                    
                case ReplayTestMode.CompareRuns:
                    // Compare against live play
                    determinismTester.StartComparisonTest(loadedRecording);
                    break;
            }
        }
        
        private void LaunchReplay(bool captureForTest)
        {
            // Configure game for replay
            context.gameConfig.ClearAllPlayers();
            context.gameConfig.AddPlayer(PlayerType.Local, 0); // TODO: Restore from replay metadata
            
            // Transition to game with replay active
            context.isReplayActive = true;
            context.replayToPlay = loadedRecording;
            context.captureForDeterminism = captureForTest;
            
            context.coordinator.TransitionTo(SessionStateType.InGame);
        }

        // Session context (for DeterminismTester access)
        public SessionContext GetContext()
        {
            return context;
        }
        
        // Public properties for UI
        public string[] AvailableReplays => availableReplays;
        public int SelectedIndex => selectedReplayIndex;
        public string SelectedReplayName => availableReplays?.Length > 0 ? 
            availableReplays[selectedReplayIndex] : null;
        public InputRecording LoadedRecording => loadedRecording;
        public ReplayTestMode CurrentMode => currentMode;
        public bool TestInProgress => testInProgress;
    }
}