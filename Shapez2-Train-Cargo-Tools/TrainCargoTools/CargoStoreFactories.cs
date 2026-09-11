using System.Linq;
using Core.Factory;
using Game.Content.Features.SpacePaths;
using Game.Core.Simulation;
using ShapezShifter.Flow.Atomic;
using ShapezShifter.Hijack;

namespace TrainCargoTools
{
    /// Builds shape cargo stores.
    ///
    /// Nothing to configure: a store neither packs nor unpacks, so it needs no converter, no
    /// capacity provider and no registry. Its one number is its own, in CargoStoreState.
    ///
    /// Reports the space belt configuration for the same reason the rest of the mod does - it
    /// is what tells research this island's throughput is belt-speed-affected.
    internal sealed class ShapeCargoStoreFactory
        : IIslandSimulationFactoryBuilder<ShapeCargoStoreSimulation, ShapeCargoStoreState, SpacePathConfiguration>
    {
        public IFactory<ShapeCargoStoreState, IslandInstance, ShapeCargoStoreSimulation> BuildFactory(
            SimulationSystemsDependencies dependencies, out SpacePathConfiguration config)
        {
            config = dependencies.Mode.Islands.SpaceBelts.First().ConfigAs<SpacePathConfiguration>();
            return new Factory();
        }

        private sealed class Factory
            : IFactory<ShapeCargoStoreState, IslandInstance, ShapeCargoStoreSimulation>
        {
            public ShapeCargoStoreSimulation Produce(ShapeCargoStoreState state, IslandInstance island)
            {
                return new ShapeCargoStoreSimulation(state);
            }
        }
    }

    /// Builds fluid cargo stores.
    internal sealed class FluidCargoStoreFactory
        : IIslandSimulationFactoryBuilder<FluidCargoStoreSimulation, FluidCargoStoreState, SpacePathConfiguration>
    {
        public IFactory<FluidCargoStoreState, IslandInstance, FluidCargoStoreSimulation> BuildFactory(
            SimulationSystemsDependencies dependencies, out SpacePathConfiguration config)
        {
            config = dependencies.Mode.Islands.SpaceBelts.First().ConfigAs<SpacePathConfiguration>();
            return new Factory();
        }

        private sealed class Factory
            : IFactory<FluidCargoStoreState, IslandInstance, FluidCargoStoreSimulation>
        {
            public FluidCargoStoreSimulation Produce(FluidCargoStoreState state, IslandInstance island)
            {
                return new FluidCargoStoreSimulation(state);
            }
        }
    }
}
