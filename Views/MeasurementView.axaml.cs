using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;

namespace avalonia_terminal.Views
{
    public partial class MeasurementView : UserControl
    {
        // スクロール制御用
        private Point _startPoint;
        private double _startOffset;
        private bool _isDragging = false;

        public MeasurementView()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void OnScrollPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            var scrollViewer = this.FindControl<ScrollViewer>("ResultScroll");
            if (scrollViewer == null) return;

            var properties = e.GetCurrentPoint(this).Properties;
            if (properties.IsLeftButtonPressed)
            {
                _startPoint = e.GetPosition(this);
                _startOffset = scrollViewer.Offset.Y;
                _isDragging = true;
                
                e.Pointer.Capture(scrollViewer);
            }
        }

        private void OnScrollPointerMoved(object? sender, PointerEventArgs e)
        {
            if (!_isDragging) return;
            var scrollViewer = this.FindControl<ScrollViewer>("ResultScroll");
            if (scrollViewer == null) return;

            var currentPoint = e.GetPosition(this);
            var deltaY = _startPoint.Y - currentPoint.Y;

            // スクロール適用
            scrollViewer.Offset = new Vector(scrollViewer.Offset.X, _startOffset + deltaY);
        }

        private void OnScrollPointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (!_isDragging) return;
            _isDragging = false;
            e.Pointer.Capture(null);
        }
    }
}
