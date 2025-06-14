using UnityEngine;
using System;
using NoMoreEngine.Input;
using NoMoreEngine.Simulation.Bridge;


namespace NoMoreEngine.Session
{
    /// <summary>
    /// Base class for session states with common functionality
    /// Uses centralized InputProcessor for all input processing
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

            // Subscribe to processed input
            var processor = InputProcessor.Instance;
            if (processor != null)
            {
                processor.OnInputFramesReady += HandleInputFrames;
            }
            else
            {
                Debug.LogError("[SessionState] InputProcessor not found! States require InputProcessor for input handling.");
            }
        }

        public virtual void OnExit()
        {
            // Unsubscribe from processed input
            var processor = InputProcessor.Instance;
            if (processor != null)
            {
                processor.OnInputFramesReady -= HandleInputFrames;
            }
        }

        public virtual void Update(float deltaTime)
        {
            stateTime += deltaTime;
        }

        protected virtual void HandleInputFrames(PlayerInputFrame[] frames)
        {
            // Handle P1 input by default
            if (frames.Length > 0)
            {
                HandleProcessedInput(frames[0]);
            }
        }

        // Derived classes implement this to handle processed input
        protected abstract void HandleProcessedInput(PlayerInputFrame input);
    }

    /// <summary>
    /// Main Menu State - Entry point
    /// </summary>
    public class MainMenuState : SessionStateBase
    {
        protected override void HandleProcessedInput(PlayerInputFrame input)
        {
            // Press Action1 (Space/A) or Menu1 (Enter/Start) to start
            if (input.GetButtonDown(InputButton.Action1) || input.GetButtonDown(InputButton.Menu1))
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

        // Navigation timing
        private float navigationCooldownTimer = 0f;
        private const float NAVIGATION_COOLDOWN = 0.15f;

        public override void OnEnter()
        {
            base.OnEnter();

            Debug.Log("[MissionLobby] Entered mission lobby");

            // Ensure we have at least one player
            if (context.gameConfig.GetActivePlayerCount() == 0)
            {
                context.gameConfig.AddPlayer(PlayerType.Local, 0);
            }

            // Reset countdown
            readyCountdown = -1f;
            navigationCooldownTimer = 0f;

            // Set default stage
            selectedStageIndex = 0;
            UpdateGameConfig();
        }

        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);

            // Update navigation cooldown
            if (navigationCooldownTimer > 0f)
            {
                navigationCooldownTimer -= deltaTime;
            }

            // Handle countdown
            if (readyCountdown >= 0f)
            {
                readyCountdown -= deltaTime;

                if (readyCountdown <= 0f)
                {
                    LaunchMission();
                }
            }
        }

        protected override void HandleProcessedInput(PlayerInputFrame input)
        {
            // Cancel countdown on any button press
            if (readyCountdown >= 0f && input.HasAnyButtonPress())
            {
                Debug.Log("[MissionLobby] Countdown cancelled");
                readyCountdown = -1f;
                return;
            }

            // Handle navigation with cooldown
            if (navigationCooldownTimer <= 0f)
            {
                Vector2 navigationInput = Vector2.zero;

                // Prioritize D-pad/arrows for menu navigation
                if (input.Pad != 5)
                {
                    navigationInput = input.GetPadVector();
                }
                // Fall back to motion axis (WASD/left stick) if no pad input
                else if (input.MotionAxis != 5)
                {
                    navigationInput = input.GetMotionVector();
                }

                // Navigate stages vertically
                if (navigationInput.y > 0.5f && selectedStageIndex > 0)
                {
                    selectedStageIndex--;
                    UpdateGameConfig();
                    navigationCooldownTimer = NAVIGATION_COOLDOWN;
                    Debug.Log($"[MissionLobby] Selected: {availableStages[selectedStageIndex]}");
                }
                else if (navigationInput.y < -0.5f && selectedStageIndex < availableStages.Length - 1)
                {
                    selectedStageIndex++;
                    UpdateGameConfig();
                    navigationCooldownTimer = NAVIGATION_COOLDOWN;
                    Debug.Log($"[MissionLobby] Selected: {availableStages[selectedStageIndex]}");
                }
            }

            // Launch mission with Action1 (Space/A)
            if (input.GetButtonDown(InputButton.Action1))
            {
                StartCountdown();
            }

            // Alternative confirm with Menu1 (Enter/Start)
            if (input.GetButtonDown(InputButton.Menu1))
            {
                StartCountdown();
            }

            // Back to menu with Action2 (F/B)
            if (input.GetButtonDown(InputButton.Action2))
            {
                context.coordinator.TransitionTo(SessionStateType.MainMenu);
            }

            // Alternative back with Menu2 (Tab/Select)
            if (input.GetButtonDown(InputButton.Menu2))
            {
                context.coordinator.TransitionTo(SessionStateType.MainMenu);
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
        private bool simulationActive = false;
        private bool createdBridgeObjects = false;

        public override void OnEnter()
        {
            base.OnEnter();

            // Create simulation bridge components
            CreateSimulationBridge();

            // Start the simulation
            StartSimulation();
        }

        public override void OnExit()
        {
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

        protected override void HandleProcessedInput(PlayerInputFrame input)
        {
            // Press Menu1 (Esc/Start) to end game and go to results
            if (input.GetButtonDown(InputButton.Menu1))
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
                initObject.transform.SetParent(null);
                initObject.transform.position = Vector3.zero;
                simulationInit = initObject.AddComponent<SimulationInitializer>();
                createdBridgeObjects = true;
            }

            if (simulationControl == null)
            {
                Debug.Log("[InGame] Creating SimulationController");
                var controlObject = new GameObject("SimulationController");
                controlObject.transform.SetParent(null);
                controlObject.transform.position = Vector3.zero;
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
        protected override void HandleProcessedInput(PlayerInputFrame input)
        {
            // Press any button to return to menu
            if (input.GetButtonDown(InputButton.Action1) ||
                input.GetButtonDown(InputButton.Action2) ||
                input.GetButtonDown(InputButton.Menu1))
            {
                ReturnToMenu();
            }
        }

        private void ReturnToMenu()
        {
            Debug.Log("[Results] Returning to main menu");
            context.coordinator.TransitionTo(SessionStateType.MainMenu);
        }
    }
}