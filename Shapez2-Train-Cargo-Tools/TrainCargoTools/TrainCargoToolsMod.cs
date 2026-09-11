using System;
using System.Collections.Generic;
using System.IO;
using Core.Collections;
using Core.Localization;
using Game.Content.Features.SpacePaths.IslandIO;
using Game.Core.Content.Islands;
using Game.Core.Coordinates;
using Game.Core.Simulation;
using JetBrains.Annotations;
using ShapezShifter.Flow;
using ShapezShifter.Flow.Atomic;
using ShapezShifter.Flow.Research;
using ShapezShifter.Flow.Toolbar;
using ShapezShifter.Hijack;
using ShapezShifter.Kit;
using ShapezShifter.Textures;
using UnityEngine;
using ILogger = Core.Logging.ILogger;

namespace TrainCargoTools
{
    /// Train cargo outside a train station.
    ///
    /// Three pieces. A packager turns loose shapes into cargo packages, a cargo belt carries them,
    /// an unpackager turns them back. The point is buffering: a station holds only a few
    /// containers, so a late train stalls everything feeding it, and a cargo belt is buffer you
    /// can build more of - one belt slot holds a whole package instead of one shape.
    ///
    /// A stock train station would refuse a package - its filling container gates on
    /// `item is ShapeItem` - so PackagedCargoStations detours that gate and every shape station
    /// accepts packages directly. A packed line therefore runs straight into a station with no
    /// unpackager in front of it, which is the whole point: the cargo belt behind it is the
    /// buffer. See DESIGN.md.
    [UsedImplicitly]
    public class TrainCargoToolsMod : IMod
    {
        private readonly PackagedCargoStations Stations;

        /// Static because the resource lookup is, and it wants somewhere to warn.
        private static ILogger Log;

        /// Registrations for the two drag placers, so Dispose can take them back out again.
        private readonly RewirerHandle PathPlacement;
        private readonly RewirerHandle FluidPathPlacement;

        /// Two folders under Rail, one per cargo line.
        ///
        /// Shapes and fluids are split because they are not interchangeable: the two lines use
        /// different connector tags and nothing crosses between them, so a player laying a
        /// shape line has no use for any of the fluid pieces and vice versa. Showing both in
        /// one folder meant eight entries, six of them wrong for whatever you were building.
        ///
        /// Sibling groups rather than one folder with two sub-tabs. Vanilla nests exactly one
        /// level - Category, then Group, then entries - and nothing in the shipped toolbar puts
        /// a Group inside a Group. ToolbarBuilder recurses on any IParentToolbarElementData so
        /// it would probably build, but whether the HUD *renders* a second level is untested,
        /// and two folders at a depth known to work gets the same separation.
        ///
        /// Appended to Rail, which puts them after the train dock groups - the closest the
        /// name-based API gets to "next to the wagon loaders" without an index path.
        private IToolbarEntryInsertLocation ToolbarSlot()
        {
            return ToolbarSlot(fluid: false);
        }

        private IToolbarEntryInsertLocation ToolbarSlot(bool fluid)
        {
            return Shapez2.ToolbarKit.ToolbarSlot.InNewGroup(
                Shapez2.ToolbarKit.ToolbarCategory.Rail,
                fluid ? "cargo-tools.toolbar.fluids.title" : "cargo-tools.toolbar.shapes.title",
                LoadIcon(fluid ? "FluidCargoBelt.png" : "CargoBelt.png"),
                fluid
                    ? "cargo-tools.toolbar.fluids.description"
                    : "cargo-tools.toolbar.shapes.description");
        }

        /// The mod's Resources folder. See ModResources for why it is not just the locator.
        private static ModFolderLocator Resources()
        {
            return ModResources.Locate(Log);
        }

        private static UnityEngine.Sprite LoadIcon(string file)
        {
            return FileTextureLoader.LoadTextureAsSprite(Resources().SubPath(file), out _);
        }

        public TrainCargoToolsMod(ILogger logger)
        {
            Log = logger;
            Shapez2.ToolbarKit.ToolbarKit.Log = logger;

            // The whole belt-tagged family is hidden. One drag entry stands for all three,
            // registered further down once the placer exists.
            AddIsland("CargoBelt", "cargo-belt", "CargoBelt.png",
                new CargoBeltSimulationFactory(), hidden: true);

            // Dragging picks a corner when the run turns, so a player never reaches for one by
            // hand. They still have to exist as definitions for the placer's definition finder
            // to choose from.
            AddIsland("CargoBelt_LeftTurn", "cargo-belt-left", "CargoBeltLeft.png",
                new CargoBeltSimulationFactory(), outputDirection: ChunkDirection.North,
                hidden: true);

            AddIsland("CargoBelt_RightTurn", "cargo-belt-right", "CargoBeltRight.png",
                new CargoBeltSimulationFactory(), outputDirection: ChunkDirection.South,
                hidden: true);

            // The pipe-tagged twin of the above, and the reason it has to exist: a fluid train
            // station's input is a SpacePipeInputConnector, and a belt-tagged output will not
            // snap to it however the lanes are configured. Same simulation, same lanes, same
            // state - only the connector tags differ.
            AddIsland("FluidCargoBelt", "fluid-cargo-belt", "FluidCargoBelt.png",
                new CargoBeltSimulationFactory(),
                inputIsPipe: true, outputIsPipe: true, hidden: true);

            AddIsland("FluidCargoBelt_LeftTurn", "fluid-cargo-belt-left", "FluidCargoBeltLeft.png",
                new CargoBeltSimulationFactory(), outputDirection: ChunkDirection.North,
                inputIsPipe: true, outputIsPipe: true, hidden: true);

            AddIsland("FluidCargoBelt_RightTurn", "fluid-cargo-belt-right", "FluidCargoBeltRight.png",
                new CargoBeltSimulationFactory(), outputDirection: ChunkDirection.South,
                inputIsPipe: true, outputIsPipe: true, hidden: true);

            AddIsland("CargoPackager", "cargo-packager", "CargoPackager.png",
                new ShapeCargoPackagerFactory());

            AddIsland("CargoUnpackager", "cargo-unpackager", "CargoUnpackager.png",
                new ShapeCargoUnpackagerFactory());

            // Both fluid ends sit on the pipe-tagged line, so both sides are pipes.
            AddIsland("FluidCargoPackager", "fluid-cargo-packager", "FluidCargoPackager.png",
                new FluidCargoPackagerFactory(), inputIsPipe: true, outputIsPipe: true, fluidLine: true);

            AddIsland("FluidCargoUnpackager", "fluid-cargo-unpackager", "FluidCargoUnpackager.png",
                new FluidCargoUnpackagerFactory(), inputIsPipe: true, outputIsPipe: true, fluidLine: true);

            // Buffers, one per line. Placed singly rather than dragged - a store is a thing you
            // put somewhere, not a run you lay out.
            AddIsland("CargoStore", "cargo-store", "CargoStore.png",
                new ShapeCargoStoreFactory(),
                modules: new CargoStoreModules(LoadIcon("CargoStore.png")));

            AddIsland("FluidCargoStore", "fluid-cargo-store", "FluidCargoStore.png",
                new FluidCargoStoreFactory(), inputIsPipe: true, outputIsPipe: true,
                fluidLine: true, modules: new CargoStoreModules(LoadIcon("FluidCargoStore.png")));

            Stations = new PackagedCargoStations(logger);

            // Drag placement, one placer per line. Each registers a path placer over its family
            // and gives it the single toolbar entry that stands for the whole thing.
            PathPlacement = GameRewirers.AddRewirer(
                new CargoPathPlacement<SpaceBeltInputConnector, SpaceBeltOutputConnector>(
                    logger, "CargoBeltPlacementInitiator",
                    "CargoBelt", "CargoBelt_LeftTurn", "CargoBelt_RightTurn",
                    "cargo-tools.group.cargo-belt.title", "cargo-tools.group.cargo-belt.description",
                    fluidPorts: false, () => LoadIcon("CargoBelt.png"),
                    () => ToolbarSlot(fluid: false)));

            FluidPathPlacement = GameRewirers.AddRewirer(
                new CargoPathPlacement<SpacePipeInputConnector, SpacePipeOutputConnector>(
                    logger, "FluidCargoBeltPlacementInitiator",
                    "FluidCargoBelt", "FluidCargoBelt_LeftTurn", "FluidCargoBelt_RightTurn",
                    "cargo-tools.group.fluid-cargo-belt.title",
                    "cargo-tools.group.fluid-cargo-belt.description",
                    fluidPorts: true, () => LoadIcon("FluidCargoBelt.png"),
                    () => ToolbarSlot(fluid: true)));
        }

        public void Dispose()
        {
            GameRewirers.RemoveRewirer(PathPlacement);
            GameRewirers.RemoveRewirer(FluidPathPlacement);
            Stations.Dispose();
        }

        /// Everything the pieces have in common: one chunk of unbuildable space path, an input
        /// West and an output East, a toolbar entry, and a stateful simulation.
        ///
        /// Only the fluid pieces differ, and only in their connector tags - the tag is what
        /// decides what an island will join to, and a fluid train station only joins to a pipe.
        private void AddIsland<TSimulation, TState, TConfig>(
            string id, string slug, string iconFile,
            IIslandSimulationFactoryBuilder<TSimulation, TState, TConfig> simulation,
            ChunkDirection? outputDirection = null,
            bool inputIsPipe = false, bool outputIsPipe = false, bool hidden = false,
            bool fluidLine = false, IIslandModuleDataProvider modules = null)
            where TSimulation : Simulation<TState>
            where TState : class, ISimulationState, new()
        {
            IslandDefinitionGroupId groupId = new($"{id}Group");
            IslandDefinitionId definitionId = new(id);

            string titleId = $"cargo-tools.group.{slug}.title";
            string descriptionId = $"cargo-tools.group.{slug}.description";

            IIslandGroupBuilder groupBuilder = IslandGroup.Create(groupId)
               .WithTitle(titleId.T())
               .WithDescription(descriptionId.T())
               .WithIcon(LoadIcon(iconFile))
               .AsNonTransportableIsland()
               .WithPreferredPlacement(DefaultPreferredPlacementMode.Area);

            ChunkLayoutLookup<ChunkVector, IslandChunkData> layout = SingleChunkLayout();

            IIslandBuilder islandBuilder = Island.Create(definitionId)
               .WithLayout(layout)
               // Per-chunk, never bounding. WithBoundingCollider takes the min and max
               // chunk position and uses (max - min) as the box size, so a one-chunk
               // island gets min == max and a box of size zero - invisible to the
               // raycast, so the platform cannot be clicked. It is wrong for multi-chunk
               // islands too: the z extent is always 0 and x/y come out one chunk short.
               .WithPerChunkColliders()
               .WithConnectorData(Connectors(
                    layout, outputDirection ?? ChunkDirection.East, inputIsPipe, outputIsPipe))
               .WithInteraction(flippable: false, canHoldBuildings: false)
               .WithDefaultChunkCost()
               .WithRenderingOptions(
                    new HomogeneousChunkDrawing(ChunkPlatformDrawingContext.DrawAll()),
                    drawPlayingField: true);

            IAtomicIslandExtender extender = AtomicIslands.Extend()
               .AllScenarios()
               .WithIsland(islandBuilder, groupBuilder)
               .UnlockedAtMilestone(new ByIndexMilestoneSelector(0))
               .WithDefaultPlacement()
               .InToolbar(hidden
                    ? Shapez2.ToolbarKit.ToolbarSlot.Hidden()
                    : ToolbarSlot(fluidLine))
               .WithSimulation(simulation);

            // Two separate builder methods rather than one taking null, because
            // that is the shape Shifter offers.
            IIslandExtender complete = modules == null
                ? extender.WithoutModules()
                : extender.WithCustomModules(modules);

            complete.Build();
        }

        /// One chunk, no buildable tiles - the shape of a space path segment.
        private ChunkLayoutLookup<ChunkVector, IslandChunkData> SingleChunkLayout()
        {
            return new ChunkLayoutLookup<ChunkVector, IslandChunkData>(ChunkData());
        }

        private IEnumerable<KeyValuePair<ChunkVector, IslandChunkData>> ChunkData()
        {
            ChunkVector origin = new(0, 0, 0);

            IslandChunkData chunkData = IslandLayoutFactory.CreateIslandChunkData(
                chunkTile: origin,
                notchDirections: Array.Empty<ChunkDirection>(),
                neighborChunks: origin.AsEnumerable(),
                isBuildable: true,
                flipped: false,
                out _);

            for (int i = 0; i < chunkData.TileVoidFlags_L.Length; i++)
            {
                chunkData.TileVoidFlags_L[i] = true;
            }

            yield return new KeyValuePair<ChunkVector, IslandChunkData>(origin, chunkData);
        }

        /// West in, East out.
        ///
        /// Every piece carries cargo on a *belt* connector, fluid ends included. Reusing
        /// SpaceBeltInput/OutputConnector for cargo rather than inventing a cargo connector is
        /// not a shortcut, it is the only option: ConnectableIslandSimulation switches on the
        /// connector class to decide the chunk connector's item type - SpaceBeltInputConnector
        /// maps to ShapeItem, SpacePipeInputConnector to FluidPackageItem - and throws
        /// NotImplementedException for anything else.
        ///
        /// The item type on those chunk connectors is only a compatibility tag, though.
        /// ItemInputChunkConnector&lt;TItem&gt;.CanConnect tests
        /// `other is IItemOutputChunkConnector&lt;TItem&gt;`, which decides which paths may join;
        /// what actually flows is whatever the lanes accept. That is what lets a packager emit
        /// packages through a shape-tagged connector, and it has a visible cost: a cargo belt
        /// will connect to an ordinary space belt and then silently refuse its shapes.
        ///
        /// The pipe connectors are the exception and behave properly, because a pipe genuinely
        /// does carry FluidPackageItems: a fluid packager's input will only join a pipe, and a
        /// fluid unpackager's output will only join a pipe.
        private IIslandConnectorData Connectors(
            ChunkLayoutLookup<ChunkVector, IslandChunkData> layout, ChunkDirection outputDirection,
            bool inputIsPipe, bool outputIsPipe)
        {
            IIslandConnector input = inputIsPipe
                ? new SpacePipeInputConnector()
                : (IIslandConnector)new SpaceBeltInputConnector();

            IIslandConnector output = outputIsPipe
                ? new SpacePipeOutputConnector()
                : (IIslandConnector)new SpaceBeltOutputConnector();

            return new IslandConnectorData(
                new[]
                {
                    Connector(ChunkDirection.West, input),
                    Connector(outputDirection, output)
                },
                layout.ChunkPositions);

            EntityIO<LocalChunkPivot, IIslandConnector> Connector(ChunkDirection dir, IIslandConnector connector)
            {
                LocalChunkPivot pivot = new(ChunkVector.Zero, dir);
                return new EntityIO<LocalChunkPivot, IIslandConnector>(pivot, connector);
            }
        }
    }
}
