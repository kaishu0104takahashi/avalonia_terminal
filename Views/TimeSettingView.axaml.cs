using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace avalonia_terminal.Views
{
    public partial class TimeSettingView : UserControl
    {
        public TimeSettingView()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
