using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using VoiceStudio.ViewModels;

namespace VoiceStudio.Views;

public partial class MixerView : UserControl
{
    private const string AppFormat = "VoiceStudio.App";

    public MixerView() => InitializeComponent();

    /// <summary>Démarre le déplacement d'une application vers un autre bus.</summary>
    private void OnAppChipMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        if (sender is not FrameworkElement element || element.DataContext is not ChannelViewModel app) return;

        // Un clic sur le curseur de volume ou sur le bouton couper ne déclenche pas de déplacement.
        if (e.OriginalSource is DependencyObject source && IsInsideControl(source)) return;

        DragDrop.DoDragDrop(element, new DataObject(AppFormat, app), DragDropEffects.Move);
    }

    private static bool IsInsideControl(DependencyObject source)
    {
        for (var current = source; current != null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is Slider or ButtonBase) return true;
            if (current is ItemsControl) return false;
        }
        return false;
    }

    private void OnBusDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(AppFormat) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnBusDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(AppFormat)) return;
        if (e.Data.GetData(AppFormat) is not ChannelViewModel app) return;
        if (sender is not FrameworkElement element || element.DataContext is not BusViewModel bus) return;
        if (DataContext is not MainViewModel model) return;

        model.Mixer.MoveApp(app, bus);
        e.Handled = true;
    }
}
