using Angband.Core.Definitions;
using Angband.Core.Geometry;
using Angband.Core.World;

namespace Angband.Core.Generation.Rooms;

/// <summary>Builds hand-drawn rooms and vaults from <see cref="MapTemplateDef"/> data.</summary>
internal sealed class TemplateRoomBuilder(string type, MapTemplateKind kind) : RoomBuilder
{
    public override string Type => type;

    public override RoomPlan? Plan(GenContext ctx)
    {
        var candidates = ctx.Data.Templates
            .Where(t => t.Kind == kind && t.MinDepth <= ctx.Depth && t.MaxDepth >= ctx.Depth)
            .ToList();
        var template = ctx.Rng.PickWeighted(candidates, t => t.Weight);
        if (template is null) return null;

        IReadOnlyList<string> rows = template.Rows;
        if (template.Rotatable)
        {
            var turns = ctx.Rng.RandInt0(4);
            for (var i = 0; i < turns; i++) rows = TemplateLegend.RotateClockwise(rows);
            if (ctx.Rng.OneIn(2)) rows = TemplateLegend.MirrorHorizontally(rows);
        }

        var isVault = kind != MapTemplateKind.Room;
        var lit = kind switch
        {
            MapTemplateKind.Room => ctx.RollRoomLight(),
            MapTemplateKind.LesserVault => true,
            _ => false,
        };
        var flags = SquareFlags.Room | GenContext.Light(lit)
                    | (isVault ? SquareFlags.Vault | SquareFlags.NoStairs | SquareFlags.NoTrap : SquareFlags.None);

        var height = rows.Count;
        var width = rows[0].Length;
        return new RoomPlan(width, height, (c, tl) =>
        {
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                TemplateLegend.Apply(c, new Loc(tl.X + x, tl.Y + y), rows[y][x], flags, TemplateLegend.LetterDepthBonus(kind));
            if (isVault) c.Level.Vaults.Add(template.Name.Length > 0 ? template.Name : template.Id);
            return new Loc(tl.X + width / 2, tl.Y + height / 2);
        });
    }
}
