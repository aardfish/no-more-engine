using Unity.Entities;


namespace NoMoreEngine.Simulation.Components
{
    /// <summary>
    /// Categorizes SimEntities for simulation and rendering logic
    /// </summary>
    public struct SimEntityTypeComponent : IComponentData
    {
        public SimEntityType simEntityType;

        public SimEntityTypeComponent(SimEntityType type)
        {
            this.simEntityType = type;
        }
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