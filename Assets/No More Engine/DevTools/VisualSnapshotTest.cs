using UnityEngine;
using Unity.Entities;
using Unity.Collections;
using NoMoreEngine.Simulation.Snapshot;
using NoMoreEngine.Simulation.Systems;
using NoMoreEngine.Simulation.Components;
using NoMoreEngine.Input;
using System.Collections.Generic;
using Unity.Mathematics.FixedPoint;

namespace NoMoreEngine.DevTools
{
    /// <summary>
    /// Visual test that shows entity positions before/after snapshot restore
    /// You'll see entities "jump back" to their saved positions
    /// FIXED: Input handling now on simulation ticks only
    /// </summary>
    public class VisualSnapshotTest : MonoBehaviour
    {
        [Header("Automated Test")]
        [SerializeField] private bool runAutomatedTest = true;
        [SerializeField] private float captureAfterSeconds = 2f;
        [SerializeField] private float restoreAfterSeconds = 5f;
        
        [Header("Manual Controls")]
        [SerializeField] private InputButton manualCaptureButton = InputButton.Action5; // LB/Shift
        [SerializeField] private InputButton manualRestoreButton = InputButton.Action6; // RB/C
        
        [Header("Visual Feedback")]
        [SerializeField] private bool showEntityMarkers = true;
        [SerializeField] private Color snapshotPositionColor = Color.green;
        [SerializeField] private Color currentPositionColor = Color.red;
        [SerializeField] private float markerDuration = 3f;
        
        private SnapshotSystem snapshotSystem;
        private SimulationTimeSystem timeSystem;
        private EntityManager entityManager;
        
        // Test state
        private bool testStarted = false;
        private float testTimer = 0f;
        private bool snapshotCaptured = false;
        private bool snapshotRestored = false;
        
        // Visual markers
        private List<Vector3> snapshotPositions = new List<Vector3>();
        private float markerTimer = 0f;
        
        void Start()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world != null)
            {
                snapshotSystem = world.GetExistingSystemManaged<SnapshotSystem>();
                timeSystem = world.GetExistingSystemManaged<SimulationTimeSystem>();
                entityManager = world.EntityManager;
                
                // Subscribe to tick events for input handling
                if (timeSystem != null)
                {
                    timeSystem.OnSimulationTick += OnSimulationTick;
                }
                
                if (runAutomatedTest)
                {
                    StartAutomatedTest();
                }
            }
        }
        
        void OnDestroy()
        {
            // Unsubscribe from tick events
            if (timeSystem != null)
            {
                timeSystem.OnSimulationTick -= OnSimulationTick;
            }
        }
        
        /// <summary>
        /// Handle input on simulation ticks only
        /// </summary>
        private void OnSimulationTick(uint tick)
        {
            if (snapshotSystem == null || runAutomatedTest) return;
            
            // Manual capture
            if (NoMoreInput.Player1.GetButtonDown(manualCaptureButton))
            {
                CaptureSnapshotWithPositions();
                snapshotCaptured = true;
            }
            
            // Manual restore
            if (NoMoreInput.Player1.GetButtonDown(manualRestoreButton) && snapshotCaptured)
            {
                RestoreAndCompare();
            }
        }
        
        void Update()
        {
            // Handle automated test timing (no input here)
            if (runAutomatedTest && testStarted && snapshotSystem != null)
            {
                testTimer += Time.deltaTime;

                // Capture snapshot after delay
                if (!snapshotCaptured && testTimer >= captureAfterSeconds)
                {
                    CaptureSnapshotWithPositions();
                    snapshotCaptured = true;
                }

                // Restore snapshot after delay
                if (!snapshotRestored && testTimer >= restoreAfterSeconds)
                {
                    RestoreAndCompare();
                    snapshotRestored = true;
                }
            }
            
            // Update marker timer
            if (markerTimer > 0)
            {
                markerTimer -= Time.deltaTime;
                
                // Draw debug visuals
                if (showEntityMarkers)
                {
                    DrawDebugMarkers();
                }
            }
        }
        
        void DrawDebugMarkers()
        {
            // Draw snapshot positions (green crosses)
            foreach (var pos in snapshotPositions)
            {
                // Draw cross at snapshot position
                float size = 0.5f;
                Debug.DrawLine(pos + Vector3.left * size, pos + Vector3.right * size, snapshotPositionColor);
                Debug.DrawLine(pos + Vector3.up * size, pos + Vector3.down * size, snapshotPositionColor);
                Debug.DrawLine(pos + Vector3.forward * size, pos + Vector3.back * size, snapshotPositionColor);
                
                // Draw sphere
                Debug.DrawSphere(pos, Quaternion.identity, 0.3f, snapshotPositionColor);
            }
            
            // Draw current entity positions (red cubes)
            if (entityManager != null)
            {
                var query = entityManager.CreateEntityQuery(
                    typeof(FixTransformComponent),
                    typeof(SimEntityTypeComponent)
                );
                
                if (!query.IsEmpty)
                {
                    var transforms = query.ToComponentDataArray<FixTransformComponent>(Allocator.Temp);
                    
                    foreach (var transform in transforms)
                    {
                        var pos = transform.position.ToVector3();
                        Debug.DrawCube(pos, Quaternion.identity, 0.4f, currentPositionColor);
                    }
                    
                    transforms.Dispose();
                }
                
                query.Dispose();
            }
            
            // Draw connection lines between snapshot and current positions
            if (snapshotPositions.Count > 0 && entityManager != null)
            {
                var query = entityManager.CreateEntityQuery(
                    typeof(FixTransformComponent),
                    typeof(SimEntityTypeComponent)
                );
                
                if (!query.IsEmpty)
                {
                    var transforms = query.ToComponentDataArray<FixTransformComponent>(Allocator.Temp);
                    
                    for (int i = 0; i < Mathf.Min(snapshotPositions.Count, transforms.Length); i++)
                    {
                        var snapPos = snapshotPositions[i];
                        var currPos = transforms[i].position.ToVector3();
                        
                        if (Vector3.Distance(snapPos, currPos) > 0.1f)
                        {
                            Debug.DrawLine(snapPos, currPos, Color.yellow);
                        }
                    }
                    
                    transforms.Dispose();
                }
                
                query.Dispose();
            }
        }
        
        void StartAutomatedTest()
        {
            testStarted = true;
            testTimer = 0f;
            Debug.Log("[VisualSnapshotTest] Starting automated test...");
            Debug.Log($"- Will capture snapshot at {captureAfterSeconds}s");
            Debug.Log($"- Will restore snapshot at {restoreAfterSeconds}s");
        }
        
        void CaptureSnapshotWithPositions()
        {
            // Debug check for PlayerControlledTag
            var playerQuery = entityManager.CreateEntityQuery(typeof(PlayerControlledTag));
            var playerEntities = playerQuery.ToEntityArray(Allocator.Temp);
            Debug.Log($"[VisualSnapshotTest] Found {playerEntities.Length} entities with PlayerControlledTag");
            
            foreach (var entity in playerEntities)
            {
                // This should work for tag components
                bool hasTag = entityManager.HasComponent<PlayerControlledTag>(entity);
                Debug.Log($"[VisualSnapshotTest] Entity {entity.Index} has PlayerControlledTag: {hasTag}");
                
                // This would throw for tag components
                // DON'T UNCOMMENT: var tag = entityManager.GetComponentData<PlayerControlledTag>(entity);
            }
            
            playerEntities.Dispose();
            playerQuery.Dispose();
            
            // Capture entity positions before snapshot
            snapshotPositions.Clear();
            var query = entityManager.CreateEntityQuery(
                typeof(FixTransformComponent),
                typeof(SimEntityTypeComponent)
            );
            
            var transforms = query.ToComponentDataArray<FixTransformComponent>(Allocator.Temp);
            
            foreach (var transform in transforms)
            {
                snapshotPositions.Add(transform.position.ToVector3());
            }
            
            transforms.Dispose();
            query.Dispose();
            
            // Capture snapshot
            snapshotSystem.CaptureSnapshot();
            var tick = timeSystem.GetCurrentTick();
            
            Debug.Log($"[VisualSnapshotTest] Captured snapshot at tick {tick} with {snapshotPositions.Count} entity positions");
            
            // Start showing markers
            if (showEntityMarkers)
            {
                markerTimer = markerDuration;
            }
        }
        
        void RestoreAndCompare()
        {
            // Get current positions before restore
            var query = entityManager.CreateEntityQuery(
                typeof(FixTransformComponent),
                typeof(SimEntityTypeComponent)
            );
            
            var beforeTransforms = query.ToComponentDataArray<FixTransformComponent>(Allocator.Temp);
            var currentPositions = new List<Vector3>();
            
            foreach (var transform in beforeTransforms)
            {
                currentPositions.Add(transform.position.ToVector3());
            }
            
            beforeTransforms.Dispose();
            
            // Restore snapshot
            var snapshots = snapshotSystem.GetAvailableSnapshots();
            if (snapshots.Length > 0)
            {
                uint snapshotTick = snapshots[snapshots.Length - 1];
                snapshotSystem.RestoreSnapshot(snapshotTick);
                
                // Get positions after restore
                var afterTransforms = query.ToComponentDataArray<FixTransformComponent>(Allocator.Temp);
                
                // Calculate how far entities moved
                float totalDistance = 0f;
                int movedCount = 0;
                
                for (int i = 0; i < Mathf.Min(currentPositions.Count, afterTransforms.Length); i++)
                {
                    var before = currentPositions[i];
                    var after = afterTransforms[i].position.ToVector3();
                    float distance = Vector3.Distance(before, after);
                    
                    if (distance > 0.01f)
                    {
                        totalDistance += distance;
                        movedCount++;
                    }
                }
                
                afterTransforms.Dispose();
                
                Debug.Log($"[VisualSnapshotTest] RESTORE COMPLETE!");
                Debug.Log($"- {movedCount}/{currentPositions.Count} entities jumped back");
                Debug.Log($"- Average jump distance: {(movedCount > 0 ? totalDistance/movedCount : 0):F2} units");
                
                // Refresh markers
                if (showEntityMarkers)
                {
                    markerTimer = markerDuration;
                }
            }
            
            query.Dispose();
        }
        
        void OnGUI()
        {
            GUILayout.BeginArea(new Rect(10, 150, 400, 200));
            GUILayout.BeginVertical("box");
            
            GUILayout.Label("=== Visual Snapshot Test ===", GUI.skin.box);
            
            if (runAutomatedTest && testStarted)
            {
                var progress = testTimer / restoreAfterSeconds;
                GUILayout.Label($"Test Progress: {progress:P0}");
                
                // Status indicators
                GUI.color = snapshotCaptured ? Color.green : Color.gray;
                GUILayout.Label($"[{(snapshotCaptured ? "✓" : " ")}] Snapshot captured at {captureAfterSeconds}s");
                
                GUI.color = snapshotRestored ? Color.green : Color.gray;
                GUILayout.Label($"[{(snapshotRestored ? "✓" : " ")}] Snapshot restored at {restoreAfterSeconds}s");
            }
            else
            {
                GUILayout.Label("Manual Mode - Use buttons:");
                GUILayout.Label($"[{manualCaptureButton.GetDisplayName()}] Capture Snapshot");
                GUILayout.Label($"[{manualRestoreButton.GetDisplayName()}] Restore Snapshot");
            }
            
            GUI.color = Color.white;
            
            if (showEntityMarkers && markerTimer > 0)
            {
                GUILayout.Space(10);
                GUI.color = snapshotPositionColor;
                GUILayout.Label("● Green = Snapshot positions");
                GUI.color = currentPositionColor;
                GUILayout.Label("● Red = Current positions");
                GUI.color = Color.yellow;
                GUILayout.Label("─ Yellow = Movement lines");
                GUI.color = Color.white;
            }
            
            GUILayout.EndVertical();
            GUILayout.EndArea();
        }
        
        [ContextMenu("Run Test Now")]
        public void RunTestNow()
        {
            StartAutomatedTest();
        }
    }
}