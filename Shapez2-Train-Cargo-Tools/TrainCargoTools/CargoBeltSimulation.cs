using Game.Content.Features.Fluids;
using Game.Content.Features.SpacePaths;
using Game.Core.Belts.BeltPath;
using Game.Core.Simulation;
using Game.Core.Trains;

namespace TrainCargoTools
{
    /// A straight space path that carries train cargo packages instead of loose shapes.
    ///
    /// Structurally this is the vanilla SpaceConveyorSimulation: one 12-lane bundle handed out
    /// as both the receiver and the provider, so items enter one side and leave the other.
    ///
    /// What makes it a *cargo* belt is the accept hook. A cargo container already travels as an
    /// ordinary belt item - PackageOnTrack&lt;TContainer&gt; implements IBeltItem, and the game
    /// already runs them down a BeltPathLane inside every train station via CargoPackageTrack.
    /// Nothing about a belt lane objects to carrying one. The hook is what stops loose shapes
    /// getting on, which is what keeps a cargo line legible and stops it silently acting as a
    /// very high capacity shape belt.
    public class CargoBeltSimulation : Simulation<CargoBeltSimulationState>, IItemBundleSimulation,
        ISimulation, IUpdatableSimulation
    {
        public readonly ItemLaneBundle<FastBeltPathLane> PathBundle;

        public int NumItemReceiverBundles => 1;

        public int NumItemProviderBundles => 1;

        public CargoBeltSimulation(BeltSpeed speed, CargoBeltSimulationState state)
            : base(state)
        {
            PathBundle = ItemLaneBundle.Create(state.PathBundleState,
                (FastBeltPathLaneState laneState) =>
                {
                    FastBeltPathLane lane = new(speed, laneState);
                    lane.PreAcceptHook = IsCargo;
                    return lane;
                });
        }

        /// Only train cargo rides a cargo belt.
        ///
        /// Both concrete package types are admitted - the game instantiates its cargo machinery
        /// as CargoPackage&lt;ShapeId&gt; and CargoPackage&lt;FluidId&gt; (see
        /// CargoExchangingOrchestrator) - so one belt type serves both rather than needing a
        /// shape variant and a fluid variant.
        private static bool IsCargo(IBeltItem item)
        {
            return item is PackageOnTrack<CargoPackage<ShapeId>>
                || item is PackageOnTrack<CargoPackage<FluidId>>;
        }

        public void ClearContent()
        {
            PathBundle.Clear();
        }

        public void TraverseLanes<TTraverser>(TTraverser traverser) where TTraverser : IItemLaneTraverser
        {
            PathBundle.TraverseLanes(traverser);
        }

        public void Update(Ticks startTicks, Ticks deltaTicks)
        {
            PathBundle.Update(deltaTicks);
        }

        public IItemReceiverBundle GetItemReceiverBundle(int inputIndex)
        {
            return PathBundle;
        }

        public IItemProviderBundle GetItemProviderBundle(int outputIndex)
        {
            return PathBundle;
        }
    }
}
