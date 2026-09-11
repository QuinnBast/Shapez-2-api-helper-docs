using System;
using Core.Pooling;
using Game.Core.Simulation;
using Game.Core.Trains;

namespace TrainCargoTools
{
    /// Takes a cargo package off a belt and drops it into a filling container's state.
    ///
    /// The one piece the game does not already provide. Unloading at a station starts from a
    /// train, so TrainCargoToBeltFillingContainer has a LoadPackage method and no receiver side -
    /// nothing in vanilla ever accepts a package that arrived on a belt. This is that missing
    /// half: it writes into the same TrainCargoFillingContainerState the filling container reads
    /// from, so the two together are a complete unpackager.
    ///
    /// Accepting only while the state is empty is what makes an unpackager back-pressure
    /// correctly: the belt behind it holds packages until the current one has been fully drained
    /// onto the output.
    internal sealed class CargoPackageReceiver<TItem> : IItemReceiver
        where TItem : unmanaged, IEquatable<TItem>
    {
        private readonly TrainCargoFillingContainerState<TItem> State;
        private readonly Pool<PackageOnTrack<CargoPackage<TItem>>> PackagePool;

        /// The value the game's own filling container reports. A receiver that is not a lane still
        /// has to tell the belt behind it how far items may advance.
        public Steps MaxStep_S => LaneConstants.ItemSpacingHalf * 12;

        public CargoPackageReceiver(
            TrainCargoFillingContainerState<TItem> state,
            Pool<PackageOnTrack<CargoPackage<TItem>>> packagePool)
        {
            State = state;
            PackagePool = packagePool;
        }

        public bool CanAcceptItem(IBeltItem itemToTransfer)
        {
            return State.Package.IsEmpty && itemToTransfer is PackageOnTrack<CargoPackage<TItem>>;
        }

        public void HandOverItem(IBeltItem itemToTransfer, Ticks remainingTicks)
        {
            PackageOnTrack<CargoPackage<TItem>> wrapper =
                (PackageOnTrack<CargoPackage<TItem>>)itemToTransfer;

            // Copy before returning: Pool.Return clears the wrapper's Container.
            State.Package = wrapper.Container;
            PackagePool.Return(wrapper);
        }
    }
}
