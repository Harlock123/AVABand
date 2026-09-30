using Angband.Avalonia.Input;
using Angband.Avalonia.Controls;
using Angband.Avalonia.ViewModels;
using Angband.Input;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

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
            map.CellClicked += (loc, secondary) =>
            {
                if (DataContext is not MainWindowViewModel vm) return;
                vm.ClickCell(loc, secondary);
                if (secondary) AnchorMenuAtPointer(vm);
            };
            // Where the last press on the map was, for the right-click menu to open beside it.
            map.AddHandler(PointerPressedEvent, (_, e) => _mapPress = e.GetPosition(this.FindControl<Panel>("Overlay")),
                RoutingStrategies.Tunnel, handledEventsToo: true);
            map.CellHovered += loc => (DataContext as MainWindowViewModel)?.HoverCell(loc);
            map.Zoom += delta =>
            {
                if (DataContext is not MainWindowViewModel vm) return;
                if (delta > 0) vm.ZoomIn();
                else vm.ZoomOut();
            };
        }
        // Two arrow keys together move diagonally: presses wait a moment for a partner, releases count too.
        _arrowChord.Move += dir => (DataContext as MainWindowViewModel)?.HandleAction(InputActions.FromDirection(dir));
        _arrowTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(10), DispatcherPriority.Input, (_, _) =>
        {
            _arrowChord.Tick(_arrowClock.Elapsed);
            if (!_arrowChord.IsWaiting) _arrowTimer!.Stop();
        });
        AddHandler(KeyUpEvent, (_, e) =>
        {
            if (ArrowKeyDirection(e.Key) is { } arrow) _arrowChord.Up(arrow, _arrowClock.Elapsed);
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
        Deactivated += (_, _) => _arrowChord.Reset();
        // Shift held shows the travel route under the mouse: follow it from keys and pointer alike.
        AddHandler(KeyDownEvent, (_, e) => TrackShift(e.KeyModifiers, e.Key, down: true), RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(KeyUpEvent, (_, e) => TrackShift(e.KeyModifiers, e.Key, down: false), RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerMovedEvent, (_, e) => (DataContext as MainWindowViewModel)?.SetRouteKeyHeld((e.KeyModifiers & KeyModifiers.Shift) != 0),
            RoutingStrategies.Tunnel, handledEventsToo: true);
        Deactivated += (_, _) => (DataContext as MainWindowViewModel)?.SetRouteKeyHeld(false);

        // A scene ends by itself, or with a click on it (and the next, if any, follows).
        if (this.FindControl<SceneView>("SceneView") is { } scene)
        {
            scene.Finished += () => (DataContext as MainWindowViewModel)?.EndScene();
            scene.PointerPressed += (_, e) =>
            {
                (DataContext as MainWindowViewModel)?.EndScene();
                e.Handled = true;
            };
        }
        // A click on a prompt's line picks it, as its letter does (caught before the list selects it).
        foreach (var name in new[] { "PromptList", "SpellPromptList", "ChoiceList" })
            this.FindControl<ListBox>(name)?.AddHandler(PointerPressedEvent, OnPromptRowPressed, RoutingStrategies.Tunnel);
        // A press becomes a click on release, or a drag (onto the hotbar) once it moves.
        AddHandler(PointerMovedEvent, OnPendingMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, OnPendingReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(DragDrop.DragOverEvent, OnHotbarDragOver);
        AddHandler(DragDrop.DropEvent, OnHotbarDrop);
    }

    private void TrackShift(KeyModifiers modifiers, Key key, bool down)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        var held = key is Key.LeftShift or Key.RightShift ? down : (modifiers & KeyModifiers.Shift) != 0;
        vm.SetRouteKeyHeld(held);
    }

    /// <summary>"Open a replay file…": a file picker for .avareplay files.</summary>
    private async void OnReplayFileRequested()
    {
        if (DataContext is not MainWindowViewModel vm) return;
        var files = await StorageProvider.OpenFilePickerAsync(new global::Avalonia.Platform.Storage.FilePickerOpenOptions
        {
            Title = "Open a replay",
            AllowMultiple = false,
            FileTypeFilter = [new global::Avalonia.Platform.Storage.FilePickerFileType("AVABand replays") { Patterns = ["*.avareplay"] }],
        });
        if (files.Count > 0 && global::Avalonia.Platform.Storage.StorageProviderExtensions.TryGetLocalPath(files[0]) is { } path) vm.PlayReplay(path);
    }

    private global::Avalonia.Point? _mapPress;

    /// <summary>
    /// Angband opens its context menus where you click: the right-click menu (the prompt box) moves
    /// beside the pointer, kept inside the map, and goes back to the top of the map when it closes.
    /// </summary>
    private void AnchorMenuAtPointer(MainWindowViewModel vm)
    {
        if (!vm.IsPrompting || vm.MenuLabels.Count == 0 || _mapPress is not { } at
            || this.FindControl<Border>("PromptBox") is not { } box || this.FindControl<Panel>("Overlay") is not { } overlay) return;
        box.MinWidth = 0;
        box.HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Left;
        box.VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Top;
        box.Measure(global::Avalonia.Size.Infinity);
        var size = box.DesiredSize;
        const double gap = 14; // beside the square, not over it
        var x = at.X + gap + size.Width <= overlay.Bounds.Width ? at.X + gap : Math.Max(0, at.X - gap - size.Width);
        var y = Math.Clamp(at.Y - 10, 0, Math.Max(0, overlay.Bounds.Height - size.Height));
        box.Margin = new global::Avalonia.Thickness(x, y, 0, 0);
        vm.PropertyChanged -= ResetMenuWhenClosed;
        vm.PropertyChanged += ResetMenuWhenClosed;
    }

    private void ResetMenuWhenClosed(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainWindowViewModel.IsPrompting) || sender is not MainWindowViewModel vm) return;
        // Checked a moment later: a menu that opens another (Other, or "Use item on" then the list) stays put.
        Dispatcher.UIThread.Post(() =>
        {
            if (vm.IsPrompting || this.FindControl<Border>("PromptBox") is not { } box) return;
            vm.PropertyChanged -= ResetMenuWhenClosed;
            box.MinWidth = 420;
            box.HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Center;
            box.VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Top;
            box.Margin = new global::Avalonia.Thickness(0, 24, 0, 0);
        });
    }

    /// <summary>Hotbar drags carry "item:serial", "spell:id" or "slot:n" (in this process only).</summary>
    public static readonly DataFormat<string> HotbarFormat = DataFormat.CreateStringApplicationFormat("avaband-hotbar");

    /// <summary>How far (pixels) a press must move to become a drag.</summary>
    private const double DragThreshold = 6;

    private (global::Avalonia.Point Start, string? Payload, Action? Click, PointerPressedEventArgs Press)? _pending;

    private void BeginPending(PointerPressedEventArgs e, string? payload, Action? click) =>
        _pending = (e.GetPosition(this), payload, click, e);

    private async void OnPendingMoved(object? sender, PointerEventArgs e)
    {
        if (_pending is not { Payload: { } payload } pending) return;
        var at = e.GetPosition(this);
        if (Math.Abs(at.X - pending.Start.X) < DragThreshold && Math.Abs(at.Y - pending.Start.Y) < DragThreshold) return;
        _pending = null;
        var data = new DataTransfer();
        data.Add(DataTransferItem.Create(HotbarFormat, payload));
        await DragDrop.DoDragDropAsync(pending.Press, data, DragDropEffects.Copy | DragDropEffects.Move); // (a drag begins from its press)
    }

    private void OnPendingReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_pending is not { } pending) return;
        _pending = null;
        pending.Click?.Invoke();
    }

    private static HotbarSlotRow? HotbarSlotAt(object? source) =>
        (source as global::Avalonia.Visual)?.GetSelfAndVisualAncestors().OfType<Control>()
            .Select(c => c.DataContext).OfType<HotbarSlotRow>().FirstOrDefault();

    private void OnHotbarDragOver(object? sender, DragEventArgs e)
    {
        var ours = e.DataTransfer.TryGetValue(HotbarFormat) is not null && HotbarSlotAt(e.Source) is not null;
        e.DragEffects = ours ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private void OnHotbarDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm || HotbarSlotAt(e.Source) is not { } slot
            || e.DataTransfer.TryGetValue(HotbarFormat) is not { } payload) return;
        vm.DropOnHotbar(slot.Index, payload);
        e.Handled = true;
    }

    /// <summary>A click on a title screen choice takes it (the click has already selected it).</summary>
    private void OnTitleTapped(object? sender, global::Avalonia.Input.TappedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm && e.Source is global::Avalonia.Visual v
            && v.FindAncestorOfType<ListBoxItem>(includeSelf: true) is { DataContext: TitleChoice choice })
            choice.Act();
    }

    /// <summary>An item row in the sidebar: pressing and moving drags it onto the hotbar; right-click opens its menu.</summary>
    private void OnItemRowPressed(object? sender, PointerPressedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not ItemRow row) return;
        var point = e.GetCurrentPoint(this).Properties;
        if (point.IsRightButtonPressed && DataContext is MainWindowViewModel vm)
        {
            vm.OpenItemMenu(row.Item);
            e.Handled = true;
        }
        else if (point.IsLeftButtonPressed) BeginPending(e, MainWindowViewModel.DragPayload(row.Item), null);
    }

    /// <summary>A hotbar slot: left-click uses it (or fills an empty one), right-click changes it.</summary>
    private void OnHotbarPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm || (sender as Control)?.DataContext is not HotbarSlotRow slot) return;
        e.Handled = true;
        if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed) vm.OpenHotbarMenu(slot.Index);
        else BeginPending(e, vm.Game.Hotbar[slot.Index] is null ? null : MainWindowViewModel.DragPayload(slot.Index), () => vm.UseHotbar(slot.Index));
    }

    private void OnPromptRowPressed(object? sender, PointerPressedEventArgs e)
    {
        e.Handled = true;
        if (DataContext is not MainWindowViewModel vm) return;
        var row = (e.Source as global::Avalonia.Visual)?.FindAncestorOfType<ListBoxItem>(includeSelf: true)?.DataContext;
        var letter = row switch { ItemRow r => r.Letter, SpellRow s => s.Letter, ChoiceRow c => c.Letter, _ => null };
        var payload = row switch { ItemRow r => MainWindowViewModel.DragPayload(r.Item), SpellRow s => MainWindowViewModel.DragPayload(s.Spell), _ => null };
        if (letter is { Length: > 0 }) BeginPending(e, payload, () => vm.PromptKey(letter[0])); // picked on release
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
            _subscribed.ReplayFileRequested -= OnReplayFileRequested;
            _subscribed.CharacterSheetRequested -= OnCharacterSheetRequested;
            _subscribed.HighScoresRequested -= OnHighScoresRequested;
            _subscribed.KnowledgeRequested -= OnKnowledgeRequested;
            _subscribed.KeyCommandsRequested -= OnKeyCommandsRequested;
            _subscribed.HelpRequested -= OnHelpRequested;
            _subscribed.GameOverMenuRequested -= OnGameOverMenuRequested;
            _subscribed.ExitRequested -= OnExitRequested;
            _subscribed.MessageHistoryRequested -= OnMessageHistoryRequested;
            _subscribed.JourneyRequested -= OnJourneyRequested;
            _subscribed.OverviewRequested -= OnOverviewRequested;
            _subscribed.OpenUrlRequested -= OnOpenUrlRequested;
        }
        _subscribed = DataContext as MainWindowViewModel;
        if (_subscribed is { } viewModel) viewModel.ViewportCells = () => Map.VisibleCells;
        if (_subscribed is not { } vm) return;
        vm.SettingsRequested += OnSettingsRequested;
        vm.OptionsRequested += OnOptionsRequested;
        vm.PropertyChanged += OnViewModelPropertyChanged;
        vm.NewCharacterRequested += OnNewCharacterRequested;
        vm.LoadRequested += OnLoadRequested;
        vm.ReplayFileRequested += OnReplayFileRequested;
        vm.CharacterSheetRequested += OnCharacterSheetRequested;
        vm.HighScoresRequested += OnHighScoresRequested;
        vm.KnowledgeRequested += OnKnowledgeRequested;
        vm.KeyCommandsRequested += OnKeyCommandsRequested;
        vm.HelpRequested += OnHelpRequested;
        vm.GameOverMenuRequested += OnGameOverMenuRequested;
        vm.ExitRequested += OnExitRequested;
        vm.MessageHistoryRequested += OnMessageHistoryRequested;
        vm.JourneyRequested += OnJourneyRequested;
        vm.OverviewRequested += OnOverviewRequested;
        vm.OpenUrlRequested += OnOpenUrlRequested;

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

    private void OnOpenUrlRequested(string url) => _ = Launcher.LaunchUriAsync(new Uri(url));

    private void OnOverviewRequested(OverviewMapSource overview) =>
        DialogFit.Show(new OverviewMapWindow { DataContext = overview }, this);

    private void OnMessageHistoryRequested(MessageHistoryViewModel history) =>
        DialogFit.Show(new MessageHistoryWindow { DataContext = history }, this);

    private void OnJourneyRequested(JourneyViewModel journey) =>
        DialogFit.Show(new JourneyWindow { DataContext = journey }, this);

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
        if (DataContext is not MainWindowViewModel vm) return;
        vm.TrySave();
        if (!e.Cancel) vm.ClosedCleanly();
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
        // The title screen, whose choices cover a first run, a crash and a dead character too.
        if (DataContext is MainWindowViewModel titled && !Design.IsDesignMode && ShowCreationOnFirstRun && ShowTitleAtStart)
        {
            titled.ShowTitle();
            return;
        }
        // First run: go straight to character creation, as Angband does.
        if (DataContext is MainWindowViewModel { IsFirstRun: true } vm && !Design.IsDesignMode && ShowCreationOnFirstRun)
            vm.RequestNewCharacter();
        // The last session crashed: offer to resume from its latest save.
        else if (DataContext is MainWindowViewModel { ShouldOfferResume: true } crashed && !Design.IsDesignMode)
            crashed.OfferResume();
        // The last character died: ask what next, rather than just starting over in town.
        else if (DataContext is MainWindowViewModel { ShouldOfferStartMenu: true } again && !Design.IsDesignMode && ShowCreationOnFirstRun)
            again.ShowStartMenu();
    }

    private readonly ArrowChord _arrowChord = new();
    private readonly DispatcherTimer _arrowTimer;
    private readonly System.Diagnostics.Stopwatch _arrowClock = System.Diagnostics.Stopwatch.StartNew();

    private static Angband.Core.Geometry.Direction? ArrowKeyDirection(Key key) => key switch
    {
        Key.Up => Angband.Core.Geometry.Direction.North,
        Key.Down => Angband.Core.Geometry.Direction.South,
        Key.Left => Angband.Core.Geometry.Direction.West,
        Key.Right => Angband.Core.Geometry.Direction.East,
        _ => null,
    };

    /// <summary>Tests turn this off so windows don't pop up.</summary>
    public static bool ShowCreationOnFirstRun { get; set; } = true;

    /// <summary>Whether the title screen opens with the window (not with --seed, --depth or --no-title).</summary>
    public bool ShowTitleAtStart { get; set; } = true;

    /// <summary>
    /// Keyboard provider: letters pick menu entries and answer y/n questions directly; every other key
    /// is looked up in the (rebindable) bindings and routed like any other device's action.
    /// </summary>
    private void OnGameKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm || KeyboardInput.IsModifierKey(e.Key)) return;
        if (vm.SceneWaits)
        {
            // Held until Space: Space or Enter moves on, Escape ends them all, other keys wait.
            vm.HandleWaitingScene(e.Key is Key.Space or Key.Enter ? InputAction.Confirm
                : e.Key == Key.Escape ? InputAction.Cancel : InputAction.None);
            e.Handled = true;
            return;
        }
        if (vm.HasScene) vm.SkipScenes(); // a key cuts the scenes short, and still does what it does

        // Two arrow keys held together move diagonally (plain arrows, at the command prompt, as bound by default).
        if (e.KeyModifiers == KeyModifiers.None && ArrowKeyDirection(e.Key) is { } arrow && !_replaying && vm.IsAtCommandPrompt && !vm.IsShowingTitle
            && !vm.IsEnteringCount && vm.OptionValue(DisplayOptions.ArrowDiagonals)
            && vm.Bindings.ForKey(e.Key.ToString()) == InputActions.FromDirection(arrow))
        {
            _arrowChord.Down(arrow, _arrowClock.Elapsed);
            if (_arrowChord.IsWaiting) _arrowTimer.Start();
            e.Handled = true;
            return;
        }

        // The title screen: a letter picks, arrows move, Enter takes.
        if (vm.IsShowingTitle)
        {
            e.Handled = true;
            if (e.Key is Key.Up or Key.NumPad8) vm.MoveTitleSelection(-1);
            else if (e.Key is Key.Down or Key.NumPad2) vm.MoveTitleSelection(+1);
            else if (e.Key is Key.Enter or Key.Space) vm.ChooseSelectedTitle();
            else if (KeyboardInput.Symbol(e) is { Length: 1 } s && char.IsLetter(s[0])) vm.TitleKey(s[0]);
            return;
        }
        var ctrlOrAlt = (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Alt)) != 0;

        // The hotbar: Alt+1 .. Alt+0 use its slots; with Shift, change them.
        if (!vm.IsInscribing && (e.KeyModifiers & (KeyModifiers.Alt | KeyModifiers.Control)) == KeyModifiers.Alt
            && e.Key is >= Key.D0 and <= Key.D9)
        {
            var slot = (e.Key - Key.D0 + 9) % 10; // 1 is the first slot, 0 the tenth
            if ((e.KeyModifiers & KeyModifiers.Shift) != 0) vm.OpenHotbarMenu(slot);
            else vm.UseHotbar(slot);
            e.Handled = true;
            return;
        }

        // Writing an inscription: the text box has the keys; Enter writes it, Escape cancels.
        if (vm.IsInscribing)
        {
            if (e.Key == Key.Enter) { vm.CommitInscription(); e.Handled = true; }
            else if (e.Key == Key.Escape) { vm.CancelInscription(); e.Handled = true; }
            return;
        }

        // A number asked for (the level to recall to): digits, Backspace, Enter, Escape.
        if (vm.IsEnteringNumber)
        {
            vm.NumberKey(KeyboardInput.Symbol(e), e.Key == Key.Back, e.Key == Key.Escape, e.Key == Key.Enter);
            e.Handled = true;
            return;
        }

        // Typing a count after '0': digits, Backspace, Escape and Enter; any other key is the command.
        if (vm.IsEnteringCount && !ctrlOrAlt
            && vm.CountKey(KeyboardInput.Symbol(e), e.Key == Key.Back, e.Key == Key.Escape, e.Key == Key.Enter))
        {
            e.Handled = true;
            return;
        }

        // A keymap (Angband): at the command prompt, a key that types others. What it types isn't
        // itself keymapped.
        if (!_replaying && vm.IsAtCommandPrompt && vm.Bindings.KeymapFor(KeyboardInput.Chords(e)) is { } keys)
        {
            e.Handled = true;
            Replay(keys);
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

    private bool _replaying;

    /// <summary>Types a keymap's keys, one after another, as if pressed.</summary>
    private void Replay(string keys)
    {
        _replaying = true;
        try
        {
            foreach (var stroke in Angband.Input.KeymapText.Parse(keys)) OnGameKeyDown(this, ToKeyEvent(stroke));
        }
        finally
        {
            _replaying = false;
        }
    }

    /// <summary>A keystroke as the key event it stands for.</summary>
    private static KeyEventArgs ToKeyEvent(Angband.Input.KeyStroke stroke)
    {
        var key = Key.None;
        string? symbol = null;
        if (stroke.KeyName is { } name)
        {
            if (!Enum.TryParse(name, ignoreCase: true, out key)) key = Key.None;
        }
        else if (stroke.Character is { } c)
        {
            symbol = c.ToString();
            key = char.IsAsciiLetter(c) ? Key.A + (char.ToUpperInvariant(c) - 'A')
                : char.IsAsciiDigit(c) ? Key.D0 + (c - '0')
                : c == ' ' ? Key.Space : Key.None;
        }
        return new KeyEventArgs
        {
            RoutedEvent = KeyDownEvent,
            Key = key,
            KeySymbol = stroke.Ctrl ? null : symbol,
            KeyModifiers = stroke.Ctrl ? KeyModifiers.Control : KeyModifiers.None,
        };
    }

    private void OnHelpRequested(HelpViewModel help) => DialogFit.Show(new HelpWindow { DataContext = help }, this);

    private void OnGameOverMenuRequested(GameOverMenuViewModel menu) => DialogFit.Show(new GameOverWindow { DataContext = menu }, this);

    private void OnExitRequested() => Close();

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
