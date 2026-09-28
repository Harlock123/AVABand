namespace Angband.Core.Game;

// Command counts (Angband 4.2 cmd-core.c): a command given a count repeats until the count runs
// out, the command has nothing more to do, or something disturbs the player.
public sealed partial class GameSession
{
    /// <summary>The largest count Angband's "Repeat:" prompt takes.</summary>
    public const int MaxCommandCount = 9999;

    /// <summary>How often opening, disarming and tunnelling try by themselves (cmd-core.c auto_repeat_n).</summary>
    public const int AutoRepeatCount = 99;

    /// <summary>
    /// Set by a command that failed but could succeed if tried again (a lock not picked, a trap not
    /// disarmed): Angband's "more", which keeps a repeated open or disarm going.
    /// </summary>
    private bool _more;

    /// <summary>Whether a command can be given a count (Angband's repeat_allowed).</summary>
    public static bool TakesCount(GameCommand command) =>
        command is WalkCommand or HoldCommand or OpenCommand or CloseCommand or TunnelCommand or DisarmCommand;

    private bool Repeat(GameCommand command, int count)
    {
        if (!TakesCount(command)) return ExecuteOnce(command);
        if (command is TunnelCommand tunnel) return !IsGameOver && Tunnel(tunnel.Direction, count);

        // Walking and holding go on for the count; the others only while there's more to try.
        var whileMore = command is not (WalkCommand or HoldCommand);
        var level = Level;
        var hp = Player.Hp;
        var seen = VisibleMonsters();
        var acted = false;
        _disturbed = false;
        for (var i = 0; i < count; i++)
        {
            _more = false;
            var at = Player.Position;
            if (!ExecuteOnce(command)) break;
            acted = true;
            if (IsGameOver || (whileMore && !_more)) break;
            if (Level != level || Player.Hp < hp || _disturbed || MonstersDisturb(seen)) break;
            // A walk that didn't move (a door opened, a monster attacked) stops there, as a run does.
            if (command is WalkCommand && Player.Position == at) break;
            seen = VisibleMonsters();
        }
        _disturbed = false;
        _more = false;
        return acted;
    }
}
