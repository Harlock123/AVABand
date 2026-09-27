using Angband.Core.Definitions;
using Angband.Core.Geometry;

namespace Angband.Core.Generation.Rooms;

/// <summary>
/// A room chosen and sized but not yet placed. <see cref="Draw"/> receives the top-left corner of the
/// <see cref="Width"/> x <see cref="Height"/> footprint (walls included) and returns the square
/// tunnels should aim for.
/// </summary>
internal sealed record RoomPlan(int Width, int Height, Func<GenContext, Loc, Loc> Draw);

internal abstract class RoomBuilder
{
    public abstract string Type { get; }

    /// <summary>Picks this room's shape for the current depth, or null when it cannot be built here.</summary>
    public abstract RoomPlan? Plan(GenContext ctx);
}

internal static class RoomBuilders
{
    private static readonly Dictionary<string, RoomBuilder> ByType = new RoomBuilder[]
    {
        new SimpleRoomBuilder(),
        new OverlapRoomBuilder(),
        new CrossedRoomBuilder(),
        new LargeRoomBuilder(),
        new CircularRoomBuilder(),
        new MonsterPitBuilder(nest: false),
        new MonsterPitBuilder(nest: true),
        new TemplateRoomBuilder("template", MapTemplateKind.Room),
        new TemplateRoomBuilder("lesser_vault", MapTemplateKind.LesserVault),
        new TemplateRoomBuilder("medium_vault", MapTemplateKind.MediumVault),
        new TemplateRoomBuilder("greater_vault", MapTemplateKind.GreaterVault),
    }.ToDictionary(b => b.Type, StringComparer.Ordinal);

    public static IReadOnlyCollection<string> Types => ByType.Keys;

    public static bool TryGet(string type, out RoomBuilder builder) => ByType.TryGetValue(type, out builder!);
}
