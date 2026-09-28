using Angband.Avalonia.Input;
using Angband.Avalonia.Controls;
using Angband.Avalonia.ViewModels;
using Angband.Input;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace Angband.Avalonia.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        // Tunnel so game keys work regardless of which element has focus (menus handle their own).
        AddHandler(KeyDownEvent, OnGameKeyDown, RoutingStrategies.Tunnel);
        if (this.FindControl<MapView>("Map") is { } map)
        {
            map.CellClicked += (loc, secondary) => (DataContext as MainWindowViewModel)?.ClickCell(loc, secondary);
            map.Zoom += delta =>
            {
                if (DataContext is not MainWindowViewModel vm) return;
                if (delta > 0) vm.ZoomIn();
                else vm.ZoomOut();
            };
        }
    }

    private MainWindowViewModel? _subscribed;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_subscribed is not null)
        {
            _subscribed.SettingsRequested -= OnSettingsRequested;
            _subscribed.OptionsRequested -= OnOptionsRequested;
            _subscribed.PropertyChanged -= OnViewModelPropertyChanged;
            _subscribed.NewCharacterRequested -= OnNewCharacterRequested;
            _subscribed.LoadRequested -= OnLoadRequested;
            _subscribed.CharacterSheetRequested -= OnCharacterSheetRequested;
            _subscribed.HighScoresRequested -= OnHighScoresRequested;
            _subscribed.KnowledgeRequested -= OnKnowledgeRequested;
            _subscribed.KeyCommandsRequested -= OnKeyCommandsRequested;
            _subscribed.MessageHistoryRequested -= OnMessageHistoryRequested;
            _subscribed.OverviewRequested -= OnOverviewRequested;
        }
        _subscribed = DataContext as MainWindowViewModel;
        if (_subscribed is not { } vm) return;
        vm.SettingsRequested += OnSettingsRequested;
        vm.OptionsRequested += OnOptionsRequested;
        vm.PropertyChanged += OnViewModelPropertyChanged;
        vm.NewCharacterRequested += OnNewCharacterRequested;
        vm.LoadRequested += OnLoadRequested;
        vm.CharacterSheetRequested += OnCharacterSheetRequested;
        vm.HighScoresRequested += OnHighScoresRequested;
        vm.KnowledgeRequested += OnKnowledgeRequested;
        vm.KeyCommandsRequested += OnKeyCommandsRequested;
        vm.MessageHistoryRequested += OnMessageHistoryRequested;
        vm.OverviewRequested += OnOverviewRequested;

        // "New game as" lists the classes from the game data.
        if (this.FindControl<MenuItem>("NewGameAsMenu") is { } menu)
        {
            menu.Items.Clear();
            foreach (var choice in vm.ClassChoices)
                menu.Items.Add(new MenuItem { Header = choice.Name, Command = vm.NewGameAsCommand, CommandParameter = choice.Id });
        }
    }

    private void OnSettingsRequested() => OpenSettings();

    private void OnOptionsRequested() => OpenSettings().ShowOptionsPage();

    /// <summary>The inscription box takes the keyboard while it is open, with the text selected.</summary>
    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainWindowViewModel.IsInscribing) || sender is not MainWindowViewModel { IsInscribing: true }) return;
        if (this.FindControl<TextBox>("InscriptionBox") is not { } box) return;
        Dispatcher.UIThread.Post(() =>
        {
            box.Focus();
            box.SelectAll();
        });
    }

    private void OnNewCharacterRequested() => OpenCharacterCreation();

    private void OnLoadRequested() => OpenLoadGame();

    private void OnCharacterSheetRequested(CharacterSheetViewModel sheet)
    {
        var window = new CharacterSheetWindow { DataContext = sheet };
        // While it is open its paper doll follows the equipment; once closed, it is let go.
        window.Closed += (_, _) => (DataContext as MainWindowViewModel)?.SheetClosed(sheet);
        DialogFit.Show(window, this);
    }

    private void OnKnowledgeRequested(KnowledgeViewModel knowledge) =>
        DialogFit.Show(new KnowledgeWindow { DataContext = knowledge }, this);

    private void OnKeyCommandsRequested(KeyCommandsViewModel commands) =>
        DialogFit.Show(new KeyCommandsWindow { DataContext = commands }, this);

    private void OnOverviewRequested(OverviewMapSource overview) =>
        DialogFit.Show(new OverviewMapWindow { DataContext = overview }, this);

    private void OnMessageHistoryRequested(MessageHistoryViewModel history) =>
        DialogFit.Show(new MessageHistoryWindow { DataContext = history }, this);

    private void OnHighScoresRequested(HighScoresViewModel scores) =>
        DialogFit.Show(new HighScoresWindow { DataContext = scores }, this);

    /// <summary>The dialog on top, if one is open (the active one, else the latest opened).</summary>
    public Window? OpenDialog =>
        OwnedWindows.LastOrDefault(w => w.IsVisible && w.IsActive && DialogKeys.IsDialog(w))
        ?? OwnedWindows.LastOrDefault(w => w.IsVisible && DialogKeys.IsDialog(w));

    /// <summary>
    /// A controller action. While a dialog is open it works the dialog — D-pad and A to move and
    /// press, B to back out (<see cref="DialogPad"/>) — and nothing reaches the game behind it;
    /// otherwise the game gets the action.
    /// </summary>
    public void HandleGamepadAction(InputAction action)
    {
        if (OpenDialog is { } dialog)
        {
            DialogPad.Handle(dialog, action);
            return;
        }
        (DataContext as MainWindowViewModel)?.HandleAction(action);
    }

    /// <summary>Opens the list of saved characters.</summary>
    public LoadGameWindow OpenLoadGame()
    {
        var window = new LoadGameWindow { DataContext = ((MainWindowViewModel)DataContext!).CreateLoadGame() };
        DialogFit.Show(window, this);
        return window;
    }

    /// <summary>Closing the window saves the game in progress, as Angband does on quit.</summary>
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        (DataContext as MainWindowViewModel)?.TrySave();
    }

    /// <summary>Opens the character creation screen; pressing Start begins a new game.</summary>
    public CharacterCreationWindow OpenCharacterCreation()
    {
        var window = new CharacterCreationWindow { DataContext = ((MainWindowViewModel)DataContext!).CreateCharacterCreation() };
        DialogFit.Show(window, this);
        return window;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        // First run: go straight to character creation, as Angband does.
        if (DataContext is MainWindowViewModel { IsFirstRun: true } vm && !Design.IsDesignMode && ShowCreationOnFirstRun)
            vm.RequestNewCharacter();
    }

    /// <summary>Tests turn this off so windows don't pop up.</summary>
    public static bool ShowCreationOnFirstRun { get; set; } = true;

    /// <summary>
    /// Keyboard provider: letters pick menu entries and answer y/n questions directly; every other key
    /// is looked up in the (rebindable) bindings and routed like any other device's action.
    /// </summary>
    private void OnGameKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm || KeyboardInput.IsModifierKey(e.Key)) return;
        var ctrlOrAlt = (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Alt)) != 0;

        // Writing an inscription: the text box has the keys; Enter writes it, Escape cancels.
        if (vm.IsInscribing)
        {
            if (e.Key == Key.Enter) { vm.CommitInscription(); e.Handled = true; }
            else if (e.Key == Key.Escape) { vm.CancelInscription(); e.Handled = true; }
            return;
        }

        // Typing a count after '0': digits, Backspace, Escape and Enter; any other key is the command.
        if (vm.IsEnteringCount && !ctrlOrAlt
            && vm.CountKey(KeyboardInput.Symbol(e), e.Key == Key.Back, e.Key == Key.Escape, e.Key == Key.Enter))
        {
            e.Handled = true;
            return;
        }

        // Banishment asks for a monster letter: any typed character (Escape cancels).
        if (vm.IsChoosingGlyph)
        {
            vm.ChooseGlyph(e.Key == Key.Escape ? "" : KeyboardInput.Symbol(e) ?? "");
            e.Handled = true;
            return;
        }

        // Look/target cursor: t, 5, space, +, -, p and r have their own meanings there.
        if (!ctrlOrAlt && vm.IsLooking && KeyboardInput.Symbol(e) is { Length: 1 } cursorKey && vm.CursorKey(cursorKey))
        {
            e.Handled = true;
            return;
        }
        // A direction prompt takes 't' (or ') as "toward the target".
        if (!ctrlOrAlt && vm.IsAwaitingDirection && KeyboardInput.Symbol(e) is "t" or "'" && vm.DirectionTowardTarget())
        {
            e.Handled = true;
            return;
        }

        if (!ctrlOrAlt && KeyboardInput.Symbol(e) is { Length: 1 } symbol && char.IsLetter(symbol[0]))
        {
            // In a store, a letter buys/sells one of that item; Shift+letter the whole stack
            // (unless a question is waiting: then y/n answer it).
            if (vm.IsInStore && !vm.IsConfirming && vm.StoreRows.Any(r => r.Letter == symbol.ToLowerInvariant()))
            {
                vm.StoreTransact(symbol[0], all: char.IsUpper(symbol[0]));
                e.Handled = true;
                return;
            }
            if (vm.IsConfirming && symbol is "y" or "Y" or "n" or "N")
            {
                vm.Confirm(symbol is "y" or "Y");
                e.Handled = true;
                return;
            }
            if (vm.IsPrompting && (vm.PromptRows.Any(r => r.Letter == symbol) || vm.SpellPromptRows.Any(r => r.Letter == symbol)
                                   || vm.ChoiceRows.Any(r => r.Letter == symbol)))
            {
                vm.PromptKey(symbol[0]);
                e.Handled = true;
                return;
            }
        }
        // A digit picks the item inscribed for it (@q1, @1).
        if (!ctrlOrAlt && KeyboardInput.Symbol(e) is { Length: 1 } digit && char.IsAsciiDigit(digit[0]) && vm.PromptHasTag(digit[0]))
        {
            vm.PromptKey(digit[0]);
            e.Handled = true;
            return;
        }

        var action = KeyboardInput.Resolve(e, vm.Bindings);
        if (action == InputAction.None)
        {
            // Any other key closes an open menu, as in Angband.
            if (vm.IsPrompting) { vm.CancelPrompt(); e.Handled = true; }
            else if (vm.IsShowingList) { vm.CloseList(); e.Handled = true; }
            return;
        }
        vm.HandleAction(action);
        e.Handled = true;
    }

    private void OnExit(object? sender, RoutedEventArgs e) => Close();

    private void OnOpenSettings(object? sender, RoutedEventArgs e) => OpenSettings();

    /// <summary>Opens the settings; changes apply live and are saved as they are made.</summary>
    public SettingsWindow OpenSettings()
    {
        var window = new SettingsWindow { DataContext = DataContext };
        DialogFit.Show(window, this);
        return window;
    }
}
