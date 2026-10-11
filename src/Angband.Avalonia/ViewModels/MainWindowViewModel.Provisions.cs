using Angband.Core.Game;
using Angband.Core.Items;
using Angband.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Angband.Avalonia.ViewModels;

// AVABand's own: safeguards against starving unawares. A food meter in the sidebar (coloured by how fed
// you are, pulsing once you're weak, with how many meals you carry); on growing weak, faint or starving,
// a question — eat your best food? — or, with the option on, eating it by yourself; resting, exploring
// and travelling won't start while you're weak until you've eaten (or, with nothing to eat, said so);
// warnings as the food runs out, and before going down from town with none; and a key that eats your
// best food without a menu. Every meal is an ordinary command, so replays play them back as they were.
public sealed partial class MainWindowViewModel
{
    [ObservableProperty] private string _foodText = "";
    [ObservableProperty] private string _foodSpoken = "";
    [ObservableProperty] private IBrush _foodBrush = Brushes.White;
    [ObservableProperty] private double _foodValue;
    [ObservableProperty] private double _foodMax = 1;
    /// <summary>Weak from hunger or worse: the meter pulses.</summary>
    [ObservableProperty] private bool _foodUrgent;

    /// <summary>The level hunger fell to during the last command (weak or worse), for asking afterwards.</summary>
    private HungerLevel? _hungerFell;
    private bool _hungerAnswered;
    /// <summary>The way down the player has said to take without food (kept until it's done, past other questions).</summary>
    private GameCommand? _noFoodAnswered;

    private static readonly IBrush Starving = new ImmutableSolidColorBrush(Color.FromUInt32(0xFFFF5050));
    private static readonly IBrush WeakBrush = new ImmutableSolidColorBrush(Color.FromUInt32(0xFFFF9933));
    private static readonly IBrush HungryBrush = new ImmutableSolidColorBrush(Color.FromUInt32(0xFFE6C84A));
    private static readonly IBrush FedBrush = new ImmutableSolidColorBrush(Color.FromUInt32(0xFF6CC070));
    private static readonly IBrush FullBrush = new ImmutableSolidColorBrush(Color.FromUInt32(0xFF5FA8E8));

    private void OnHungerChanged(HungerChangedEvent e)
    {
        if (e.Worse && e.Level <= HungerLevel.Weak) _hungerFell = e.Level;
    }

    /// <summary>The sidebar's food meter.</summary>
    private void RefreshFood()
    {
        var level = _game.HungerLevel;
        var c = _data.Constants;
        var meals = _game.Provisions;
        FoodMax = c.FoodFull;
        FoodValue = Math.Min(_game.Player.Food, c.FoodFull);
        FoodBrush = level switch
        {
            HungerLevel.Starving or HungerLevel.Faint => Starving,
            HungerLevel.Weak => WeakBrush,
            HungerLevel.Hungry => HungryBrush,
            HungerLevel.Fed => FedBrush,
            _ => FullBrush,
        };
        FoodUrgent = level <= HungerLevel.Weak;
        var state = level switch
        {
            HungerLevel.Starving => "Starving! Eat now!",
            HungerLevel.Faint => "Faint from hunger: eat now!",
            HungerLevel.Weak => "Weak from hunger: eat now",
            HungerLevel.Hungry => "Hungry: eat soon",
            _ => level.ToString(),
        };
        var supply = meals == 0 ? "no food!" : meals == 1 ? "1 meal" : $"{meals} meals";
        FoodText = $"Food: {state}  ·  {supply}";
        FoodSpoken = $"Food: {state}; carrying {(meals == 0 ? "no food" : supply)}.";
    }

    /// <summary>One of a stack, named ("a Ration of Food").</summary>
    private string OneOf(Item item) => ItemNaming.Describe(item.Clone(item.Serial, 1), _game.Knowledge);

    private static string HungerPhrase(HungerLevel level) => level switch
    {
        HungerLevel.Starving => "starving",
        HungerLevel.Faint => "faint from hunger",
        _ => "weak from hunger",
    };

    /// <summary>
    /// Resting, exploring or travelling while weak from hunger: eat first (then carry on), or, with nothing
    /// to eat, say you'll go on regardless. True if it asked.
    /// </summary>
    private bool AskAboutHunger(GameCommand command)
    {
        if (_hungerAnswered)
        {
            _hungerAnswered = false;
            return false;
        }
        var level = _game.HungerLevel;
        if (command is not (RestCommand or ExploreCommand or TravelCommand) || level > HungerLevel.Weak) return false;
        var what = command switch { RestCommand => "rest", ExploreCommand => "explore", _ => "travel" };
        if (_game.BestFood() is { } food)
        {
            AskYesNo($"You are {HungerPhrase(level)}. Eat {OneOf(food)} first?", yes =>
            {
                if (!yes)
                {
                    LastMessage = $"Not while you're {HungerPhrase(level)}: eat something first ({KeyName(InputAction.EatBest)} eats your best food).";
                    return;
                }
                Execute(new UseCommand(food));
                if (_game.Player.IsDead || IsConfirming) return;
                _hungerAnswered = true;
                Execute(command);
            });
        }
        else
        {
            AskYesNo($"You are {HungerPhrase(level)} and have nothing to eat. {char.ToUpperInvariant(what[0])}{what[1..]} anyway?", yes =>
            {
                if (!yes)
                {
                    LastMessage = "Cancelled.";
                    return;
                }
                _hungerAnswered = true;
                Execute(command);
            });
        }
        return true;
    }

    /// <summary>
    /// Leaving town for the dungeon without any food — down the stairs, or by Word of Recall (the scroll,
    /// a rod, a spell or an activation): ask first. True if it asked.
    /// </summary>
    private bool AskAboutNoFood(GameCommand command)
    {
        if (Equals(_noFoodAnswered, command) || _game.Player.Depth != 0 || _game.Provisions > 0) return false;
        var stairs = command is TakeStairsCommand { Down: true }
                     && _game.Level.Has(_game.Player.Position, Angband.Core.Definitions.TerrainFlags.DownStair);
        var recall = _game.StartsRecall(command);
        if (!stairs && !recall) return false;
        AskYesNo($"You're carrying no food (the General Store, 1, sells rations). {(recall ? "Recall to the dungeon" : "Go down")} anyway?", yes =>
        {
            if (!yes)
            {
                LastMessage = "Cancelled.";
                return;
            }
            _noFoodAnswered = command;
            Execute(command);
        });
        return true;
    }

    /// <summary>
    /// After a command: a warning as the food runs out, and if hunger fell to weak or worse, eating —
    /// by yourself with the option on, else asked.
    /// </summary>
    private void AfterHunger(int mealsBefore)
    {
        _hungerAnswered = false;
        _noFoodAnswered = null;
        if (_game.Player.IsDead)
        {
            _hungerFell = null;
            return;
        }
        var meals = _game.Provisions;
        if (IsInStore) mealsBefore = meals; // (selling or giving food away is no surprise)
        if (meals < mealsBefore && meals == 0) AddMessage("You have no food left. The General Store (1) sells rations.");
        else if (meals < mealsBefore && meals == 1) AddMessage("You have one meal left.");

        if (_hungerFell is not { } fell) return;
        _hungerFell = null;
        var level = _game.HungerLevel;
        if (level > HungerLevel.Weak) return;
        if (_game.BestFood() is not { } food)
        {
            AddMessage($"You are {HungerPhrase(level)} and have nothing to eat! Head for town, or find some food.");
            return;
        }
        if (OptionValue(DisplayOptions.AutoEat))
        {
            AddMessage($"You are {HungerPhrase(level)}, so you eat {OneOf(food)}.");
            Execute(new UseCommand(food));
            return;
        }
        if (IsConfirming || IsPrompting)
        {
            AddMessage($"You are {HungerPhrase(level)}: eat something ({KeyName(InputAction.EatBest)} eats your best food).");
            return;
        }
        AskYesNo($"You are {HungerPhrase(level)}. Eat {OneOf(food)}?", yes =>
        {
            if (yes) Execute(new UseCommand(food));
            else LastMessage = $"Eat soon: {KeyName(InputAction.EatBest)} eats your best food.";
        });
    }

    /// <summary>The one-key eat: your best food, no menu.</summary>
    private void EatBest()
    {
        if (_game.BestFood() is { } food)
        {
            Execute(new UseCommand(food));
            return;
        }
        AddMessage(_game.Provisions == 0 ? "You have nothing to eat."
            : _game.HungerLevel >= HungerLevel.Full ? "You are too full to eat any more."
            : "You have nothing you can eat just now.");
        Refresh();
    }
}
