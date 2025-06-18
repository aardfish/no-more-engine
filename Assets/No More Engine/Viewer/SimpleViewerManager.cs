using Unity.Entities;
using UnityEngine;
using NoMoreEngine.Viewer.Game;
using NoMoreEngine.Viewer.Debug;
using NoMoreEngine.Simulation.Components;


namespace NoMoreEngine.Viewer
{
    /// <summary>
    /// Simplified ViewerManager for the proof-of-concept
    /// Only handles visualization, no UI system dependencies
    /// </summary>
    public class SimpleViewerManager : MonoBehaviour
    {
        [Header("Viewer Settings")]
        [SerializeField] private bool enableVisualization = true;
        [SerializeField] private bool showDebugInfo = false;

        [Header("Entity Visualization")]
        [SerializeField] private bool showSimEntities = true;
        [SerializeField] private bool showCollisionBounds = false;
        [SerializeField] private bool showPhysicsDebug = false;

        [Header("Performance")]
        [SerializeField] private int maxEntitiesPerFrame = 1000;

        // Viewer subsystems
        private SimEntityViewer simEntityViewer;
        private CollisionViewer collisionViewer;
        private PhysicsViewer physicsViewer;

        // ECS references
        private EntityManager entityManager;
        private World simulationWorld;

        void Start()
        {
            // Get reference to simulation world
            simulationWorld = World.DefaultGameObjectInjectionWorld;
            if (simulationWorld != null)
            {
                entityManager = simulationWorld.EntityManager;
            }

            // Initialize only visualization viewers
            InitializeViewers();
        }

        void Update()
        {
            // Update visualization subsystems only if enabled
            if (enableVisualization)
            {
                UpdateVisualizationViewers();
            }
        }

        private void InitializeViewers()
        {
            // Initialize visualization viewers
            if (simulationWorld != null && simulationWorld.IsCreated)
            {
                simEntityViewer = new SimEntityViewer();
                collisionViewer = new CollisionViewer();
                physicsViewer = new PhysicsViewer();

                simEntityViewer.Initialize(entityManager);
                collisionViewer.Initialize(entityManager);
                physicsViewer.Initialize(entityManager);

                UnityEngine.Debug.Log("[SimpleViewerManager] Initialized visualization systems");
            }
        }

        private void UpdateVisualizationViewers()
        {
            if (simulationWorld == null || !simulationWorld.IsCreated) return;

            // Update simulation entity visualization
            if (showSimEntities)
            {
                simEntityViewer?.Update(maxEntitiesPerFrame);
            }

            // Update collision visualization
            if (showCollisionBounds)
            {
                collisionViewer?.Update();
            }

            // Update physics visualization
            if (showPhysicsDebug)
            {
                physicsViewer?.Update();
            }
        }

        void OnGUI()
        {
            // Simple debug controls
            if (showDebugInfo)
            {
                DrawDebugControls();
            }
        }

        private void DrawDebugControls()
        {
            GUILayout.BeginArea(new Rect(Screen.width - 250, 10, 240, 200));
            GUILayout.BeginVertical("box");

            GUILayout.Label("=== Viewer Debug ===");

            enableVisualization = GUILayout.Toggle(enableVisualization, "Enable Visualization");
            showSimEntities = GUILayout.Toggle(showSimEntities, "Show SimEntities");
            showCollisionBounds = GUILayout.Toggle(showCollisionBounds, "Show Collision Bounds");
            showPhysicsDebug = GUILayout.Toggle(showPhysicsDebug, "Show Physics Debug");

            GUILayout.Space(10);

            // Entity count
            if (simulationWorld != null && simulationWorld.IsCreated)
            {
                var query = entityManager.CreateEntityQuery(typeof(SimEntityTypeComponent));
                GUILayout.Label($"Entities: {query.CalculateEntityCount()}");
                query.Dispose();
            }

            GUILayout.EndVertical();
            GUILayout.EndArea();
        }

        /// <summary>
        /// Public API for external systems to control viewer settings
        /// </summary>
        public void SetVisualizationEnabled(bool enabled) => enableVisualization = enabled;
        public void SetSimEntitiesVisible(bool visible) => showSimEntities = visible;
        public void SetCollisionBoundsVisible(bool visible) => showCollisionBounds = visible;
        public void SetPhysicsDebugVisible(bool visible) => showPhysicsDebug = visible;
    }
}