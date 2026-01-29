using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace avalonia_terminal.Views;

public partial class JsonDisplayView : UserControl
{
    public JsonDisplayView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
