using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Windows.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using avalonia_terminal.Models;
using avalonia_terminal.Services;
using System.Globalization;
using System.IO; 
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using OpenCvSharp;

namespace avalonia_terminal.ViewModels;

public enum GalleryViewMode {
    List,
    Detail
}

public class GalleryDateGroupViewModel : ViewModelBase
{
    public string DateHeader { get; }
    public ObservableCollection<GalleryItemViewModel> Items { get; } = new();

    public GalleryDateGroupViewModel(IGrouping<string, InspectionRecord> group, GalleryViewModel parentVM)
    {
        if (DateTime.TryParseExact(group.Key, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            DateHeader = date.ToString("yyyy年MM月dd日");
        }
        else
        {
            DateHeader = group.Key;
        }

        foreach (var record in group)
        {
            Items.Add(new GalleryItemViewModel(record, parentVM));
        }
    }
}

public class GalleryViewModel : ViewModelBase
{
    private readonly MainViewModel _main;
    private readonly DatabaseService _dbService;
    private List<InspectionRecord> _allRecords = new();
    private List<InspectionRecord> _filteredRecordsList = new();

    public ObservableCollection<GalleryDateGroupViewModel> DisplayGroups { get; } = new();

    private GalleryViewMode _currentMode = GalleryViewMode.List;
    public GalleryViewMode CurrentMode
    {
        get => _currentMode;
        set { _currentMode = value; RaisePropertyChanged(); }
    }

    private bool _isDeleteMode = false;
    public bool IsDeleteMode
    {
        get => _isDeleteMode;
        set 
        { 
            _isDeleteMode = value; 
            RaisePropertyChanged(); 
            if(value) IsRenameMode = false;
            UpdateItemsMode();
            RaisePropertyChanged(nameof(DeleteButtonText));
            RaisePropertyChanged(nameof(StatusMessage));
        }
    }
    
    private bool _isRenameMode = false;
    public bool IsRenameMode
    {
        get => _isRenameMode;
        set
        {
            _isRenameMode = value;
            RaisePropertyChanged();
            if(value) IsDeleteMode = false;
            UpdateItemsMode();
            RaisePropertyChanged(nameof(RenameButtonText));
            RaisePropertyChanged(nameof(StatusMessage));
        }
    }

    private bool _showRenameDialog = false;
    public bool ShowRenameDialog
    {
        get => _showRenameDialog;
        set { _showRenameDialog = value; RaisePropertyChanged(); }
    }
    
    private bool _isSearchKeyboardVisible = false;
    public bool IsSearchKeyboardVisible
    {
        get => _isSearchKeyboardVisible;
        set { _isSearchKeyboardVisible = value; RaisePropertyChanged(); }
    }

    private bool _isUpperCase = true;
    public bool IsUpperCase
    {
        get => _isUpperCase;
        set 
        { 
            _isUpperCase = value; 
            RaisePropertyChanged(); 
            RaisePropertyChanged(nameof(CapsButtonText));
            RaisePropertyChanged(nameof(CapsButtonColor));
        }
    }
    public string CapsButtonText => IsUpperCase ? "A" : "a";
    public string CapsButtonColor => IsUpperCase ? "#007ACC" : "#888888";

    private string _renameErrorMessage = "";
    public string RenameErrorMessage
    {
        get => _renameErrorMessage;
        set { _renameErrorMessage = value; RaisePropertyChanged(); }
    }

    private string _renameInput = "";
    public string RenameInput
    {
        get => _renameInput;
        set 
        {
            if (string.IsNullOrEmpty(value) || Regex.IsMatch(value, "^[a-zA-Z0-9_-]*$"))
            {
                _renameInput = value;
                RenameErrorMessage = "";
                RaisePropertyChanged();
            }
        }
    }
    
    private GalleryItemViewModel? _targetItemForRename;

    private bool _showDeleteConfirm = false;
    public bool ShowDeleteConfirm
    {
        get => _showDeleteConfirm;
        set { _showDeleteConfirm = value; RaisePropertyChanged(); }
    }

    public string DeleteButtonText => IsDeleteMode ? "実行" : "削除";
    public string RenameButtonText => IsRenameMode ? "キャンセル" : "名前変更";

    public string StatusMessage
    {
        get
        {
            if (IsDeleteMode) return "削除する画像を選択してください";
            if (IsRenameMode) return "名前を変更する画像を選択してください";
            return "日付順に表示中";
        }
    }

    private GalleryDetailViewModel? _detailViewModel;
    public GalleryDetailViewModel? DetailViewModel
    {
        get => _detailViewModel;
        set { _detailViewModel = value; RaisePropertyChanged(); }
    }

    private string _searchText = "";
    public string SearchText
    {
        get => _searchText;
        set { _searchText = value; RaisePropertyChanged(); ApplyFilter(); }
    }

    public ICommand HeaderDeleteButtonCommand { get; }
    public ICommand HeaderRenameButtonCommand { get; }
    public ICommand ExecuteDeleteConfirmCommand { get; }
    public ICommand CancelDeleteConfirmCommand { get; }
    public ICommand ExecuteRenameCommand { get; }
    public ICommand CancelRenameCommand { get; }
    public ICommand ToggleCaseCommand { get; }
    public ICommand KeyboardAppendCommand { get; }
    public ICommand KeyboardBackspaceCommand { get; }
    public ICommand KeyboardClearCommand { get; }
    public ICommand OpenSearchKeyboardCommand { get; }
    public ICommand CloseSearchKeyboardCommand { get; }
    public ICommand SearchKeyboardAppendCommand { get; }
    public ICommand SearchKeyboardBackspaceCommand { get; }
    public ICommand SearchKeyboardClearCommand { get; }
    public ICommand ShowDetailCommand { get; }
    public ICommand BackToListCommand { get; }
    public ICommand BackCommand { get; }

    public GalleryViewModel(MainViewModel main)
    {
        _main = main;
        _dbService = new DatabaseService();
        
        BackCommand = new RelayCommand(() => 
        {
            if (IsDeleteMode || IsRenameMode) QuitModes();
            else _main.Navigate(new HomeViewModel(_main));
        });
        BackToListCommand = new RelayCommand(ExecuteBackToList);
        
        ShowDetailCommand = new RelayCommand<InspectionRecord>(r => OpenDetail(r, startFromEnd: false));

        HeaderDeleteButtonCommand = new RelayCommand(() =>
        {
            if (!IsDeleteMode) IsDeleteMode = true;
            else
            {
                if (CountSelectedItems() > 0) ShowDeleteConfirm = true;
                else QuitModes();
            }
        });
        HeaderRenameButtonCommand = new RelayCommand(() => { IsRenameMode = !IsRenameMode; });

        ExecuteDeleteConfirmCommand = new RelayCommand(PerformDeletion);
        CancelDeleteConfirmCommand = new RelayCommand(() => { ShowDeleteConfirm = false; QuitModes(); });

        ExecuteRenameCommand = new RelayCommand(PerformRename);
        CancelRenameCommand = new RelayCommand(() => { ShowRenameDialog = false; _targetItemForRename = null; });
        
        ToggleCaseCommand = new RelayCommand(() => IsUpperCase = !IsUpperCase);

        KeyboardAppendCommand = new LocalRelayCommand<string>(key => 
        {
            string text = key;
            if (!IsUpperCase && text.Length == 1 && char.IsLetter(text[0])) text = text.ToLower();
            RenameInput += text;
        });
        KeyboardBackspaceCommand = new RelayCommand(() => 
        {
            if (!string.IsNullOrEmpty(RenameInput)) RenameInput = RenameInput.Substring(0, RenameInput.Length - 1);
        });
        KeyboardClearCommand = new RelayCommand(() => RenameInput = "");

        OpenSearchKeyboardCommand = new RelayCommand(() => IsSearchKeyboardVisible = true);
        CloseSearchKeyboardCommand = new RelayCommand(() => IsSearchKeyboardVisible = false);
        SearchKeyboardAppendCommand = new LocalRelayCommand<string>(key => 
        {
            string text = key;
            if (!IsUpperCase && text.Length == 1 && char.IsLetter(text[0])) text = text.ToLower();
            SearchText += text;
        });
        SearchKeyboardBackspaceCommand = new RelayCommand(() => 
        {
            if (!string.IsNullOrEmpty(SearchText)) SearchText = SearchText.Substring(0, SearchText.Length - 1);
        });
        SearchKeyboardClearCommand = new RelayCommand(() => SearchText = "");

        RunReorderScript();
        LoadData();
    }
    
    private void RunReorderScript()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "python3",
                Arguments = "/home/shikoku-pc/db/reorder_db.py",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using (var process = Process.Start(psi))
            {
                process?.WaitForExit();
            }
        }
        catch (Exception ex) { Console.WriteLine($"Reorder Script Error: {ex.Message}"); }
    }

    public void OnItemClicked(GalleryItemViewModel item)
    {
        if (IsRenameMode)
        {
            _targetItemForRename = item;
            RenameInput = item.Record.SaveName;
            RenameErrorMessage = ""; 
            ShowRenameDialog = true;
        }
    }

    private void UpdateItemsMode()
    {
        foreach (var group in DisplayGroups)
        {
            foreach (var item in group.Items)
            {
                item.IsDeleteMode = IsDeleteMode;
                item.IsRenameMode = IsRenameMode;
                if (!IsDeleteMode) item.IsSelected = false;
            }
        }
    }

    private int CountSelectedItems()
    {
        int count = 0;
        foreach (var group in DisplayGroups)
            foreach (var item in group.Items)
                if (item.IsSelected) count++;
        return count;
    }

    private void QuitModes()
    {
        ShowDeleteConfirm = false;
        ShowRenameDialog = false;
        IsDeleteMode = false;
        IsRenameMode = false;
    }

    private void PerformDeletion()
    {
        var itemsToDelete = new List<GalleryItemViewModel>();
        foreach (var group in DisplayGroups)
        {
            foreach (var item in group.Items)
            {
                if (item.IsSelected) itemsToDelete.Add(item);
            }
        }

        foreach (var item in itemsToDelete)
        {
            _dbService.DeleteInspection(item.Record.Id);
            if (Directory.Exists(item.Record.SaveAbsolutePath))
            {
                try { Directory.Delete(item.Record.SaveAbsolutePath, true); }
                catch (Exception ex) { Console.WriteLine($"File Delete Error: {ex.Message}"); }
            }
        }

        RunReorderScript();
        QuitModes();
        LoadData();
    }

    private void PerformRename()
    {
        if (_targetItemForRename == null || string.IsNullOrWhiteSpace(RenameInput)) return;

        string oldName = _targetItemForRename.Record.SaveName;
        string newName = RenameInput;
        string oldPath = _targetItemForRename.Record.SaveAbsolutePath;
        string parentDir = Path.GetDirectoryName(oldPath) ?? "/home/shikoku-pc/pic";
        string newPath = Path.Combine(parentDir, newName);

        if (oldName == newName) { ShowRenameDialog = false; return; }

        try
        {
            if (Directory.Exists(newPath)) 
            { 
                RenameErrorMessage = "既に同名のフォルダが存在します";
                return; 
            }

            Directory.Move(oldPath, newPath);
            string[] files = Directory.GetFiles(newPath);
            foreach (var file in files)
            {
                string fileName = Path.GetFileName(file);
                if (fileName.Contains(oldName))
                {
                    string newFileName = fileName.Replace(oldName, newName);
                    string newFilePath = Path.Combine(newPath, newFileName);
                    File.Move(file, newFilePath);
                }
            }

            _dbService.UpdateInspectionName(_targetItemForRename.Record.Id, newName, newPath);
            ShowRenameDialog = false;
            IsRenameMode = false;
            LoadData();
        }
        catch (Exception ex) { Console.WriteLine($"Rename Error: {ex.Message}"); }
    }

    public void OpenDetail(InspectionRecord? record, bool startFromEnd)
    {
        if (IsDeleteMode || IsRenameMode || record == null) return;
        DetailViewModel = new GalleryDetailViewModel(record, this, startFromEnd);
        CurrentMode = GalleryViewMode.Detail; 
    }
    
    private void ExecuteBackToList()
    {
        DetailViewModel = null;
        CurrentMode = GalleryViewMode.List;
    }

    private void LoadData()
    {
        _dbService.Initialize();
        _allRecords = _dbService.GetAllRecords();
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        DisplayGroups.Clear();
        ExecuteBackToList();
        IsDeleteMode = false; 
        IsRenameMode = false;

        var filteredQuery = _allRecords.Where(r =>
        {
            bool matchText = string.IsNullOrEmpty(SearchText) ||
                             r.SaveName.Contains(SearchText, StringComparison.Ordinal) ||
                             r.Date.Contains(SearchText);
            return matchText;
        })
        .OrderByDescending(g => g.Date);

        _filteredRecordsList = filteredQuery.Take(50).ToList();
        var grouped = _filteredRecordsList
            .GroupBy(r => r.Date.Length >= 10 ? r.Date.Substring(0, 10) : r.Date);

        foreach (var group in grouped)
        {
            DisplayGroups.Add(new GalleryDateGroupViewModel(group, this));
        }
    }

    public bool CanGoNextRecord(InspectionRecord current)
    {
        int index = _filteredRecordsList.FindIndex(r => r.Id == current.Id);
        return index >= 0 && index < _filteredRecordsList.Count - 1;
    }

    public bool CanGoPreviousRecord(InspectionRecord current)
    {
        int index = _filteredRecordsList.FindIndex(r => r.Id == current.Id);
        return index > 0;
    }

    public void GoToNextRecord(InspectionRecord current)
    {
        int index = _filteredRecordsList.FindIndex(r => r.Id == current.Id);
        if (index >= 0 && index < _filteredRecordsList.Count - 1)
        {
            OpenDetail(_filteredRecordsList[index + 1], startFromEnd: false);
        }
    }

    public void GoToPreviousRecord(InspectionRecord current)
    {
        int index = _filteredRecordsList.FindIndex(r => r.Id == current.Id);
        if (index > 0)
        {
            OpenDetail(_filteredRecordsList[index - 1], startFromEnd: true);
        }
    }
}

public class GalleryItemViewModel : ViewModelBase
{
    public InspectionRecord Record { get; }
    
    private Bitmap? _thumbnail;
    public Bitmap? Thumbnail 
    { 
        get => _thumbnail;
        set { _thumbnail = value; RaisePropertyChanged(); }
    }

    public string TypeLabel => "検査画像";
    public string LabelColor => "#007ACC";
    
    private bool _isDeleteMode = false;
    public bool IsDeleteMode
    {
        get => _isDeleteMode;
        set { _isDeleteMode = value; RaisePropertyChanged(); }
    }
    
    private bool _isRenameMode = false;
    public bool IsRenameMode
    {
        get => _isRenameMode;
        set { _isRenameMode = value; RaisePropertyChanged(); }
    }

    private bool _isSelected = false;
    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; RaisePropertyChanged(); }
    }
    
    public ICommand ItemClickCommand { get; } 

    public GalleryItemViewModel(InspectionRecord record, GalleryViewModel parentVM)
    {
        Record = record;
        ItemClickCommand = new RelayCommand(() => 
        {
            if (parentVM.IsDeleteMode) IsSelected = !IsSelected;
            else if (parentVM.IsRenameMode) parentVM.OnItemClicked(this);
            else parentVM.ShowDetailCommand.Execute(record);
        });
        LoadThumbnailAsync();
    }

    private async void LoadThumbnailAsync()
    {
        await Task.Run(() => 
        {
            try
            {
                if (File.Exists(Record.ThumbnailPath))
                {
                    using (var stream = File.OpenRead(Record.ThumbnailPath))
                    {
                        var bmp = Bitmap.DecodeToWidth(stream, 240);
                        Dispatcher.UIThread.Post(() => Thumbnail = bmp);
                    }
                }
            }
            catch (Exception) { /* エラー時はnull */ }
        });
    }
}

public class GalleryDetailViewModel : ViewModelBase
{
    private readonly GalleryViewModel _parentVM;
    public InspectionRecord Record { get; }
    private List<Bitmap> _images = new();
    private List<string> _imagePaths = new(); 
    private Bitmap? _currentImage;
    public Bitmap? CurrentImage
    {
        get => _currentImage;
        set { _currentImage = value; RaisePropertyChanged(); }
    }
    
    public ObservableCollection<GalleryDetectionItem> DetectionItems { get; } = new();

    private string _detectionStatusMessage = "読み込み中...";
    public string DetectionStatusMessage
    {
        get => _detectionStatusMessage;
        set { _detectionStatusMessage = value; RaisePropertyChanged(); }
    }
    
    private int _currentPageIndex = 0;
    public string PageIndicator => _images.Count > 0 ? $"{_currentPageIndex + 1} / {_images.Count}" : "";
    public bool CanGoPreviousImage => _currentPageIndex > 0;
    public bool CanGoNextImage => _currentPageIndex < _images.Count - 1;
    public bool CanMovePrevious => CanGoPreviousImage || _parentVM.CanGoPreviousRecord(Record);
    public bool CanMoveNext => CanGoNextImage || _parentVM.CanGoNextRecord(Record);
    public string TypeLabel => "画像詳細";
    public string FormattedDate => DateTime.TryParseExact(Record.Date, "yyyy-MM-dd-HH-mm-ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
        ? date.ToString("yyyy年MM月dd日 HH時mm分ss秒") : Record.Date;
    public string BoardInfo => "基板情報: 取得中";
    
    // --- 修正機能プロパティ ---
    private bool _isCorrectionMode = false;
    public bool IsCorrectionMode
    {
        get => _isCorrectionMode;
        set 
        { 
            _isCorrectionMode = value; 
            RaisePropertyChanged(); 
            RaisePropertyChanged(nameof(CorrectionButtonText));
            UpdateItemsCorrectionState();
        }
    }
    public string CorrectionButtonText => IsCorrectionMode ? "修正終了" : "修正";

    private bool _isShowKeypad = false;
    public bool IsShowKeypad
    {
        get => _isShowKeypad;
        set { _isShowKeypad = value; RaisePropertyChanged(); }
    }

    private string _editInputValue = "";
    public string EditInputValue
    {
        get => _editInputValue;
        set { _editInputValue = value; RaisePropertyChanged(); }
    }

    private GalleryDetectionItem? _selectedEditItem;

    public ICommand CloseDetailCommand { get; }
    public ICommand MoveNextCommand { get; }
    public ICommand MovePreviousCommand { get; }
    public ICommand ToggleCorrectionCommand { get; }
    public ICommand ItemEditCommand { get; }
    public ICommand KeypadAppendCommand { get; }
    public ICommand KeypadBackspaceCommand { get; }
    public ICommand KeypadClearCommand { get; }
    public ICommand KeypadEnterCommand { get; }
    public ICommand KeypadCancelCommand { get; }

    public GalleryDetailViewModel(InspectionRecord record, GalleryViewModel parent, bool startFromEnd)
    {
        Record = record;
        _parentVM = parent;
        CloseDetailCommand = parent.BackToListCommand;
        
        ToggleCorrectionCommand = new RelayCommand(() => IsCorrectionMode = !IsCorrectionMode);
        
        ItemEditCommand = new RelayCommand<GalleryDetectionItem>(item => 
        {
            if (!IsCorrectionMode || item == null) return;
            _selectedEditItem = item;
            EditInputValue = item.RawValue;
            IsShowKeypad = true;
        });

        KeypadAppendCommand = new LocalRelayCommand<string>(key => EditInputValue += key);
        KeypadBackspaceCommand = new RelayCommand(() => 
        {
            if (EditInputValue.Length > 0) EditInputValue = EditInputValue.Substring(0, EditInputValue.Length - 1);
        });
        KeypadClearCommand = new RelayCommand(() => EditInputValue = "");
        KeypadCancelCommand = new RelayCommand(() => IsShowKeypad = false);
        KeypadEnterCommand = new RelayCommand(SaveCorrection);

        var paths = new List<string>();
        
        if (!string.IsNullOrEmpty(record.SimpleOmotePath)) paths.Add(record.SimpleOmotePath);
        if (!string.IsNullOrEmpty(record.SimpleUraPath)) paths.Add(record.SimpleUraPath);
        if (!string.IsNullOrEmpty(record.PrecisionPcbOmotePath)) paths.Add(record.PrecisionPcbOmotePath);
        if (!string.IsNullOrEmpty(record.PrecisionPcbUraPath)) paths.Add(record.PrecisionPcbUraPath);
        if (!string.IsNullOrEmpty(record.PrecisionCircuitOmotePath)) paths.Add(record.PrecisionCircuitOmotePath);
        if (!string.IsNullOrEmpty(record.PrecisionCircuitUraPath)) paths.Add(record.PrecisionCircuitUraPath);

        foreach (var path in paths)
        {
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                try 
                { 
                    _images.Add(new Bitmap(path)); 
                    _imagePaths.Add(path);
                }
                catch { }
            }
        }

        if (_images.Count > 0)
        {
            if (startFromEnd) _currentPageIndex = _images.Count - 1;
            else _currentPageIndex = 0;
            CurrentImage = _images[_currentPageIndex];
        }
        
        UpdateNavigationState();
        LoadDetections();

        MoveNextCommand = new RelayCommand(() =>
        {
            if (CanGoNextImage)
            {
                _currentPageIndex++;
                CurrentImage = _images[_currentPageIndex];
                UpdateNavigationState();
                LoadDetections();
            }
            else if (_parentVM.CanGoNextRecord(Record))
            {
                _parentVM.GoToNextRecord(Record);
            }
        });
        MovePreviousCommand = new RelayCommand(() =>
        {
            if (CanGoPreviousImage)
            {
                _currentPageIndex--;
                CurrentImage = _images[_currentPageIndex];
                UpdateNavigationState();
                LoadDetections();
            }
            else if (_parentVM.CanGoPreviousRecord(Record))
            {
                _parentVM.GoToPreviousRecord(Record);
            }
        });
    }

    private void UpdateNavigationState()
    {
        RaisePropertyChanged(nameof(PageIndicator));
        RaisePropertyChanged(nameof(CanMovePrevious));
        RaisePropertyChanged(nameof(CanMoveNext));
    }
    
    private void UpdateItemsCorrectionState()
    {
        foreach(var item in DetectionItems)
        {
            item.IsCorrectionMode = IsCorrectionMode;
        }
        // コレクション全体の更新を通知しないとViewのスタイルが反映されないことがあるため
        // ここでは簡易的に、リストをリフレッシュする手もあるが、バインディングで解決する
    }

    private void SaveCorrection()
    {
        if (_selectedEditItem == null) return;

        try
        {
            var db = new DatabaseService();
            // DB更新
            db.UpdateDetectionValue(Record.SaveName, _selectedEditItem.Id, EditInputValue);
            
            // リストのアイテムを更新 (再読み込みする方が確実だが、チラつき防止のため値だけ更新)
            // コレクションを置き換えるか、プロパティ変更通知が必要
            // ここでは簡易的にリスト再構築を行う
            LoadDetections();
            
            IsShowKeypad = false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Save Error: {ex.Message}");
        }
    }
    
    private void LoadDetections()
    {
        DetectionItems.Clear();
        DetectionStatusMessage = "データ確認中...";
        
        if (_currentPageIndex < 0 || _currentPageIndex >= _imagePaths.Count) return;
        string currentPath = _imagePaths[_currentPageIndex];

        Task.Run(() => 
        {
            try
            {
                var db = new DatabaseService();
                var detections = db.GetDetections(Record.SaveName);

                if (detections.Count == 0)
                {
                    Dispatcher.UIThread.Post(() => DetectionStatusMessage = "検出データがありません");
                    return;
                }

                using var src = Cv2.ImRead(currentPath);
                if (src.Empty())
                {
                     Dispatcher.UIThread.Post(() => DetectionStatusMessage = "画像読み込みエラー");
                     return;
                }

                var items = new List<GalleryDetectionItem>();

                foreach (var det in detections)
                {
                    if (det.YoloObb == null) continue;

                    var center = new Point2f(det.YoloObb.CenterX, det.YoloObb.CenterY);
                    var size = new Size2f(det.YoloObb.Width, det.YoloObb.Height);
                    float angleDeg = det.YoloObb.RotationRad * 180f / (float)Math.PI;

                    var rotatedRect = new RotatedRect(center, size, angleDeg);
                    var rectSize = rotatedRect.Size;
                    if (rectSize.Width <= 0 || rectSize.Height <= 0) continue;

                    using var cropped = new Mat();
                    Cv2.GetRectSubPix(src, new OpenCvSharp.Size((int)rectSize.Width, (int)rectSize.Height), rotatedRect.Center, cropped);
                    
                    var bmp = MatToBitmap(cropped);
                    var newItem = new GalleryDetectionItem(det.DetectionId, det.Value ?? "", bmp);
                    newItem.IsCorrectionMode = IsCorrectionMode; // 現在のモードを反映
                    items.Add(newItem);
                }

                Dispatcher.UIThread.Post(() =>
                {
                    foreach(var i in items) DetectionItems.Add(i);
                    DetectionStatusMessage = items.Count > 0 ? "" : "表示対象がありません";
                });
            }
            catch (Exception ex)
            {
                Dispatcher.UIThread.Post(() => DetectionStatusMessage = $"エラー: {ex.Message}");
            }
        });
    }

    private Avalonia.Media.Imaging.Bitmap? MatToBitmap(Mat mat)
    {
        try
        {
            using var ms = mat.ToMemoryStream(".png");
            ms.Position = 0;
            return new Avalonia.Media.Imaging.Bitmap(ms);
        }
        catch { return null; }
    }
}
