using NoMoreEngine.Simulation.Snapshot;
using Unity.Entities;
using Unity.Mathematics.FixedPoint;


namespace NoMoreEngine.Simulation.Components
{
    /// <summary>
    /// Core Transform component using fixed-point math for deterministic simulation
    /// </summary>
    [Snapshotable(Priority = 0)] // Default priority, can be overridden by other components
    public struct FixTransformComponent : IComponentData, ISnapshotable<FixTransformComponent>
    {
        public fp3 position;
        public fpquaternion rotation;
        public fp3 scale;

        public FixTransformComponent(fp3 position, fpquaternion rotation, fp3 scale)
        {
            this.position = position;
            this.rotation = rotation;
            this.scale = scale;
        }

        public static FixTransformComponent Identity => new FixTransformComponent(
            fp3.zero,
            fpquaternion.identity,
            new fp3(fp.zero)
            );

        // Isnapshottable implementation
        public int GetSnapshotSize() => System.Runtime.InteropServices.Marshal.SizeOf<FixTransformComponent>();
        public bool ValidateSnapshot() => true; // Transform is always valid
        //TODO:
        //-----
        //* add parent reference for hierarchy later
        // "public Entity parent;"
        //* add dirty flagging for performance optimization
        // "public bool isDirty;"
    }
}