using Godot;

namespace OpenCorneroids.Game.Ui;

/// <summary>Modal questions and notices, shared by the menu and the running world.</summary>
public static class Dialogs
{
    /// <summary>
    /// Shows a modal dialog under <paramref name="parent"/>. With a null <paramref name="onOk"/> it is a notice with one
    /// button. Exactly one of <paramref name="onOk"/> and <paramref name="onCancel"/> runs: closing the dialog with Esc
    /// or its close button counts as cancel, so a caller that paused something always gets to resume it.
    /// </summary>
    /// <param name="okText">Label of the confirm button; destructive actions should say what they do ("Delete").</param>
    public static AcceptDialog Show(Node parent, string title, string text, Action? onOk, string okText = "OK",
        string cancelText = "Cancel", Action? onCancel = null)
    {
        AcceptDialog dialog = onOk is null ? new AcceptDialog() : new ConfirmationDialog();
        dialog.Title = title;
        dialog.DialogText = text;
        dialog.DialogAutowrap = true;
        dialog.MinSize = new Vector2I(520, 0);
        dialog.Exclusive = true;
        bool answered = false;
        dialog.Confirmed += () =>
        {
            if (answered) return;
            answered = true;
            onOk?.Invoke();
        };
        dialog.Canceled += () =>
        {
            if (answered) return;
            answered = true;
            onCancel?.Invoke();
        };
        dialog.VisibilityChanged += () =>
        {
            if (!dialog.Visible) dialog.QueueFree();
        };
        parent.AddChild(dialog);
        // The labels go on the buttons themselves, after the dialog is in the tree: setting OkButtonText before that
        // still showed a plain "OK", so a permanent delete sat behind a button that did not say what it does.
        dialog.GetOkButton().Text = onOk is null ? "OK" : okText;
        if (dialog is ConfirmationDialog confirm) confirm.GetCancelButton().Text = cancelText;
        dialog.PopupCentered(new Vector2I(560, 0));
        return dialog;
    }

    /// <summary>
    /// Asks for one line of text: a label, a text box limited to <paramref name="maxLength"/> characters, and OK and
    /// Cancel. Enter in the box counts as OK; Esc, Cancel or the close button as cancel, and then exactly one of
    /// <paramref name="onOk"/> (with the text as typed) and <paramref name="onCancel"/> runs. The game is not paused.
    /// </summary>
    public static ConfirmationDialog AskText(Node parent, string title, string label, int maxLength, Action<string> onOk,
        Action? onCancel = null)
    {
        var dialog = new ConfirmationDialog { Title = title, Exclusive = true, MinSize = new Vector2I(500, 0) };
        var box = new VBoxContainer();
        box.AddChild(new Label { Text = label });
        var line = new LineEdit { MaxLength = Math.Max(1, maxLength), CustomMinimumSize = new Vector2(460, 0) };
        box.AddChild(line);
        dialog.AddChild(box);
        dialog.RegisterTextEnter(line);
        bool answered = false;
        dialog.Confirmed += () =>
        {
            if (answered) return;
            answered = true;
            onOk(line.Text);
        };
        dialog.Canceled += () =>
        {
            if (answered) return;
            answered = true;
            onCancel?.Invoke();
        };
        dialog.VisibilityChanged += () =>
        {
            if (!dialog.Visible) dialog.QueueFree();
        };
        parent.AddChild(dialog);
        dialog.GetOkButton().Text = "Ok";
        dialog.GetCancelButton().Text = "Cancel";
        dialog.PopupCentered(new Vector2I(500, 150));
        line.GrabFocus();
        return dialog;
    }
}
