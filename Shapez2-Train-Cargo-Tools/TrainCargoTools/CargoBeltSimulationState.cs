using Game.Content.Features.SpacePaths;
using Game.Core.Belts.BeltPath;
using Game.Core.Serialization;
using Game.Core.Simulation;

namespace TrainCargoTools
{
    /// One 12-lane bundle, saved.
    ///
    /// Mirrors the vanilla SpaceConveyorSimulationState. The slot count is deliberately larger
    /// than a space belt: buffering is the whole reason a cargo belt exists, since a train
    /// station holds only a handful of containers and a stalled train stalls the line behind it.
    [SyncableIdentifier("TrainCargoToolsCargoBeltState")]
    public class CargoBeltSimulationState : ISimulationState, ISyncable
    {
        private const short NumItemsPerLane = 32;

        public BundleState<FastBeltPathLaneState> PathBundleState { get; }

        public CargoBeltSimulationState()
        {
            PathBundleState = BundleState.Create(() => new FastBeltPathLaneState(NumItemsPerLane));
        }

        public void Sync(ISerializationVisitor visitor)
        {
            PathBundleState.Sync(visitor);
        }
    }
}
