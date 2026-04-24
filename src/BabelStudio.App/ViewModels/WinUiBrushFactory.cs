using System.Runtime.InteropServices;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace BabelStudio.App.ViewModels;

internal static class WinUiBrushFactory
{
    public static Brush? TryCreateSolidColorBrush(Color color)
    {
        try
        {
            return new SolidColorBrush(color);
        }
        catch (COMException)
        {
            return null;
        }
        catch (TypeInitializationException)
        {
            return null;
        }
    }
}
