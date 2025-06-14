using Unity.Entities;


namespace NoMoreEngine.Simulation.Components
{
    /// <summary>
    /// Core Transform component using fixed-point math for deterministic simulation
    /// </summary>
    public struct FixTransformComponent : IComponentData
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

        //TODO:
        //-----
        //* add parent reference for hierarchy later
        // "public Entity parent;"
        //* add dirty flagging for performance optimization
        // "public bool isDirty;"
    }
}