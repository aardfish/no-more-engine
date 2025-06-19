using UnityEngine;
using Unity.Entities;
using NoMoreEngine.Simulation.Bridge;
using NoMoreEngine.Simulation.Systems;
using Unity.Mathematics.FixedPoint;

namespace NoMoreEngine.Simulation.Snapshot
{
    /// <summary>
    /// Simple pre-warming utility for the Snapshot System.
    /// Eliminates first-time performance hitches by warming up memory allocations and code paths.
    /// </summary>
    public static class SnapshotSystemPrewarm
    {
        /// <summary>
        /// Pre-warms the snapshot system to eliminate first-use performance hitches.
        /// Call this during application initialization or in a preload state.
        /// </summary>
        public static void Prewarm()
        {
            Debug.Log("[SnapshotSystemPrewarm] Starting pre-warm...");
            
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
            {
                Debug.LogError("[SnapshotSystemPrewarm] No simulation world available");
                return;
            }
            
            // Get required systems
            var snapshotSystem = world.GetExistingSystemManaged<SnapshotSystem>();
            var timeSystem = world.GetExistingSystemManaged<SimulationTimeSystem>();
            var simEntityManager = SimulationEntityManager.Instance;
            
            if (snapshotSystem == null || timeSystem == null || simEntityManager == null)
            {
                Debug.LogError("[SnapshotSystemPrewarm] Required systems not available");
                return;
            }
            
            // Create test entities using SimulationEntityManager's built-in methods
            const int testEntityCount = 10;
            var testEntities = new Entity[testEntityCount];
            
            // Create a mix of entity types to ensure all code paths are warmed
            testEntities[0] = simEntityManager.CreatePlayerEntity(new fp3(0, 0, 0), 0, true);
            testEntities[1] = simEntityManager.CreatePlayerEntity(new fp3(1, 0, 0), 1, false);
            testEntities[2] = simEntityManager.CreateEnvironmentEntity(
                new fp3(2, 0, 0), 
                new fp3(2, 1, 2), 
                true
            );
            testEntities[3] = simEntityManager.CreateEnvironmentEntity(
                new fp3(3, 0, 0), 
                new fp3(1, 3, 1), 
                false
            );
            
            // Create projectiles (using Entity.Null as owner for prewarm)
            testEntities[4] = simEntityManager.CreateProjectileEntity(
                new fp3(4, 0, 0),
                new fp3(0, 0, 5),
                Entity.Null
            );
            testEntities[5] = simEntityManager.CreateProjectileEntity(
                new fp3(5, 0, 0),
                new fp3(5, 0, 0),
                Entity.Null
            );
            
            // Fill rest with more environment entities
            for (int i = 6; i < testEntityCount; i++)
            {
                testEntities[i] = simEntityManager.CreateEnvironmentEntity(
                    new fp3(i, 0, 0),
                    new fp3(fp.one),
                    true
                );
            }
            
            // Capture snapshot
            var prewarmTick = timeSystem.GetCurrentTick();
            snapshotSystem.CaptureSnapshot();
            
            // Force update to process the capture
            snapshotSystem.Update();
            
            // Restore snapshot
            snapshotSystem.RestoreSnapshot(prewarmTick);
            
            // Clean up test entities
            foreach (var entity in testEntities)
            {
                simEntityManager.DestroyEntity(entity);
            }
            
            Debug.Log("[SnapshotSystemPrewarm] Pre-warm completed successfully!");
        }
    }
}