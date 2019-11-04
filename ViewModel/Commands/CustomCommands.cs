using System.Windows.Input;

namespace TVRename.ViewModel.Commands
{
    public static class CustomCommands
{
    public static readonly RoutedUICommand Exit = new RoutedUICommand
    (
        "Exit",
        "Exit",
        typeof(CustomCommands),
        new InputGestureCollection()
        {
            new KeyGesture(Key.F4, ModifierKeys.Alt)
        }
    );

    public static readonly RoutedUICommand ShowFilters = new RoutedUICommand
    (
        "Show Filters",
        "Filter",
        typeof(CustomCommands)
    );
        //Define more commands here, just like the one above
    }
}
