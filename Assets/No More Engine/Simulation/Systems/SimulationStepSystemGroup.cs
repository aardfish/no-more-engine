using Unity.Entities;
using UnityEngine;


namespace NoMoreEngine.Simulation.Systems
{
    /// <summary>
    /// System group that executes once per simulation step at fixed timestep
    /// All deterministic simulation systems should run within this group
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.Default | WorldSystemFilterFlags.Editor)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(BeginSimulationEntityCommandBufferSystem))]
    public partial class SimulationStepSystemGroup : ComponentSystemGroup
    {
        private bool isEnabled = true;
        
        protected override void OnCreate()
        {
            base.OnCreate();
            
            // Set the fixed timestep rate for this group
            // Note: The actual stepping is controlled by SimulationTimeSystem
            RateManager = new RateUtils.FixedRateCatchUpManager(1f / 60f);
            
            Debug.Log("[SimulationStepSystemGroup] Created with 60Hz update rate");
        }
        
        protected override void OnUpdate()
        {
            // This is called by SimulationTimeSystem when it's time to step
            // Only update if enabled (for pause functionality)
            if (isEnabled)
            {
                base.OnUpdate();
            }
        }
        
        /// <summary>
        /// Enable or disable the simulation group (for pausing)
        /// </summary>
        public void SetEnabled(bool enabled)
        {
            isEnabled = enabled;
        }
        
        public bool IsEnabled => isEnabled;
    }
    
    /// <summary>
    /// Phases within the fixed timestep simulation for explicit ordering
    /// </summary>
    [UpdateInGroup(typeof(SimulationStepSystemGroup), OrderFirst = true)]
    public partial class InputProcessingPhase : ComponentSystemGroup { }
    
    [UpdateInGroup(typeof(SimulationStepSystemGroup))]
    [UpdateAfter(typeof(InputProcessingPhase))]
    public partial class PhysicsPhase : ComponentSystemGroup { }
    
    [UpdateInGroup(typeof(SimulationStepSystemGroup))]
    [UpdateAfter(typeof(PhysicsPhase))]
    public partial class GameplayPhase : ComponentSystemGroup { }
    
    [UpdateInGroup(typeof(SimulationStepSystemGroup), OrderLast = true)]
    public partial class CleanupPhase : ComponentSystemGroup { }
}