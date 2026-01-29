using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using avalonia_terminal.ViewModels;

namespace avalonia_terminal.Views;

public partial class GalleryView : UserControl
{
    // --- スクロール制御用変数 ---
    private Point _startPoint;
    private double _startOffset;
    private bool _isDragging = false;
    private bool _isScrollAction = false;

    public GalleryView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    // ----------------------------------------------------------------
    // 1. キーボード・フォーカス制御（元の機能）
    // ----------------------------------------------------------------

    // テキストボックスにフォーカスが当たったとき、自動でキーボードを表示
    public void OnSearchBoxFocused(object? sender, GotFocusEventArgs e)
    {
        if (DataContext is GalleryViewModel vm)
        {
            if (!vm.IsSearchKeyboardVisible)
            {
                vm.OpenSearchKeyboardCommand.Execute(null);
            }
        }
    }

    // 画面のどこかがクリックされたとき（キーボードを閉じる判定）
    public void OnBackgroundClicked(object? sender, PointerPressedEventArgs e)
    {
        var source = e.Source as Visual;
        if (source == null) return;

        var searchBox = this.FindControl<TextBox>("SearchTextBox");
        var keyboardContainer = this.FindControl<Grid>("KeyboardContainer");

        bool isTextBox = IsChildOf(source, searchBox);
        bool isKeyboard = IsChildOf(source, keyboardContainer);

        // テキストボックスでもキーボードでもない場所なら、キーボードを閉じる
        if (!isTextBox && !isKeyboard)
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel?.FocusManager != null)
            {
                topLevel.FocusManager.ClearFocus();
            }

            if (DataContext is GalleryViewModel vm)
            {
                if (vm.IsSearchKeyboardVisible)
                {
                    vm.CloseSearchKeyboardCommand.Execute(null);
                }
            }
        }
    }

    private bool IsChildOf(Visual? source, Visual? target)
    {
        if (source == null || target == null) return false;
        return target.IsVisualAncestorOf(source) || source == target;
    }


    // ----------------------------------------------------------------
    // 2. タッチスクロール制御（新しい機能）
    // ----------------------------------------------------------------

    private void OnScrollPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var scrollViewer = this.FindControl<ScrollViewer>("MainScroll");
        if (scrollViewer == null) return;

        var properties = e.GetCurrentPoint(this).Properties;
        if (properties.IsLeftButtonPressed)
        {
            _startPoint = e.GetPosition(this);
            _startOffset = scrollViewer.Offset.Y;
            _isDragging = true;
            _isScrollAction = false;
            
            // スクロール操作を開始したら、キーボードを閉じるなどの処理も走らせる
            // (OnBackgroundClickedが親Gridで拾ってくれるはずだが、念のためここでもキャプチャ)
            e.Pointer.Capture(scrollViewer);
        }
    }

    private void OnScrollPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isDragging) return;

        var scrollViewer = this.FindControl<ScrollViewer>("MainScroll");
        if (scrollViewer == null) return;

        var currentPoint = e.GetPosition(this);
        var deltaY = _startPoint.Y - currentPoint.Y;

        // 5ピクセル以上動いたらスクロールとみなす
        if (Math.Abs(deltaY) > 5)
        {
            _isScrollAction = true;
            scrollViewer.Offset = new Vector(scrollViewer.Offset.X, _startOffset + deltaY);
        }
    }

    private void OnScrollPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isDragging) return;

        _isDragging = false;
        e.Pointer.Capture(null);

        // スクロールじゃなかった(=タップ)場合のみ、アイテム選択処理
        if (!_isScrollAction)
        {
            var point = e.GetPosition(this);
            var result = this.InputHitTest(point) as Visual;

            if (result != null)
            {
                var border = FindParentBorderWithTag(result);
                if (border != null && border.Tag is GalleryItemViewModel itemVM)
                {
                    itemVM.ItemClickCommand.Execute(null);
                }
            }
        }
    }

    private Border? FindParentBorderWithTag(Visual? start)
    {
        var current = start;
        while (current != null)
        {
            if (current is Border border && border.Tag is GalleryItemViewModel)
            {
                return border;
            }
            current = current.GetVisualParent();
        }
        return null;
    }
}
