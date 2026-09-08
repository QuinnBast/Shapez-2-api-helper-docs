using System.Collections.Generic;
using System.Text;
using Game.Core.Coordinates;
using Game.Core.Map.Simulation;
using Game.Core.Simulation;

/// <summary>
/// Works out what a selection of platforms looks like from the outside: how big it is, and
/// which of its ports cross the boundary.
///
/// The boundary is the whole point. Anything wired to another platform inside the selection
/// is internal detail and can be hidden; anything wired outward - or left unconnected - has
/// to survive as a port on whatever replaces the selection. That count is what decides
/// whether a 1x1 platform can stand in for it.
/// </summary>
public class SelectionAnalysis
{
    /// One port that crosses out of the selection.
    public struct Port
    {
        public GlobalChunkCoordinate Chunk;
        public PortKind Kind;
        public bool Connected;

        public override string ToString()
        {
            return Kind + " at " + Chunk + (Connected ? "" : " (unconnected)");
        }
    }

    public enum PortKind
    {
        ItemIn,
        ItemOut,
        FluidIn,
        FluidOut
    }

    public int IslandCount;
    public int ChunkCount;
    public int BuildingCount;

    public readonly List<Port> ExternalPorts = new List<Port>();

    /// Ports that connect to another platform in the same selection - hideable detail.
    public int InternalPortCount;

    public int ExternalInputs;
    public int ExternalOutputs;

    /// <summary>
    /// A platform can carry four ports per side per building layer. This is the smallest
    /// square that has room for the ports, ignoring which side each one needs to be on -
    /// so treat it as a floor, not a promise.
    /// </summary>
    public int SuggestedPlatformSize
    {
        get
        {
            int ports = ExternalPorts.Count;
            int size = 1;
            while (ports > size * 4 && size < 8)
            {
                size++;
            }

            return size;
        }
    }

    public static SelectionAnalysis Of(IMapModel map, IEnumerable<IslandModel> selection)
    {
        SelectionAnalysis analysis = new SelectionAnalysis();

        HashSet<IslandId> selected = new HashSet<IslandId>();
        foreach (IslandModel island in selection)
        {
            selected.Add(island.Id);
            analysis.IslandCount++;
            analysis.BuildingCount += island.BuildingsCount;

            foreach (GlobalChunkCoordinate chunk in island.Chunks)
            {
                analysis.ChunkCount++;
            }
        }

        if (analysis.IslandCount == 0)
        {
            return analysis;
        }

        List<ILocalizedSimulation> connected = new List<ILocalizedSimulation>();

        foreach (ILocalizedSimulation localized in map.Simulator.Simulations)
        {
            PortKind kind;
            if (!TryClassifyPort(localized.Simulation, out kind) || localized.NumOccupiedChunks == 0)
            {
                continue;
            }

            GlobalChunkCoordinate chunk = localized.GetOccupiedChunk(0);
            if (!map.TryGetIsland(chunk, out IslandModel owner) || !selected.Contains(owner.Id))
            {
                continue;
            }

            connected.Clear();
            map.Simulator.FindAllConnectedSimulations(localized, connected);

            bool leavesSelection = connected.Count == 0;
            for (int i = 0; i < connected.Count; i++)
            {
                ILocalizedSimulation other = connected[i];
                if (other.NumOccupiedChunks == 0)
                {
                    continue;
                }

                if (!map.TryGetIsland(other.GetOccupiedChunk(0), out IslandModel otherIsland)
                    || !selected.Contains(otherIsland.Id))
                {
                    leavesSelection = true;
                    break;
                }
            }

            if (!leavesSelection)
            {
                analysis.InternalPortCount++;
                continue;
            }

            analysis.ExternalPorts.Add(new Port
            {
                Chunk = chunk,
                Kind = kind,
                Connected = connected.Count > 0
            });

            if (kind == PortKind.ItemIn || kind == PortKind.FluidIn)
            {
                analysis.ExternalInputs++;
            }
            else
            {
                analysis.ExternalOutputs++;
            }
        }

        return analysis;
    }

    /// <summary>
    /// Ports come as one simulation type per direction and per cargo, and there is no shared
    /// interface to test against, so they are named individually.
    /// </summary>
    private static bool TryClassifyPort(ISimulation simulation, out PortKind kind)
    {
        switch (simulation)
        {
            case SpaceBeltPortSenderSimulation _:
            case BeltPortTransferSimulation _:
                kind = PortKind.ItemOut;
                return true;
            case SpaceBeltPortReceiverSimulation _:
                kind = PortKind.ItemIn;
                return true;
            case SpaceFluidPortSenderSimulation _:
                kind = PortKind.FluidOut;
                return true;
            case SpaceFluidPortReceiverSimulation _:
                kind = PortKind.FluidIn;
                return true;
            default:
                kind = PortKind.ItemIn;
                return false;
        }
    }

    public string Describe()
    {
        if (IslandCount == 0)
        {
            return "Nothing selected. Select platforms in space view first.";
        }

        StringBuilder text = new StringBuilder();
        text.Append(IslandCount).Append(IslandCount == 1 ? " platform, " : " platforms, ");
        text.Append(ChunkCount).Append(" chunks, ");
        text.Append(BuildingCount).Append(" buildings");

        text.Append("\ncrossing the boundary: ")
            .Append(ExternalInputs).Append(" in, ")
            .Append(ExternalOutputs).Append(" out");

        if (InternalPortCount > 0)
        {
            text.Append(" (").Append(InternalPortCount).Append(" internal ports would be hidden)");
        }

        text.Append("\nsmallest platform with room for those ports: ")
            .Append(SuggestedPlatformSize).Append("x").Append(SuggestedPlatformSize);

        return text.ToString();
    }
}
