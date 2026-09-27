using Angband.Input;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Angband.Avalonia.Views;

/// <summary>
/// The controller in a dialog: the D-pad moves between controls (and works lists, sliders, tabs and
/// drop-downs), A presses or toggles what has the focus, B backs out (<see cref="DialogKeys.Cancel"/>).
/// </summary>
public static class DialogPad
{
    public static void Handle(Window dialog, InputAction action)
    {
        if (action == InputAction.Cancel)
        {
            DialogKeys.Cancel(dialog);
            return;
        }
        if (DialogKeys.IsBusy(dialog)) return; // e.g. Settings waiting for a key to bind

        // An open drop-down takes the D-pad and A: choose within it, A to close it on the choice.
        if (dialog.GetVisualDescendants().OfType<ComboBox>().FirstOrDefault(c => c.IsDropDownOpen) is { } open)
        {
            switch (action)
            {
                case InputAction.MoveNorth: open.SelectedIndex = Math.Max(0, open.SelectedIndex - 1); break;
                case InputAction.MoveSouth: open.SelectedIndex = Math.Min(open.ItemCount - 1, open.SelectedIndex + 1); break;
                case InputAction.Confirm: open.IsDropDownOpen = false; break;
            }
            return;
        }

        var focused = Focused(dialog);
        if (focused is null)
        {
            // Nothing has the focus yet: the first press lands on the first control.
            if (action.ToDirection() is not null || action == InputAction.Confirm) MoveFocus(dialog, dialog, forward: true);
            return;
        }

        switch (action)
        {
            case InputAction.MoveNorth: Vertical(dialog, focused, Key.Up, forward: false); break;
            case InputAction.MoveSouth: Vertical(dialog, focused, Key.Down, forward: true); break;
            case InputAction.MoveWest: Horizontal(dialog, focused, Key.Left, forward: false); break;
            case InputAction.MoveEast: Horizontal(dialog, focused, Key.Right, forward: true); break;
            case InputAction.Confirm: Activate(focused); break;
        }
    }

    /// <summary>The control in the dialog that has the keyboard focus, if any.</summary>
    public static Control? Focused(Window dialog) =>
        dialog.FocusManager?.GetFocusedElement() is Control c && c.GetVisualAncestors().Contains(dialog) ? c : null;

    /// <summary>
    /// Up/Down: lists move their selection; everything else passes the focus on — a closed
    /// drop-down or a slider would otherwise change its value, which is Left/Right's job.
    /// </summary>
    private static void Vertical(Window dialog, Control focused, Key key, bool forward)
    {
        var inList = focused is ListBoxItem || focused is ListBox;
        if (inList && Press(focused, key)) return;
        MoveFocus(dialog, focused, forward);
    }

    /// <summary>Left/Right: sliders, tabs and text fields use them; otherwise the focus moves.</summary>
    private static void Horizontal(Window dialog, Control focused, Key key, bool forward)
    {
        if (focused is ComboBox) { MoveFocus(dialog, focused, forward); return; }
        if (focused is Slider or TabItem or TextBox && Press(focused, key)) return;
        MoveFocus(dialog, focused, forward);
    }

    /// <summary>A: presses a button, toggles a check box or switch, opens a drop-down, picks a list entry.</summary>
    private static void Activate(Control focused)
    {
        switch (focused)
        {
            case ComboBox combo:
                combo.IsDropDownOpen = true;
                break;
            case ToggleButton toggle: // check boxes, toggle switches
                toggle.IsChecked = toggle.IsThreeState ? toggle.IsChecked switch { true => null, false => true, null => false } : toggle.IsChecked != true;
                break;
            case Button button:
                if (button.Command?.CanExecute(button.CommandParameter) == true) button.Command.Execute(button.CommandParameter);
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                break;
            case TabItem tab:
                tab.IsSelected = true;
                break;
            default:
                // Lists (Load: Enter loads the chosen character) and anything else that answers Enter.
                Press(focused, Key.Enter);
                break;
        }
    }

    /// <summary>Moves the focus to the next (or previous) control, as Tab / Shift+Tab would.</summary>
    private static void MoveFocus(Window dialog, IInputElement from, bool forward)
    {
        var next = KeyboardNavigationHandler.GetNext(from, forward ? NavigationDirection.Next : NavigationDirection.Previous);
        if (next is null || next is Control c && !c.GetVisualAncestors().Contains(dialog) && !ReferenceEquals(c, dialog)) return;
        next.Focus(NavigationMethod.Tab);
    }

    /// <summary>Sends a key to a control as if typed; true if the control used it.</summary>
    private static bool Press(Control target, Key key)
    {
        var down = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, Source = target };
        target.RaiseEvent(down);
        target.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyUpEvent, Key = key, Source = target });
        return down.Handled;
    }
}
