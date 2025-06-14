using UnityEngine;
using Unity.Entities;


namespace NoMoreEngine.Simulation.Bridge
{
    /// <summary>
    /// SimulationController - Controls simulation state (start/stop/pause)
    /// Bridge between session layer and simulation systems
    /// </summary>
    public class SimulationController : MonoBehaviour
    {
        private World simulationWorld;
        private SimulationWorldManager worldManager;

        // Simulation state
        private bool isRunning = false;
        private bool isPaused = false;

        // Events
        public event System.Action OnSimulationStarted;
        public event System.Action OnSimulationStopped;
        public event System.Action OnSimulationPaused;
        public event System.Action OnSimulationResumed;

        void Awake()
        {
            // Find world manager but don't get world yet - it might not exist
            worldManager = FindAnyObjectByType<SimulationWorldManager>();

            // Ensure world manager exists
            if (worldManager == null)
            {
                Debug.LogWarning("[SimController] SimulationWorldManager not found, creating one");
                var worldManagerObject = new GameObject("SimulationWorldManager");
                worldManagerObject.transform.SetParent(null); // Not in subscene!
                worldManager = worldManagerObject.AddComponent<SimulationWorldManager>();
            }
        }

        /// <summary>
        /// Start the simulation
        /// </summary>
        public void StartSimulation()
        {
            if (isRunning)
            {
                Debug.LogWarning("[SimController] Simulation already running");
                return;
            }

            // Get world when we need it
            simulationWorld = World.DefaultGameObjectInjectionWorld;
            if (simulationWorld == null || !simulationWorld.IsCreated)
            {
                Debug.LogError("[SimController] No simulation world available!");
                return;
            }

            Debug.Log("[SimController] Starting simulation");

            // Ensure world manager is initialized
            if (!worldManager.IsInitialized)
            {
                worldManager.Initialize();
            }

            isRunning = true;
            isPaused = false;

            // Enable simulation systems
            SetSimulationSystemsEnabled(true);

            OnSimulationStarted?.Invoke();
        }

        /// <summary>
        /// Stop the simulation
        /// </summary>
        public void StopSimulation()
        {
            if (!isRunning)
            {
                // Not a problem, just not running
                return;
            }

            Debug.Log("[SimController] Stopping simulation");

            isRunning = false;
            isPaused = false;

            // Disable simulation systems
            SetSimulationSystemsEnabled(false);

            OnSimulationStopped?.Invoke();
        }

        /// <summary>
        /// Pause the simulation
        /// </summary>
        public void PauseSimulation()
        {
            if (!isRunning || isPaused)
            {
                return;
            }

            Debug.Log("[SimController] Pausing simulation");

            isPaused = true;

            // Disable update systems but keep data intact
            SetSimulationSystemsEnabled(false);

            OnSimulationPaused?.Invoke();
        }

        /// <summary>
        /// Resume the simulation
        /// </summary>
        public void ResumeSimulation()
        {
            if (!isRunning || !isPaused)
            {
                return;
            }

            Debug.Log("[SimController] Resuming simulation");

            isPaused = false;

            // Re-enable systems
            SetSimulationSystemsEnabled(true);

            OnSimulationResumed?.Invoke();
        }

        /// <summary>
        /// Check if simulation is running
        /// </summary>
        public bool IsRunning => isRunning && !isPaused;

        /// <summary>
        /// Check if simulation is paused
        /// </summary>
        public bool IsPaused => isPaused;

        private void SetSimulationSystemsEnabled(bool enabled)
        {
            // Make sure we have the world reference
            if (simulationWorld == null)
            {
                simulationWorld = World.DefaultGameObjectInjectionWorld;
            }

            if (simulationWorld == null || !simulationWorld.IsCreated)
            {
                Debug.LogError("[SimController] No simulation world available");
                return;
            }

            // Get all simulation systems
            var simulationGroup = simulationWorld.GetExistingSystemManaged<SimulationSystemGroup>();
            if (simulationGroup != null)
            {
                simulationGroup.Enabled = enabled;
                Debug.Log($"[SimController] Simulation systems {(enabled ? "enabled" : "disabled")}");
            }

            // Optionally control specific system groups
            // For now, the entire simulation group is enough
        }

        /// <summary>
        /// Set time scale for the simulation
        /// </summary>
        public void SetTimeScale(float scale)
        {
            // TODO: Implement time scaling when we have proper fixed timestep
            Debug.Log($"[SimController] Time scale set to {scale} (not yet implemented)");
        }

        /// <summary>
        /// Get current match time
        /// </summary>
        public float GetMatchTime()
        {
            if (simulationWorld == null || !simulationWorld.IsCreated)
                return 0f;

            var entityManager = simulationWorld.EntityManager;
            var rulesQuery = entityManager.CreateEntityQuery(typeof(MatchRulesComponent));

            if (!rulesQuery.IsEmpty)
            {
                var rules = rulesQuery.GetSingleton<MatchRulesComponent>();
                rulesQuery.Dispose();
                return rules.currentTime;
            }

            rulesQuery.Dispose();
            return 0f;
        }
    }
}