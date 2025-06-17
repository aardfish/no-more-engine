using UnityEngine;
using System;
using System.Collections.Generic;
using NoMoreEngine.Input;

namespace NoMoreEngine.Session
{
    /// <summary>
    /// SessionCoordinator - Manages application flow through different states
    /// Updated to work with the new NoMoreInput system
    /// </summary>
    public class SessionCoordinator : MonoBehaviour
    {
        [Header("Configuration")]
        [SerializeField] private SessionStateType startingState = SessionStateType.MainMenu;
        [SerializeField] private bool debugLogging = true;

        [Header("Current State")]
        [SerializeField] private SessionStateType currentStateType;
        [SerializeField] private float timeInCurrentState;

        // State management
        private ISessionState currentState;
        private Dictionary<SessionStateType, ISessionState> states;

        // Core components
        private GameConfiguration gameConfig;

        // Events
        public event Action<SessionStateType, SessionStateType> OnStateChanged;

        // Singleton for easy access
        private static SessionCoordinator instance;
        public static SessionCoordinator Instance => instance;

        void Awake()
        {
            // Simple singleton
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }
            instance = this;

            // Initialize components
            InitializeComponents();
            InitializeStates();
        }

        void Start()
        {
            // Enter starting state
            TransitionTo(startingState);
        }

        void Update()
        {
            // Update current state
            if (currentState != null)
            {
                currentState.Update(Time.deltaTime);
                timeInCurrentState += Time.deltaTime;
            }
        }

        void OnDestroy()
        {
            // Clean exit from current state
            currentState?.OnExit();

            // Cleanup singleton
            if (instance == this)
            {
                instance = null;
            }
        }

        private void InitializeComponents()
        {
            // Create game configuration
            gameConfig = new GameConfiguration();
        }

        private void InitializeStates()
        {
            states = new Dictionary<SessionStateType, ISessionState>();

            // Create context that states can use
            var context = new SessionContext
            {
                coordinator = this,
                gameConfig = gameConfig
            };

            // Create all session states
            states[SessionStateType.MainMenu] = new MainMenuState();
            states[SessionStateType.MissionLobby] = new MissionLobbyState();
            states[SessionStateType.VersusLobby] = new VersusLobbyState();
            states[SessionStateType.InGame] = new InGameState();
            states[SessionStateType.Pause] = new PauseState();
            states[SessionStateType.Results] = new ResultsState();

            // Initialize all states
            foreach (var state in states.Values)
            {
                state.Initialize(context);
            }
        }

        /// <summary>
        /// Transition to a new state
        /// </summary>
        public void TransitionTo(SessionStateType newStateType)
        {
            if (!states.ContainsKey(newStateType))
            {
                Debug.LogError($"[SessionCoordinator] State {newStateType} not found!");
                return;
            }

            var oldStateType = currentStateType;
            var newState = states[newStateType];

            // Exit current state
            if (currentState != null)
            {
                if (debugLogging)
                    Debug.Log($"[SessionCoordinator] Exiting: {currentStateType}");

                currentState.OnExit();
            }

            // Special handling for pause state
            if (newStateType == SessionStateType.Pause && currentStateType == SessionStateType.InGame)
            {
                var pauseState = newState as PauseState;
                pauseState?.SetPreviousState(currentStateType);
            }

            // Switch state
            currentState = newState;
            currentStateType = newStateType;
            timeInCurrentState = 0f;

            // Enter new state
            if (debugLogging)
                Debug.Log($"[SessionCoordinator] Entering: {newStateType}");

            currentState.OnEnter();

            // Update input context
            UpdateInputContext(newStateType);

            // Notify listeners
            OnStateChanged?.Invoke(oldStateType, newStateType);
        }

        private void UpdateInputContext(SessionStateType stateType)
        {
            var inputSerializer = FindAnyObjectByType<InputSerializer>();
            if (inputSerializer == null) return;

            // Determine context based on state
            var context = stateType switch
            {
                SessionStateType.MainMenu => InputContext.Menu,
                SessionStateType.MissionLobby => InputContext.Menu,
                SessionStateType.VersusLobby => InputContext.Menu,
                SessionStateType.InGame => InputContext.InGame,
                SessionStateType.Pause => InputContext.Menu,
                SessionStateType.Results => InputContext.Menu,
                _ => InputContext.Menu
            };

            inputSerializer.SetInputContext(context);
        }

        /// <summary>
        /// Get the current game configuration
        /// </summary>
        public GameConfiguration GetGameConfig() => gameConfig;

        /// <summary>
        /// Get the current state type
        /// </summary>
        public SessionStateType GetCurrentStateType() => currentStateType;

        /// <summary>
        /// Get the current state object (use carefully - prefer state-specific methods)
        /// </summary>
        public ISessionState GetCurrentState() => currentState;

        /// <summary>
        /// Get a specific state (for UI access to state data)
        /// </summary>
        public T GetState<T>() where T : class, ISessionState
        {
            foreach (var state in states.Values)
            {
                if (state is T typedState)
                    return typedState;
            }
            return null;
        }
    }

    /// <summary>
    /// Types of session states
    /// </summary>
    public enum SessionStateType
    {
        MainMenu,
        MissionLobby,
        VersusLobby,
        InGame,
        Pause,
        Results
    }

    /// <summary>
    /// Shared context passed to all states
    /// </summary>
    public class SessionContext
    {
        public SessionCoordinator coordinator;
        public GameConfiguration gameConfig;
        public InputRecording lastRecording;
    }

    /// <summary>
    /// Interface for session states
    /// </summary>
    public interface ISessionState
    {
        void Initialize(SessionContext context);
        void OnEnter();
        void OnExit();
        void Update(float deltaTime);
    }
}