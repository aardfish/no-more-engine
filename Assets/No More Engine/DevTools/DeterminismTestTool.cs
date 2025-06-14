using Unity.Entities;
using Unity.Collections;
using UnityEngine;
using System.Collections.Generic;
using System.Text;
using System.IO;
using UnityEditor;
using NoMoreEngine.Simulation.Components;


namespace NoMoreEngine.DevTools
{
    /// <summary>
    /// Tool for testing deterministic behavior across multiple simulation runs
    /// Records entity states and compares them between runs
    /// Persists data across play mode sessions using JSON files
    /// </summary>
    public class DeterminismTestTool : MonoBehaviour
    {
        [Header("Test Settings")]
        [SerializeField] private bool enableTesting = true;
        [SerializeField] private int testDurationFrames = 300; // 5 seconds at 60fps
        [SerializeField] private int recordInterval = 10; // Record every 10 frames
        [SerializeField] private bool autoRunTests = false;
        [SerializeField] private string testDataFileName = "determinism_test_data.json";

        [Header("Debug Display")]
        [SerializeField] private bool showTestResults = true;
        [SerializeField] private bool showDetailedOutput = false;

        private EntityManager entityManager;
        private EntityQuery testEntityQuery;

        // Test data storage
        private List<SimulationSnapshot> currentRunData;
        private TestRunCollection savedTestData;
        private int currentFrame;
        private bool isRecording;
        private bool testCompleted;
        private bool determinismPassed;

        // Results
        private string lastTestResult = "No test run yet";
        private int totalComparisons;
        private int failedComparisons;
        private string dataFilePath;

        void Start()
        {
            entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
            testEntityQuery = entityManager.CreateEntityQuery(
                typeof(FixTransformComponent),
                typeof(SimEntityTypeComponent)
            );

            currentRunData = new List<SimulationSnapshot>();
            dataFilePath = Path.Combine(Application.persistentDataPath, testDataFileName);

            LoadSavedTestData();

            if (autoRunTests)
            {
                StartTest();
            }
        }

        void Update()
        {
            if (!enableTesting || !isRecording) return;

            currentFrame++;

            // Record snapshot at intervals
            if (currentFrame % recordInterval == 0)
            {
                RecordSnapshot();
            }

            // End test after duration
            if (currentFrame >= testDurationFrames)
            {
                EndTest();
            }
        }

        void OnGUI()
        {
            if (!showTestResults) return;

            GUILayout.BeginArea(new Rect(420, 10, 400, 400));
            GUILayout.BeginVertical("box");

            GUILayout.Label("=== Determinism Test Tool ===", EditorStyles.boldLabel);

            // Test controls
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Start Test"))
            {
                StartTest();
            }
            if (GUILayout.Button("Compare Last 2 Runs") && savedTestData.testRuns.Count >= 2)
            {
                CompareLastTwoRuns();
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Clear All Data"))
            {
                ClearAllTestData();
            }
            if (GUILayout.Button("Save Data"))
            {
                SaveTestData();
            }
            GUILayout.EndHorizontal();

            // Test status
            GUILayout.Space(10);
            GUILayout.Label("=== Status ===", EditorStyles.boldLabel);
            GUILayout.Label($"Recording: {isRecording}");
            GUILayout.Label($"Frame: {currentFrame}/{testDurationFrames}");
            GUILayout.Label($"Snapshots: {currentRunData.Count}");
            GUILayout.Label($"Saved Runs: {savedTestData.testRuns.Count}");

            // Test results
            GUILayout.Space(10);
            GUILayout.Label("=== Results ===", EditorStyles.boldLabel);

            if (testCompleted && savedTestData.testRuns.Count >= 2)
            {
                Color originalColor = GUI.color;
                GUI.color = determinismPassed ? Color.green : Color.red;
                GUILayout.Label($"Determinism: {(determinismPassed ? "PASSED" : "FAILED")}");
                GUI.color = originalColor;

                GUILayout.Label($"Comparisons: {totalComparisons}");
                GUILayout.Label($"Failed: {failedComparisons}");
            }

            GUILayout.Label($"Last Result: {lastTestResult}");

            if (showDetailedOutput && !string.IsNullOrEmpty(lastTestResult))
            {
                GUILayout.Space(10);
                GUILayout.Label("=== Details ===", EditorStyles.boldLabel);
                GUILayout.TextArea(lastTestResult, GUILayout.Height(80));
            }

            // Controls
            GUILayout.Space(10);
            enableTesting = GUILayout.Toggle(enableTesting, "Enable Testing");
            showDetailedOutput = GUILayout.Toggle(showDetailedOutput, "Show Details");

            GUILayout.EndVertical();
            GUILayout.EndArea();
        }

        public void StartTest()
        {
            Debug.Log("[DeterminismTest] Starting new test run...");

            // Reset for new test
            currentRunData.Clear();
            currentFrame = 0;
            isRecording = true;
            testCompleted = false;
            lastTestResult = "Test in progress...";
        }

        void RecordSnapshot()
        {
            if (!testEntityQuery.IsEmpty)
            {
                var snapshot = CaptureSimulationState();
                currentRunData.Add(snapshot);

                if (showDetailedOutput)
                {
                    Debug.Log($"[DeterminismTest] Recorded snapshot at frame {currentFrame} with {snapshot.entityStates.Count} entities");
                }
            }
        }

        void EndTest()
        {
            isRecording = false;
            testCompleted = true;

            // Save this run to our collection
            var testRun = new TestRun
            {
                runNumber = savedTestData.testRuns.Count + 1,
                timestamp = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                snapshots = new List<SimulationSnapshot>(currentRunData)
            };

            savedTestData.testRuns.Add(testRun);
            SaveTestData();

            lastTestResult = $"Test completed. Run #{testRun.runNumber} recorded {currentRunData.Count} snapshots over {currentFrame} frames.";

            Debug.Log($"[DeterminismTest] {lastTestResult}");

            // Auto-compare if we have multiple runs
            if (savedTestData.testRuns.Count >= 2)
            {
                CompareLastTwoRuns();
            }
        }

        void CompareLastTwoRuns()
        {
            if (savedTestData.testRuns.Count < 2)
            {
                lastTestResult = "Cannot compare: Need at least two test runs";
                return;
            }

            var run1 = savedTestData.testRuns[savedTestData.testRuns.Count - 2];
            var run2 = savedTestData.testRuns[savedTestData.testRuns.Count - 1];

            Debug.Log($"[DeterminismTest] Comparing run #{run1.runNumber} vs run #{run2.runNumber}...");

            totalComparisons = 0;
            failedComparisons = 0;
            StringBuilder resultBuilder = new StringBuilder();

            int minSnapshots = Mathf.Min(run1.snapshots.Count, run2.snapshots.Count);

            for (int i = 0; i < minSnapshots; i++)
            {
                var snapshot1 = run1.snapshots[i];
                var snapshot2 = run2.snapshots[i];

                if (!CompareSnapshots(snapshot1, snapshot2, out string differences))
                {
                    failedComparisons++;
                    if (showDetailedOutput)
                    {
                        resultBuilder.AppendLine($"Frame {snapshot1.frameNumber}: {differences}");
                    }
                }
                totalComparisons++;
            }

            determinismPassed = failedComparisons == 0;

            if (determinismPassed)
            {
                lastTestResult = $"DETERMINISM PASSED! Runs #{run1.runNumber} vs #{run2.runNumber}: All {totalComparisons} snapshots matched perfectly.";
            }
            else
            {
                lastTestResult = $"DETERMINISM FAILED! Runs #{run1.runNumber} vs #{run2.runNumber}: {failedComparisons}/{totalComparisons} snapshots differed.";
                if (showDetailedOutput)
                {
                    lastTestResult += "\n" + resultBuilder.ToString();
                }
            }

            Debug.Log($"[DeterminismTest] {lastTestResult}");
        }

        void LoadSavedTestData()
        {
            if (File.Exists(dataFilePath))
            {
                try
                {
                    string json = File.ReadAllText(dataFilePath);
                    savedTestData = JsonUtility.FromJson<TestRunCollection>(json);
                    Debug.Log($"[DeterminismTest] Loaded {savedTestData.testRuns.Count} previous test runs");
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"[DeterminismTest] Failed to load test data: {e.Message}");
                    savedTestData = new TestRunCollection();
                }
            }
            else
            {
                savedTestData = new TestRunCollection();
            }
        }

        void SaveTestData()
        {
            try
            {
                string json = JsonUtility.ToJson(savedTestData, true);
                File.WriteAllText(dataFilePath, json);
                Debug.Log($"[DeterminismTest] Saved test data to {dataFilePath}");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[DeterminismTest] Failed to save test data: {e.Message}");
            }
        }

        void ClearAllTestData()
        {
            savedTestData = new TestRunCollection();
            if (File.Exists(dataFilePath))
            {
                File.Delete(dataFilePath);
            }
            lastTestResult = "All test data cleared";
            Debug.Log("[DeterminismTest] All test data cleared");
        }

        SimulationSnapshot CaptureSimulationState()
        {
            var snapshot = new SimulationSnapshot
            {
                frameNumber = currentFrame,
                entityStates = new List<EntityState>()
            };

            var entities = testEntityQuery.ToEntityArray(Allocator.Temp);
            var transforms = testEntityQuery.ToComponentDataArray<FixTransformComponent>(Allocator.Temp);
            var types = testEntityQuery.ToComponentDataArray<SimEntityTypeComponent>(Allocator.Temp);

            for (int i = 0; i < entities.Length; i++)
            {
                var entityState = new EntityState
                {
                    entityId = entities[i].Index, // Simple ID for comparison
                    position = transforms[i].position,
                    rotation = transforms[i].rotation,
                    scale = transforms[i].scale,
                    entityType = types[i].simEntityType
                };

                snapshot.entityStates.Add(entityState);
            }

            entities.Dispose();
            transforms.Dispose();
            types.Dispose();

            // Sort by entity ID for consistent comparison
            snapshot.entityStates.Sort((a, b) => a.entityId.CompareTo(b.entityId));

            return snapshot;
        }

        bool CompareSnapshots(SimulationSnapshot a, SimulationSnapshot b, out string differences)
        {
            differences = "";

            if (a.entityStates.Count != b.entityStates.Count)
            {
                differences = $"Entity count mismatch: {a.entityStates.Count} vs {b.entityStates.Count}";
                return false;
            }

            for (int i = 0; i < a.entityStates.Count; i++)
            {
                var entityA = a.entityStates[i];
                var entityB = b.entityStates[i];

                if (!CompareEntityStates(entityA, entityB, out string entityDiff))
                {
                    differences = $"Entity {entityA.entityId}: {entityDiff}";
                    return false;
                }
            }

            return true;
        }

        bool CompareEntityStates(EntityState a, EntityState b, out string differences)
        {
            differences = "";

            // Compare positions (fixed-point should be exactly equal)
            if (!a.position.Equals(b.position))
            {
                differences += $"Position: {a.position} vs {b.position}. ";
            }

            // Compare rotations
            if (!a.rotation.Equals(b.rotation))
            {
                differences += $"Rotation: {a.rotation} vs {b.rotation}. ";
            }

            // Compare scales
            if (!a.scale.Equals(b.scale))
            {
                differences += $"Scale: {a.scale} vs {b.scale}. ";
            }

            // Compare types
            if (a.entityType != b.entityType)
            {
                differences += $"Type: {a.entityType} vs {b.entityType}. ";
            }

            return string.IsNullOrEmpty(differences);
        }
    }

    [System.Serializable]
    public class TestRunCollection
    {
        public List<TestRun> testRuns = new List<TestRun>();
    }

    [System.Serializable]
    public class TestRun
    {
        public int runNumber;
        public string timestamp;
        public List<SimulationSnapshot> snapshots = new List<SimulationSnapshot>();
    }

    [System.Serializable]
    public struct SimulationSnapshot
    {
        public int frameNumber;
        public List<EntityState> entityStates;
    }

    [System.Serializable]
    public struct EntityState
    {
        public int entityId;
        public fix3 position;
        public fixQuaternion rotation;
        public fix3 scale;
        public SimEntityType entityType;
    }
}