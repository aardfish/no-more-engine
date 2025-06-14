using Unity.Entities;
using System.Runtime.InteropServices;


namespace NoMoreEngine.Simulation.Components
{
    /// <summary>
    /// Simple linear movement component for testing transform updates
    /// </summary>
    public struct SimpleMovementComponent : IComponentData
    {
        public fix3 velocity;

        [MarshalAs(UnmanagedType.U1)]
        public bool isMoving;

        public SimpleMovementComponent(fix3 velocity, bool isMoving = true)
        {
            this.velocity = velocity;
            this.isMoving = isMoving;
        }

        public static SimpleMovementComponent Stationary => new SimpleMovementComponent(fix3.zero, false);
    }

}