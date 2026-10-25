using System;
using Avalonia;
using Avalonia.Controls;

namespace NenFocus.Views.Controls;

/// <summary>
/// A card over a dimmed screen — "Are you sure?", or a notice to read and dismiss (Gap 89 Slice B).
/// The screen gives the words and its buttons; the dialog owns the backdrop, the card, the text
/// styles and the button row (its look is ModalDialog.axaml, the one place it is defined).
/// <code>
/// &lt;controls:ModalDialog IsVisible="{Binding IsConfirmDeleteVisible}" Title="Delete Preset"
///                       Message="{Binding ConfirmDeleteMessage}" Error="{Binding ConfirmDeleteError}"&gt;
///     &lt;Button Classes="secondary" Content="Cancel" Command="{Binding CancelDeleteCommand}"/&gt;
///     &lt;Button Classes="danger-btn" Content="Delete" Command="{Binding ConfirmDeleteCommand}"/&gt;
/// &lt;/controls:ModalDialog&gt;
/// </code>
/// An ItemsControl so it can hold several buttons as its content.
/// </summary>
public class ModalDialog : ItemsControl
{
    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<ModalDialog, string?>(nameof(Title));

    public static readonly StyledProperty<string?> MessageProperty =
        AvaloniaProperty.Register<ModalDialog, string?>(nameof(Message));

    public static readonly StyledProperty<string?> ErrorProperty =
        AvaloniaProperty.Register<ModalDialog, string?>(nameof(Error));

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string? Message
    {
        get => GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    /// <summary>Shown under the message when it isn't empty — e.g. why a delete was refused.</summary>
    public string? Error
    {
        get => GetValue(ErrorProperty);
        set => SetValue(ErrorProperty, value);
    }

    // Its own look (ModalDialog.axaml), not the theme's ItemsControl.
    protected override Type StyleKeyOverride => typeof(ModalDialog);
}
