using Unity.Entities;
using Unity.Burst;


namespace NoMoreEngine.Simulation.Systems
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct SimEntityTransformSystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            //placeholder
            //will add hierarchy calculations and dirty flagging
            //(parent-child transform relationships, only update when needed)

            //TODO:
            //-----
            //* add transform hierarchy updates for SimEntities
            //* add dirty flagging for performance
            //* add rollback snapshot integration
        }
    }
}