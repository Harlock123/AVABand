using Angband.Core.Definitions;
using Angband.Core.Game;
using Angband.Core.Geometry;
using Angband.Input;

namespace Angband.Tests;

public class InputTests
{
    private static readonly TimeSpan T0 = TimeSpan.Zero;

    private static (GamepadMapper Mapper, List<InputAction> Actions) Pad()
    {
        var mapper = new GamepadMapper(InputBindings.Defaults());
        var actions = new List<InputAction>();
        mapper.ActionTriggered += actions.Add;
        return (mapper, actions);
    }

    // --- Keysets ------------------------------------------------------------------------------

    [Theory]
    [InlineData("Char:l", InputAction.Look)]
    [InlineData("Char:t", InputAction.TakeOff)]
    [InlineData("Char:T", InputAction.Tunnel)]
    [InlineData("Char:k", InputAction.Ignore)]
    [InlineData("Char:z", InputAction.ZapRod)]
    [InlineData("Char:a", InputAction.AimWand)]
    [InlineData("Char:u", InputAction.UseStaff)]
    [InlineData("Char:b", InputAction.Browse)]
    [InlineData("Char:n", InputAction.RepeatCommand)]
    [InlineData("Char:,", InputAction.Hold)]
    [InlineData("Char:.", InputAction.Run)]
    [InlineData("Up", InputAction.MoveNorth)]
    [InlineData("Char:L", InputAction.Locate)]
    [InlineData("Char:W", InputAction.WalkIntoTrap)]
    [InlineData("Char:U", InputAction.UseItem)]
    [InlineData("Char:h", InputAction.FireNearest)]
    [InlineData("Char:/", InputAction.IdentifySymbol)]
    public void TheOriginalKeyset_IsAngbands(string chord, InputAction action) =>
        Assert.Equal(action, InputBindings.Preset(InputBindings.Keyset.Original).ForKey(chord));

    [Theory]
    [InlineData("Char:h", InputAction.MoveWest)]
    [InlineData("Char:y", InputAction.MoveNorthWest)]
    [InlineData("Char:L", InputAction.RunEast)]
    [InlineData("Char:x", InputAction.Look)]
    [InlineData("Char:T", InputAction.TakeOff)]
    [InlineData("Ctrl+T", InputAction.Tunnel)]
    [InlineData("Ctrl+Shift+T", InputAction.ToggleTiles)]
    [InlineData("Char:t", InputAction.Fire)]
    [InlineData("Char:a", InputAction.ZapRod)]
    [InlineData("Char:z", InputAction.AimWand)]
    [InlineData("Char:Z", InputAction.UseStaff)]
    [InlineData("Char:P", InputAction.Browse)]
    [InlineData("Char:O", InputAction.ToggleIgnore)]
    [InlineData("Char:.", InputAction.Hold)]
    [InlineData("Char:,", InputAction.Run)]
    [InlineData("Ctrl+V", InputAction.RepeatCommand)]
    [InlineData("Char:W", InputAction.Locate)]
    [InlineData("Char:-", InputAction.WalkIntoTrap)]
    [InlineData("Char:X", InputAction.UseItem)]
    public void TheRoguelikeKeyset_IsAngbands(string chord, InputAction action) =>
        Assert.Equal(action, InputBindings.Preset(InputBindings.Keyset.Roguelike).ForKey(chord));

    [Fact]
    public void EveryKeyset_LeavesNoActionWithoutAKey_ThatHadOne()
    {
        var had = InputBindings.Defaults().Keys.Values.ToHashSet();
        foreach (var keyset in Enum.GetValues<InputBindings.Keyset>())
        {
            var bound = InputBindings.Preset(keyset).Keys.Values.ToHashSet();
            Assert.Empty(had.Except(bound));
        }
    }

    // --- Keymaps ------------------------------------------------------------------------------

    [Fact]
    public void KeymapText_IsWrittenAsAngbandWritesIt()
    {
        Assert.Equal([new KeyStroke(null, 'm'), new KeyStroke(null, 'a'), new KeyStroke(null, 'a'), new KeyStroke(null, '\'')],
            KeymapText.Parse("maa'"));
        Assert.Equal([new KeyStroke(null, 't', Ctrl: true)], KeymapText.Parse("^T"));
        Assert.Equal([new KeyStroke("Escape", null), new KeyStroke("Enter", null), new KeyStroke(null, '\\')],
            KeymapText.Parse("\\e\\n\\\\"));
        Assert.Equal([new KeyStroke("F1", null), new KeyStroke(null, 'q')], KeymapText.Parse("[F1]q"));
    }

    [Fact]
    public void Keymaps_AreSaved_WithTheBindings()
    {
        var path = Path.Combine(Directory.CreateTempSubdirectory("avaband-keys-").FullName, "bindings.json");
        var b = InputBindings.Defaults();
        b.Keymaps["F2"] = "maa'";
        b.Save(path);
        var loaded = InputBindings.Load(path);
        Assert.Equal("maa'", loaded.KeymapFor(["F2"]));
        Assert.Null(loaded.KeymapFor(["F3"]));
    }

    [Fact]
    public void OldSavedBindings_GetTheHelpOnQuestionMark_UnlessSomeoneChoseOtherwise()
    {
        var dir = Directory.CreateTempSubdirectory("avaband-keys-").FullName;
        var old = InputBindings.Defaults();
        old.Keys["Char:?"] = InputAction.ShowCommands; // as saved before the help existed
        old.Save(Path.Combine(dir, "old.json"));
        var loaded = InputBindings.Load(Path.Combine(dir, "old.json"));
        Assert.Equal(InputAction.Help, loaded.ForKey("Char:?"));
        Assert.Equal(InputAction.ShowCommands, loaded.ForKey("F1"));

        // Someone who moved the command list off F1 chose '?' for it: leave it be.
        var chosen = InputBindings.Defaults();
        chosen.Keys["Char:?"] = InputAction.ShowCommands;
        chosen.Keys.Remove("F1");
        chosen.Save(Path.Combine(dir, "chosen.json"));
        Assert.Equal(InputAction.ShowCommands, InputBindings.Load(Path.Combine(dir, "chosen.json")).ForKey("Char:?"));
    }

    // --- Gamepad mapping ----------------------------------------------------------------------

    [Fact]
    public void Buttons_TriggerTheirBoundActionOncePerPress()
    {
        var (pad, actions) = Pad();
        pad.ButtonDown("A", T0);
        pad.ButtonDown("A", T0); // still held: no repeat for buttons
        pad.ButtonUp("A", T0);
        pad.ButtonDown("X", T0);
        Assert.Equal([InputAction.Confirm, InputAction.Fire], actions);
    }

    [Fact]
    public void DPad_MovesAndRepeatsWhileHeld()
    {
        var (pad, actions) = Pad();
        pad.ButtonDown("DPadUp", T0);
        Assert.Equal([InputAction.MoveNorth], actions);

        pad.Tick(TimeSpan.FromMilliseconds(200));                   // before the repeat delay
        Assert.Single(actions);
        pad.Tick(GamepadMapper.RepeatDelay);                        // first repeat
        pad.Tick(GamepadMapper.RepeatDelay + GamepadMapper.RepeatInterval); // second
        Assert.Equal(3, actions.Count);

        pad.ButtonUp("DPadUp", TimeSpan.FromSeconds(1));
        pad.Tick(TimeSpan.FromSeconds(5));
        Assert.Equal(3, actions.Count);
    }

    [Fact]
    public void TwoDPadButtons_GiveADiagonal()
    {
        var (pad, actions) = Pad();
        pad.ButtonDown("DPadUp", T0);
        pad.ButtonDown("DPadRight", T0);
        Assert.Equal([InputAction.MoveNorth, InputAction.MoveNorthEast], actions);
    }

    [Theory]
    [InlineData(1.0, 0.0, InputAction.MoveEast)]
    [InlineData(0.7, 0.7, InputAction.MoveSouthEast)]
    [InlineData(0.0, 1.0, InputAction.MoveSouth)]
    [InlineData(-0.7, 0.7, InputAction.MoveSouthWest)]
    [InlineData(-1.0, 0.0, InputAction.MoveWest)]
    [InlineData(-0.7, -0.7, InputAction.MoveNorthWest)]
    [InlineData(0.0, -1.0, InputAction.MoveNorth)]
    [InlineData(0.7, -0.7, InputAction.MoveNorthEast)]
    public void Stick_SnapsToEightDirections(double x, double y, InputAction expected)
    {
        var (pad, actions) = Pad();
        pad.LeftStick(x, y, T0);
        Assert.Equal([expected], actions);
    }

    [Fact]
    public void Stick_HasADeadZone()
    {
        var (pad, actions) = Pad();
        pad.LeftStick(0.3, 0.2, T0);
        Assert.Empty(actions);
    }

    [Fact]
    public void Triggers_PressPastTheThreshold_WithHysteresis()
    {
        var (pad, actions) = Pad();
        pad.Trigger("LeftTrigger", 0.4, T0);
        Assert.Empty(actions);
        pad.Trigger("RightTrigger", 0.9, T0);
        pad.Trigger("RightTrigger", 0.45, T0); // not released yet (hysteresis)
        pad.Trigger("RightTrigger", 0.9, T0);
        Assert.Equal([InputAction.Wield], actions);
        pad.Trigger("RightTrigger", 0.0, T0);
        pad.Trigger("RightTrigger", 0.9, T0);
        Assert.Equal([InputAction.Wield, InputAction.Wield], actions);
    }

    [Fact]
    public void TheLeftTrigger_TappedAlone_StillThrows_OnRelease()
    {
        var (pad, actions) = Pad();
        pad.Trigger("LeftTrigger", 0.9, T0);
        Assert.Empty(actions); // it might be the start of a chord
        pad.Trigger("LeftTrigger", 0.0, T0);
        Assert.Equal([InputAction.Throw], actions);
    }

    [Fact]
    public void LeftTrigger_WithADirection_Runs_Once()
    {
        var (pad, actions) = Pad();
        var grace = GamepadMapper.RunGrace;
        pad.Trigger("LeftTrigger", 0.9, T0);
        pad.ButtonDown("DPadLeft", T0);
        Assert.Empty(actions);                 // a moment's grace for a second button
        pad.Tick(grace);
        pad.Tick(TimeSpan.FromSeconds(2));     // no hold-to-repeat for a run
        pad.ButtonUp("DPadLeft", TimeSpan.FromSeconds(2));
        var t = TimeSpan.FromSeconds(3);
        pad.LeftStick(0.7, 0.7, t);            // the stick runs too
        pad.Tick(t + grace);
        pad.LeftStick(0, 0, t + grace);
        pad.Trigger("LeftTrigger", 0.0, t + grace);
        Assert.Equal([InputAction.RunWest, InputAction.RunSouthEast], actions); // and no throw
    }

    [Fact]
    public void TwoDPadButtons_AMomentApart_RunDiagonally()
    {
        var (pad, actions) = Pad();
        pad.Trigger("LeftTrigger", 0.9, T0);
        pad.ButtonDown("DPadUp", T0);
        pad.Tick(TimeSpan.FromMilliseconds(30));
        pad.ButtonDown("DPadRight", TimeSpan.FromMilliseconds(40));
        pad.Tick(GamepadMapper.RunGrace);
        pad.Tick(TimeSpan.FromSeconds(1));
        Assert.Equal([InputAction.RunNorthEast], actions);
    }

    [Fact]
    public void ADirectionLetGoWithinTheGrace_DoesNotRun()
    {
        var (pad, actions) = Pad();
        pad.Trigger("LeftTrigger", 0.9, T0);
        pad.ButtonDown("DPadUp", T0);
        pad.ButtonUp("DPadUp", TimeSpan.FromMilliseconds(20));
        pad.Tick(TimeSpan.FromSeconds(1));
        Assert.Empty(actions);
    }

    [Fact]
    public void LeftTrigger_WithA_RepeatsTheLastCommand()
    {
        var (pad, actions) = Pad();
        pad.Trigger("LeftTrigger", 0.9, T0);
        pad.ButtonDown("A", T0);
        pad.ButtonUp("A", T0);
        pad.ButtonDown("A", T0);
        pad.Trigger("LeftTrigger", 0.0, T0);
        pad.ButtonUp("A", T0);
        pad.ButtonDown("A", T0); // the trigger let go: A confirms again
        Assert.Equal([InputAction.RepeatCommand, InputAction.RepeatCommand, InputAction.Confirm], actions);
    }

    [Fact]
    public void SavedBindings_WithoutTheShortcuts_GetThem()
    {
        var old = InputBindings.Defaults();
        old.Buttons.Remove("LeftTrigger+DPad");
        old.Buttons.Remove("LeftTrigger+A");
        old.AddMissingDefaults();
        Assert.Equal(InputAction.Run, old.ForButton("LeftTrigger+DPad"));
        Assert.Equal(InputAction.RepeatCommand, old.ForButton("LeftTrigger+A"));
    }

    [Fact]
    public void CaptureNextButton_ReportsInsteadOfActing()
    {
        var (pad, actions) = Pad();
        string? captured = null;
        pad.CaptureNextButton = b => captured = b;
        pad.ButtonDown("Y", T0);
        Assert.Equal("Y", captured);
        Assert.Empty(actions);
        pad.ButtonUp("Y", T0);
        pad.ButtonDown("Y", T0);
        Assert.Equal([InputAction.Cast], actions);
    }

    // --- Bindings -----------------------------------------------------------------------------

    [Fact]
    public void Defaults_BindEveryActionToAKey()
    {
        var b = InputBindings.Defaults();
        var unbound = Enum.GetValues<InputAction>().Where(a => a != InputAction.None && !b.KeysFor(a).Any()).ToList();
        Assert.Empty(unbound);
    }

    [Fact]
    public void Rebinding_TakesChordsAndButtonsOver()
    {
        var b = InputBindings.Defaults();
        b.BindKey("Char:f", InputAction.Quaff);
        Assert.Equal(InputAction.Quaff, b.ForKey("Char:f"));
        Assert.DoesNotContain("Char:f", b.KeysFor(InputAction.Fire));

        b.BindButton("Y", InputAction.Fire);   // Fire had X; a pad action keeps one button
        Assert.Equal(["Y"], b.ButtonsFor(InputAction.Fire));
        Assert.Equal(InputAction.None, b.ForButton("X"));
    }

    [Fact]
    public void Bindings_SaveAndLoad()
    {
        var path = Path.Combine(Directory.CreateTempSubdirectory("avaband-bind-").FullName, "bindings.json");
        var b = InputBindings.Defaults();
        b.BindKey("F2", InputAction.Rest);
        b.Save(path);

        var loaded = InputBindings.Load(path);
        Assert.Equal(InputAction.Rest, loaded.ForKey("F2"));
        Assert.Equal(b.Keys.Count, loaded.Keys.Count);
        Assert.Equal(InputAction.Confirm, loaded.ForButton("A"));
    }

    [Fact]
    public void Sdl_StartsOrDeclinesGracefully()
    {
        using var pad = SdlGamepadProvider.TryCreate(InputBindings.Defaults());
        if (pad is null) return; // no SDL on this machine: gamepads are simply unavailable
        pad.Poll();
        Assert.False(string.IsNullOrEmpty(pad.Status));
    }

    // --- Travel (mouse clicks) ------------------------------------------------------------------

    private static readonly string[] Maze =
    [
        "############",
        "#@.......#.#",
        "########.#.#",
        "#........+.#",
        "############",
    ];

    private static GameSession Known(params string[] rows)
    {
        var game = Arena.Create(1, rows);
        game.Known.RememberAll(game.Level);
        return game;
    }

    [Fact]
    public void Travel_FollowsKnownCorridors_AndOpensDoors()
    {
        var game = Known(Maze);
        Assert.True(game.Execute(new TravelCommand(new Loc(10, 1))));
        Assert.Equal(new Loc(10, 1), game.Player.Position);
        Assert.True(game.Level.Has(new Loc(9, 3), TerrainFlags.Passable)); // the door was opened on the way
    }

    [Fact]
    public void Travel_NeedsAKnownRoute()
    {
        var game = Arena.Create(1, Maze); // nothing remembered beyond what's in view
        var messages = new List<string>();
        game.Events.Subscribe<MessageEvent>(m => messages.Add(m.Text));
        Assert.False(game.Execute(new TravelCommand(new Loc(10, 3))));
        Assert.Contains("You don't know a way there.", messages);
    }

    [Fact]
    public void Travel_StopsWhenAMonsterAppears()
    {
        var game = Known(Maze);
        Arena.AddMonster(game, "grey_mold", new Loc(7, 3)).Hp = 10_000; // lit by the torch as the player passes
        game.Execute(new TravelCommand(new Loc(10, 1)));
        Assert.NotEqual(new Loc(10, 1), game.Player.Position);
    }

    [Fact]
    public void Travel_AvoidsVisibleTraps()
    {
        var game = Known(
            "#######",
            "#@....#",
            "#.....#",
            "#######");
        game.Level[new Loc(2, 1)].Trap = 1;
        game.Level[new Loc(2, 1)].Flags |= Angband.Core.World.SquareFlags.TrapVisible;
        var path = game.FindPath(game.Player.Position, new Loc(5, 1))!;
        Assert.DoesNotContain(new Loc(2, 1), path);
        Assert.Equal(new Loc(5, 1), path[^1]);
    }

    [Fact]
    public void Saved_bindings_gain_new_actions_without_losing_changes()
    {
        var path = Path.Combine(Path.GetTempPath(), "avaband-bindings-" + Guid.NewGuid() + ".json");
        try
        {
            var old = InputBindings.Defaults();
            foreach (var key in old.KeysFor(InputAction.SaveGame).ToList()) old.Keys.Remove(key);
            old.Keys.Remove("Ctrl+H");
            old.Keys["Ctrl+H"] = InputAction.Rest; // the player's own choice
            old.Save(path);

            var loaded = InputBindings.Load(path);
            Assert.Equal(InputAction.SaveGame, loaded.Keys["Ctrl+S"]);
            Assert.Equal(InputAction.Rest, loaded.Keys["Ctrl+H"]);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
