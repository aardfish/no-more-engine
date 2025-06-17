using Unity.Entities;
using System.Runtime.InteropServices;
using NoMoreEngine.Simulation.Snapshot;
using Unity.Mathematics.FixedPoint;


namespace NoMoreEngine.Simulation.Components
{
    /// <summary>
    /// Simple linear movement component for testing transform updates
    /// </summary>

    [Snapshotable(Priority = 1)]
    public struct SimpleMovementComponent : IComponentData, ISnapshotableComponent<SimpleMovementComponent>
    {
        public fp3 velocity;

        [MarshalAs(UnmanagedType.U1)]
        public bool isMoving;

        public SimpleMovementComponent(fp3 velocity, bool isMoving = true)
        {
            this.velocity = velocity;
            this.isMoving = isMoving;
        }

        public static SimpleMovementComponent Stationary => new SimpleMovementComponent(fp3.zero, false);

        public int GetSnapshotSize() => System.Runtime.InteropServices.Marshal.SizeOf<SimpleMovementComponent>();
        public bool ValidateSnapshot() => true;
    }

}