using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace avalonia_terminal.Views
{
    public partial class MeasurementView : UserControl
    {
        public MeasurementView()
        {
            InitializeComponent();
        }
        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
