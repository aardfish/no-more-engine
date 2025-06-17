using NoMoreEngine.Simulation.Snapshot;
using Unity.Entities;


namespace NoMoreEngine.Simulation.Components
{
    /// <summary>
    /// Categorizes SimEntities for simulation and rendering logic
    /// </summary>

    [Snapshotable(Priority = 5)]
    public struct SimEntityTypeComponent : IComponentData, ISnapshotableComponent<SimEntityTypeComponent>
    {
        public SimEntityType simEntityType;

        public SimEntityTypeComponent(SimEntityType type)
        {
            this.simEntityType = type;
        }

        public int GetSnapshotSize() => System.Runtime.InteropServices.Marshal.SizeOf<SimEntityTypeComponent>();
        public bool ValidateSnapshot() => true;
    }

    /// <summary>
    /// SimEntity type categories
    /// </summary>
    public enum SimEntityType : byte
    {
        Player = 0,
        Enemy = 1,
        Projectile = 2,
        Environment = 3,

        //expand as needed
    }
}