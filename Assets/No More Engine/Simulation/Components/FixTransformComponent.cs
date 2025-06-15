using NoMoreEngine.Simulation.Snapshot;
using Unity.Entities;


namespace NoMoreEngine.Simulation.Components
{
    /// <summary>
    /// Core Transform component using fixed-point math for deterministic simulation
    /// </summary>
    [Snapshotable(Priority = 0)] // Default priority, can be overridden by other components
    public struct FixTransformComponent : IComponentData, ISnapshotable<FixTransformComponent>
    {
        public fix3 position;
        public fixQuaternion rotation;
        public fix3 scale;

        public FixTransformComponent(fix3 position, fixQuaternion rotation, fix3 scale)
        {
            this.position = position;
            this.rotation = rotation;
            this.scale = scale;
        }

        public static FixTransformComponent Identity => new FixTransformComponent(
            fix3.zero,
            fixQuaternion.Identity,
            fix3.one
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