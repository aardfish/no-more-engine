using Unity.Entities;
using UnityEngine;
using NoMoreEngine.Simulation.Components;
using NoMoreEngine.Simulation.Systems;


namespace NoMoreEngine.DevTools
{
    /// <summary>
    /// Debug monitor for the Time System
    /// Shows real-time stats about simulation ticks and performance
    /// </summary>
    public class TimeSystemMonitor : MonoBehaviour
    {
        [Header("Display Settings")]
        [SerializeField] private bool showTimeMonitor = true;
        [SerializeField] private bool showDetailedStats = false;
        [SerializeField] private bool showPerformanceWarnings = true;
        
        [Header("Current State (Read Only)")]
        [SerializeField] private uint currentTick;
        [SerializeField] private float elapsedTime;
        [SerializeField] private float actualTickRate;
        [SerializeField] private int stepsLastFrame;
        [SerializeField] private float accumulatorValue;
        
        // References
        private World simulationWorld;
        private SimulationTimeSystem timeSystem;
        private EntityQuery timeQuery;
        private EntityQuery accumulatorQuery;
        
        // Performance tracking
        private uint lastTick = 0;
        private float lastTickCheckTime = 0f;
        private int ticksSinceLastCheck = 0;
        
        void Start()
        {
            // Get simulation world
            simulationWorld = World.DefaultGameObjectInjectionWorld;
            if (simulationWorld == null)
            {
                Debug.LogError("[TimeSystemMonitor] No simulation world found!");
                enabled = false;
                return;
            }
            
            // Get time system
            timeSystem = simulationWorld.GetExistingSystemManaged<SimulationTimeSystem>();
            if (timeSystem == null)
            {
                Debug.LogError("[TimeSystemMonitor] SimulationTimeSystem not found!");
                enabled = false;
                return;
            }
            
            // Create queries
            timeQuery = simulationWorld.EntityManager.CreateEntityQuery(typeof(SimulationTimeComponent));
            accumulatorQuery = simulationWorld.EntityManager.CreateEntityQuery(typeof(TimeAccumulatorComponent));
            
            Debug.Log("[TimeSystemMonitor] Initialized and monitoring time system");
        }
        
        void Update()
        {
            if (!UpdateMonitorData()) return;
            
            // Track tick rate
            UpdateTickRate();
        }
        
        private bool UpdateMonitorData()
        {
            if (simulationWorld == null || !simulationWorld.IsCreated) return false;
            if (timeQuery.IsEmpty || accumulatorQuery.IsEmpty) return false;
            
            // Get time data
            var time = timeQuery.GetSingleton<SimulationTimeComponent>();
            var accumulator = accumulatorQuery.GetSingleton<TimeAccumulatorComponent>();
            
            // Update inspector fields
            currentTick = time.currentTick;
            elapsedTime = time.GetElapsedSeconds();
            stepsLastFrame = accumulator.stepsLastFrame;
            accumulatorValue = accumulator.accumulator;
            
            return true;
        }
        
        private void UpdateTickRate()
        {
            // Calculate actual tick rate
            if (Time.time - lastTickCheckTime >= 1f)
            {
                ticksSinceLastCheck = (int)(currentTick - lastTick);
                actualTickRate = ticksSinceLastCheck;
                
                lastTick = currentTick;
                lastTickCheckTime = Time.time;
                ticksSinceLastCheck = 0;
                
                // Check for performance issues
                if (showPerformanceWarnings && Mathf.Abs(actualTickRate - 60f) > 5f)
                {
                    Debug.LogWarning($"[TimeSystemMonitor] Tick rate deviation! " +
                                   $"Expected: 60Hz, Actual: {actualTickRate}Hz");
                }
            }
        }
        
        void OnGUI()
        {
            if (!showTimeMonitor) return;
            
            var boldStyle = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };
            
            GUILayout.BeginArea(new Rect(10, 10, 300, showDetailedStats ? 300 : 150));
            GUILayout.BeginVertical("box");
            
            GUILayout.Label("=== Time System Monitor ===", boldStyle);
            
            // Basic stats
            GUILayout.Label($"Tick: {currentTick}");
            GUILayout.Label($"Elapsed: {elapsedTime:F2}s");
            GUILayout.Label($"Tick Rate: {actualTickRate:F1}Hz (Target: 60Hz)");
            
            // Performance indicator
            Color originalColor = GUI.color;
            if (Mathf.Abs(actualTickRate - 60f) > 5f)
            {
                GUI.color = Color.red;
                GUILayout.Label("⚠️ PERFORMANCE WARNING");
            }
            else if (stepsLastFrame > 1)
            {
                GUI.color = Color.yellow;
                GUILayout.Label($"Catching up ({stepsLastFrame} steps)");
            }
            else
            {
                GUI.color = Color.green;
                GUILayout.Label("✓ Running smoothly");
            }
            GUI.color = originalColor;
            
            if (showDetailedStats)
            {
                GUILayout.Space(10);
                GUILayout.Label("=== Detailed Stats ===", boldStyle);
                GUILayout.Label($"Steps Last Frame: {stepsLastFrame}");
                GUILayout.Label($"Accumulator: {accumulatorValue:F4}");
                GUILayout.Label($"Fixed DeltaTime: {1f/60f:F4}");
                GUILayout.Label($"Unity FPS: {1f/Time.deltaTime:F1}");
                
                // Add controls
                GUILayout.Space(10);
                if (GUILayout.Button("Reset Time"))
                {
                    timeSystem?.ResetSimulationTime();
                }
                
                if (GUILayout.Button("Toggle Pause"))
                {
                    var fixedStepGroup = simulationWorld.GetExistingSystemManaged<SimulationStepSystemGroup>();
                    if (fixedStepGroup != null)
                    {
                        bool isEnabled = fixedStepGroup.IsEnabled;
                        timeSystem?.SetSimulationPaused(isEnabled);
                    }
                }
            }
            
            GUILayout.EndVertical();
            GUILayout.EndArea();
        }
        
        void OnDestroy()
        {
            // Queries are automatically cleaned up by ECS
        }
        
        // Context menu helpers for testing
        [ContextMenu("Log Time Stats")]
        public void LogTimeStats()
        {
            if (!UpdateMonitorData()) return;
            
            var time = timeQuery.GetSingleton<SimulationTimeComponent>();
            Debug.Log($"[TimeSystemMonitor] Stats:\n" +
                     $"Current Tick: {time.currentTick}\n" +
                     $"Elapsed Time: {time.GetElapsedSeconds():F2}s\n" +
                     $"Tick Rate: {time.tickRate}Hz\n" +
                     $"Delta Time: {(float)time.deltaTime:F6}s\n" +
                     $"Rollback Window: {time.maxRollbackWindow} ticks");
        }
        
        [ContextMenu("Force Time Step")]
        public void ForceTimeStep()
        {
            if (timeSystem != null)
            {
                Debug.Log("[TimeSystemMonitor] Forcing a manual time step...");
                // This would need to be implemented in SimulationTimeSystem as a debug feature
            }
        }
    }
}