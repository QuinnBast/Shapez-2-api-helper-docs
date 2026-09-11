using System;
using System.Collections.Generic;
using Core.Localization;
using Game.Content.Features.SpacePaths.IslandIO;
using Game.Core.Content.Islands;
using Game.Core.Coordinates;
using ShapezShifter;
using ShapezShifter.Flow.Toolbar;
using ShapezShifter.Hijack;
using UnityEngine;
using ILogger = Core.Logging.ILogger;

namespace TrainCargoTools
{
    /// Makes cargo belts drag out like space belts, corners and all.
    ///
    /// A space belt is not one island, it is a *family* - forward, turns, and in vanilla also
    /// splitters, mergers and lifts - plus a placer that picks the right member for each node
    /// of a dragged run. Placing cargo belts one chunk at a time was not a missing feature so
    /// much as a missing placer.
    ///
    /// Nothing here reimplements dragging. PlatformIslandsPlacersCreators already builds exactly
    /// the placer wanted, generically, and the pieces it needs are all reachable:
    ///
    /// - MatchingDefinitionFinder is constructible over any list of definitions, so the cargo
    ///   family becomes a path family just by handing it one.
    /// - IslandInitiatorsParams, which is what a placement rewirer is given, happens to carry
    ///   every single argument PlatformIslandsPlacersCreators' constructor wants. So rather than
    ///   copying the placer's guts, this builds an instance of the game's own creator purely to
    ///   call the factory on it. That constructor is field assignment and nothing else, and
    ///   RegisterPlacers is never called on it, so the throwaway instance has no side effects.
    /// Also an IToolbarDataRewirer, because the toolbar entry cannot be built until the placer
    /// is registered: a placement entry holds a PlacementInitiatorId, and that id only exists
    /// once RegisterInitiator has returned it. One object doing both phases is the simplest way
    /// to carry the id from one to the other.
    ///
    /// Generic over the connector pair because there are two cargo paths, not one: the shape
    /// line is belt-tagged, the fluid line pipe-tagged. They have to be, because the tag is
    /// what decides which islands will *join*. A fluid train station takes a
    /// SpacePipeInputConnector, and ItemInputChunkConnector&lt;FluidPackageItem&gt;.CanConnect
    /// will not accept a belt-tagged output no matter what the lanes actually carry - which is
    /// why a belt-tagged cargo belt would not snap to a fluid wagon loader at all.
    internal sealed class CargoPathPlacement<TInput, TOutput>
        : IPlatformIslandPlacementRewirers, IToolbarDataRewirer
        where TInput : class, IEntityConnector, new()
        where TOutput : class, IEntityConnector, new()
    {
        private readonly string PlacerSerialName;
        private readonly string ForwardId;
        private readonly string LeftTurnId;
        private readonly string RightTurnId;
        private readonly string TitleId;
        private readonly string DescriptionId;
        private readonly bool FluidPorts;

        private readonly ILogger Logger;
        private readonly Func<Sprite> Icon;
        private readonly Func<IToolbarEntryInsertLocation> Slot;

        /// Set during placer registration, read during toolbar rewiring.
        private PlacementInitiatorId? Placer;

        public CargoPathPlacement(
            ILogger logger, string placerSerialName, string forwardId, string leftTurnId,
            string rightTurnId, string titleId, string descriptionId, bool fluidPorts,
            Func<Sprite> icon, Func<IToolbarEntryInsertLocation> slot)
        {
            Logger = logger;
            PlacerSerialName = placerSerialName;
            ForwardId = forwardId;
            LeftTurnId = leftTurnId;
            RightTurnId = rightTurnId;
            TitleId = titleId;
            DescriptionId = descriptionId;
            FluidPorts = fluidPorts;
            Icon = icon;
            Slot = slot;
        }

        public void ModifyIslandPlacers(
            IslandInitiatorsParams initiators, IPlacementInitiatorIdRegistry registry)
        {
            try
            {
                if (!TryFamily(initiators.Islands, out IIslandDefinition forward,
                        out IReadOnlyList<IIslandDefinition> family))
                {
                    // A scenario this mod did not extend. Nothing to place.
                    return;
                }

                IMatchingDefinitionFinder<IslandDescriptor, GlobalChunkPivot> finder =
                    new MatchingDefinitionFinder<IslandDescriptor, ChunkVector, ChunkDirection,
                        GlobalChunkCoordinate, LocalChunkPivot, GlobalChunkPivot, GlobalChunkTransform,
                        TInput, TOutput>(family, new IslandPlacementAdapter());

                PlatformIslandsPlacersCreators creators = new(
                    initiators.Buildings, initiators.Islands, initiators.MaxBuildingLayer,
                    initiators.ProgressManager, initiators.EntityPlacementRunner,
                    initiators.IslandsModulesLookup,

                    // A scratch pipette map, deliberately not the real one.
                    //
                    // CreateSpacePathPlacementInitiator registers every member of the family
                    // for pipetting, and DefaultIslandPlacementExtender - which the builder
                    // chain gives no way to skip - registers each island again. Two Add calls
                    // for one definition, and Dictionary.Add throws on a duplicate key, so
                    // whichever ran second took the game's startup down with
                    // "An item with the same key has already been added. Key: CargoBelt".
                    //
                    // Letting the path placer write into a throwaway dictionary settles it
                    // without depending on which of the two runs first. The cost is that
                    // pipetting a cargo belt picks the single-chunk placer rather than the
                    // drag placer, which is a fair trade for not crashing.
                    new Dictionary<IEntityDefinition, PipettePlacementRequest>(),

                    initiators.TutorialState,
                    initiators.ChunkLimitManager, initiators.ViewportLayersController,
                    initiators.RailColorRegistry, Logger);

                IPlacementInitiator initiator = creators
                   .CreateSpacePathPlacementInitiator<TInput, TOutput>(
                        finder,
                        family,
                        forward,
                        // The port buildings a lifted path uses to bridge a layer. Vanilla
                        // pairs its belt placer with the belt ports and its pipe placer with
                        // the fluid ones, so a pipe-tagged cargo path follows the pipe placer.
                        FluidPorts ? initiators.Buildings.FluidPortSender : initiators.Buildings.BeltPortSender,
                        FluidPorts ? initiators.Buildings.FluidPortReceiver : initiators.Buildings.BeltPortReceiver,

                        // Placements of interest drive tutorial prompts. UnknownIsland is the
                        // enum's own value for "not one of the builtin ones", so a cargo belt
                        // cannot accidentally satisfy a step about space belts.
                        BuiltinPlacementOfInterest.UnknownIsland,
                        initiators.ViewportLayersController);

                Placer = registry.RegisterInitiator(new SerializedPlacerId(PlacerSerialName), initiator);
                Logger.Info?.Log($"{ForwardId} can be dragged.");
            }
            catch (Exception exception)
            {
                // A broken placer must not take the build menu with it. Without the catch, every
                // island placement throws, not just this one.
                Logger.Exception?.LogException(exception);
            }
        }

        /// The forward piece plus its turns.
        ///
        /// Looked up rather than held from construction, because definitions are rebuilt for
        /// every scenario load while this rewirer outlives them. No lifts: a cargo line
        /// therefore cannot cross another cargo line the way a space belt can, which is a known
        /// gap rather than an oversight - lifts would need their own multi-chunk definitions.
        private bool TryFamily(
            GameIslands islands, out IIslandDefinition forward,
            out IReadOnlyList<IIslandDefinition> family)
        {
            forward = null;
            family = null;

            if (!islands.TryGetDefinition(new IslandDefinitionId(ForwardId), out forward) ||
                !islands.TryGetDefinition(new IslandDefinitionId(LeftTurnId),
                    out IIslandDefinition left) ||
                !islands.TryGetDefinition(new IslandDefinitionId(RightTurnId),
                    out IIslandDefinition right))
            {
                return false;
            }

            // Forward first: CreateSpacePathPlacementInitiator uses the first entry as the
            // representing node when it builds the placer.
            family = new[] { forward, left, right };
            return true;
        }

        public ToolbarData ModifyToolbarData(ToolbarData toolbarData)
        {
            try
            {
                if (!Placer.HasValue)
                {
                    // Either the family was missing or placer registration threw - both already
                    // logged. Say so anyway, because the visible symptom is a missing button.
                    Logger.Warning?.Log(
                        $"The {ForwardId} drag placer was never registered, so it has no toolbar " +
                        "entry and those belts cannot be placed at all.");
                    return toolbarData;
                }

                PlacementToolbarElementData entry = new(
                    TitleId.T(),
                    DescriptionId.T(),
                    Placer.Value,
                    Icon());

                Slot().AddEntry(toolbarData, entry);
            }
            catch (Exception exception)
            {
                Logger.Exception?.LogException(exception);
            }

            return toolbarData;
        }
    }
}
