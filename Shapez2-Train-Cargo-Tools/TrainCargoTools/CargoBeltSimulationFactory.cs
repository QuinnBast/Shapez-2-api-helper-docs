using System.Linq;
using Core.Factory;
using Game.Core.Simulation;
using ShapezShifter.Flow.Atomic;
using ShapezShifter.Hijack;

namespace TrainCargoTools
{
    /// Gives a cargo belt the same speed as a vanilla space belt.
    ///
    /// Read off the space belt island definition rather than hard-coded, so research speed
    /// upgrades apply - SpaceConveyorSpeed is a BuffableBeltSpeed carrying its own
    /// ResearchSpeedId, and returning that configuration is what tells research this island is
    /// affected.
    internal sealed class CargoBeltSimulationFactory
        : IIslandSimulationFactoryBuilder<CargoBeltSimulation, CargoBeltSimulationState, SpacePathConfiguration>
    {
        public IFactory<CargoBeltSimulationState, IslandInstance, CargoBeltSimulation> BuildFactory(
            SimulationSystemsDependencies dependencies, out SpacePathConfiguration config)
        {
            config = dependencies.Mode.Islands.SpaceBelts.First().ConfigAs<SpacePathConfiguration>();
            return new Factory(config.SpaceConveyorSpeed);
        }

        private sealed class Factory : IFactory<CargoBeltSimulationState, IslandInstance, CargoBeltSimulation>
        {
            private readonly BeltSpeed Speed;

            public Factory(BeltSpeed speed)
            {
                Speed = speed;
            }

            public CargoBeltSimulation Produce(CargoBeltSimulationState state, IslandInstance island)
            {
                return new CargoBeltSimulation(Speed, state);
            }
        }
    }
}
