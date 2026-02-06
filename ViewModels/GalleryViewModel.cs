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

// カラーコード定義
public class ResistorColor
{
    public string Name { get; set; } = "";
    public string ColorCode { get; set; } = ""; 
    public string TextColor { get; set; } = "White";
    public int Digit { get; set; }      
    public double Multiplier { get; set; } 
    public string Tolerance { get; set; } = "";
}

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
            bool isCaseChangeOnly = oldName.Equals(newName, StringComparison.OrdinalIgnoreCase);

            if (!isCaseChangeOnly && Directory.Exists(newPath)) 
            { 
                RenameErrorMessage = "既に同名のフォルダが存在します";
                return; 
            }

            if (isCaseChangeOnly && Directory.Exists(newPath))
            {
                string tempPath = newPath + "_temp_" + Guid.NewGuid().ToString().Substring(0, 8);
                Directory.Move(oldPath, tempPath);
                Directory.Move(tempPath, newPath);
            }
            else
            {
                Directory.Move(oldPath, newPath);
            }

            string[] files = Directory.GetFiles(newPath);
            foreach (var file in files)
            {
                string fileName = Path.GetFileName(file);
                if (fileName.Contains(oldName))
                {
                    string newFileName = fileName.Replace(oldName, newName);
                    string newFilePath = Path.Combine(newPath, newFileName);
                    if (file != newFilePath)
                    {
                        File.Move(file, newFilePath);
                    }
                }
            }

            _dbService.UpdateInspectionName(_targetItemForRename.Record.Id, newName, newPath);
            ShowRenameDialog = false;
            IsRenameMode = false;
            LoadData();
        }
        catch (Exception ex) 
        { 
            Console.WriteLine($"Rename Error: {ex.Message}");
            RenameErrorMessage = $"エラー: {ex.Message}";
        }
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

    private Bitmap? _editTargetImage;
    public Bitmap? EditTargetImage
    {
        get => _editTargetImage;
        set { _editTargetImage = value; RaisePropertyChanged(); }
    }

    public List<ResistorColor> StandardColors { get; } = new List<ResistorColor>
    {
        new ResistorColor { Name="黒", ColorCode="Black", TextColor="White", Digit=0, Multiplier=1 },
        new ResistorColor { Name="茶", ColorCode="#A52A2A", TextColor="White", Digit=1, Multiplier=10 },
        new ResistorColor { Name="赤", ColorCode="Red", TextColor="White", Digit=2, Multiplier=100 },
        new ResistorColor { Name="橙", ColorCode="Orange", TextColor="Black", Digit=3, Multiplier=1000 },
        new ResistorColor { Name="黄", ColorCode="Yellow", TextColor="Black", Digit=4, Multiplier=10000 },
        new ResistorColor { Name="緑", ColorCode="Green", TextColor="White", Digit=5, Multiplier=100000 },
        new ResistorColor { Name="青", ColorCode="Blue", TextColor="White", Digit=6, Multiplier=1000000 },
        new ResistorColor { Name="紫", ColorCode="Purple", TextColor="White", Digit=7, Multiplier=10000000 },
        new ResistorColor { Name="灰", ColorCode="Gray", TextColor="Black", Digit=8, Multiplier=0 },
        new ResistorColor { Name="白", ColorCode="White", TextColor="Black", Digit=9, Multiplier=0 }
    };

    public List<ResistorColor> SpecialColors { get; } = new List<ResistorColor>
    {
        new ResistorColor { Name="金", ColorCode="Gold", TextColor="Black", Multiplier=0.1, Tolerance="±5%" },
        new ResistorColor { Name="銀", ColorCode="Silver", TextColor="Black", Multiplier=0.01, Tolerance="±10%" }
    };

    public ObservableCollection<ResistorColor> CurrentBands { get; } = new();

    private GalleryDetectionItem? _selectedEditItem;

    public ICommand CloseDetailCommand { get; }
    public ICommand MoveNextCommand { get; }
    public ICommand MovePreviousCommand { get; }
    public ICommand ToggleCorrectionCommand { get; }
    public ICommand ItemEditCommand { get; }
    public ICommand KeypadEnterCommand { get; }
    public ICommand KeypadCancelCommand { get; }
    public ICommand AddColorCommand { get; }
    public ICommand ClearColorsCommand { get; }
    public ICommand BackspaceColorCommand { get; }

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
            EditTargetImage = item.CroppedImage;
            
            // ★変更: 初期表示時もフォーマット(切り捨て)を適用
            EditInputValue = FormatDecimal(item.RawValue);
            
            CurrentBands.Clear(); 
            IsShowKeypad = true;
        });

        AddColorCommand = new RelayCommand<ResistorColor>(color => 
        {
            if (color == null) return;
            if (CurrentBands.Count >= 5) return; 

            CurrentBands.Add(color);
            CalculateValueFromBands();
        });
        ClearColorsCommand = new RelayCommand(() => 
        {
            CurrentBands.Clear();
            EditInputValue = "";
        });
        BackspaceColorCommand = new RelayCommand(() => 
        {
            if (CurrentBands.Count > 0)
            {
                CurrentBands.RemoveAt(CurrentBands.Count - 1);
                CalculateValueFromBands();
            }
        });
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

    // ★追加: 数値を文字列化する際に小数点第2位以下を切り捨てるヘルパー
    private string FormatDecimal(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return "";
        
        // オーム単位記号や接尾辞があれば一旦無視して数値部分だけ見るのは難しいので、
        // 単純な数値文字列であることを前提にパース
        if (double.TryParse(input, NumberStyles.Any, CultureInfo.InvariantCulture, out double val))
        {
            // 100倍してFloorして100で割る = 小数第2位まで残して切り捨て
            double truncated = Math.Floor(val * 100.0) / 100.0;
            return truncated.ToString("0.##"); // 不要な0は削除
        }
        return input;
    }

    private void CalculateValueFromBands()
    {
        if (CurrentBands.Count == 0)
        {
            EditInputValue = "";
            return;
        }

        double value = 0;
        var valueBands = CurrentBands.Where(b => string.IsNullOrEmpty(b.Tolerance)).ToList();
        
        if (valueBands.Count == 1)
        {
            value = valueBands[0].Digit;
        }
        else if (valueBands.Count == 2)
        {
            value = valueBands[0].Digit * 10 + valueBands[1].Digit;
        }
        else if (valueBands.Count >= 3)
        {
            double baseVal = valueBands[0].Digit * 10 + valueBands[1].Digit;
            // 3番目のバンド（インデックス2）を乗数として使用
            var multiplierBand = CurrentBands[2];
            value = baseVal * multiplierBand.Multiplier;
        }

        // ここでは生の数値を表示したいのか、単位付きか？
        // 元のコードでは単位付きだったが、スクリーンショットは生数値。
        // 要望の「切り捨て」を適用して文字列化
        
        // ★修正: 計算結果も切り捨てフォーマット
        double truncated = Math.Floor(value * 100.0) / 100.0;
        EditInputValue = truncated.ToString("0.##");
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
    }

    private void SaveCorrection()
    {
        if (_selectedEditItem == null) return;
        try
        {
            var db = new DatabaseService();
            db.UpdateDetectionValue(Record.SaveName, _selectedEditItem.Id, EditInputValue);
            LoadDetections();
            IsShowKeypad = false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Save Error: {ex.Message}");
        }
    }
    
    // ★ロジックを元の(a.txt)の状態に戻す
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

                    var obb = det.YoloObb;
                    float angleDeg = obb.RotationRad * (180f / (float)Math.PI);
                    
                    var center = new Point2f(obb.CenterX, obb.CenterY);
                    var size = new Size2f(obb.Width, obb.Height);
                    var rotatedRect = new RotatedRect(center, size, angleDeg);

                    Point2f[] pts = rotatedRect.Points();
                    Point2f tl = new Point2f(), tr = new Point2f(), br = new Point2f(), bl = new Point2f();
                    float min_s = float.MaxValue;
                    float max_s = float.MinValue;
                    float min_diff = float.MaxValue;
                    float max_diff = float.MinValue;

                    for (int i = 0; i < 4; i++)
                    {
                        float s = pts[i].X + pts[i].Y;
                        float diff = pts[i].Y - pts[i].X;

                        if (s < min_s) { min_s = s; tl = pts[i]; }
                        if (s > max_s) { max_s = s; br = pts[i]; }
                        if (diff < min_diff) { min_diff = diff; tr = pts[i]; }
                        if (diff > max_diff) { max_diff = diff; bl = pts[i]; }
                    }

                    float widthA = (float)Math.Sqrt(Math.Pow(br.X - bl.X, 2) + Math.Pow(br.Y - bl.Y, 2));
                    float widthB = (float)Math.Sqrt(Math.Pow(tr.X - tl.X, 2) + Math.Pow(tr.Y - tl.Y, 2));
                    int maxWidth = (int)Math.Max(widthA, widthB);
                    float heightA = (float)Math.Sqrt(Math.Pow(tr.X - br.X, 2) + Math.Pow(tr.Y - br.Y, 2));
                    float heightB = (float)Math.Sqrt(Math.Pow(tl.X - bl.X, 2) + Math.Pow(tl.Y - bl.Y, 2));
                    int maxHeight = (int)Math.Max(heightA, heightB);

                    if (maxWidth <= 0 || maxHeight <= 0) continue;

                    Point2f[] srcPts = { tl, tr, br, bl };
                    Point2f[] dstPts = {
                        new Point2f(0, 0),
                        new Point2f(maxWidth - 1, 0),
                        new Point2f(maxWidth - 1, maxHeight - 1),
                        new Point2f(0, maxHeight - 1)
                    };

                    using var perspectiveM = Cv2.GetPerspectiveTransform(srcPts, dstPts);
                    using var cropped = new Mat();
                    Cv2.WarpPerspective(src, cropped, perspectiveM, new OpenCvSharp.Size(maxWidth, maxHeight));

                    if (cropped.Rows > cropped.Cols)
                    {
                        Cv2.Rotate(cropped, cropped, RotateFlags.Rotate90Clockwise);
                    }
                    
                    var bmp = MatToBitmap(cropped);
                    var newItem = new GalleryDetectionItem(det.DetectionId, det.Value ?? "", bmp);
                    newItem.IsCorrectionMode = IsCorrectionMode;
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
