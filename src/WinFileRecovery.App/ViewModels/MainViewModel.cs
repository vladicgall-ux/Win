using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Data;
using WinFileRecovery.Core.Carving;
using WinFileRecovery.Core.Disks;
using WinFileRecovery.Core.FileSystems.Fat32;
using WinFileRecovery.Core.FileSystems.Ntfs;
using WinFileRecovery.Core.Native;
using WinFileRecovery.Core.Recovery;
using WinFileRecovery.Core.Verification;

namespace WinFileRecovery.App.ViewModels;

[SupportedOSPlatform("windows")]
public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly RecoveryOrchestrator _orchestrator = new();
    private CancellationTokenSource? _scanCts;

    public ObservableCollection<PhysicalDrive> Drives { get; } = new();
    public ObservableCollection<SelectableItem<RecoverableFile>> ScanResults { get; } = new();
    public ObservableCollection<RecoveredFileResult> RecoveryLog { get; } = new();
    public ICollectionView ScanResultsView { get; }

    private PhysicalDrive? _selectedDrive;
    public PhysicalDrive? SelectedDrive
    {
        get => _selectedDrive;
        set { _selectedDrive = value; OnPropertyChanged(nameof(SelectedDrive)); ScanCommand.RaiseCanExecuteChanged(); }
    }

    // Index-based: 0 = быстрый (по файловой системе), 1 = глубокий (по сигнатурам).
    private int _scanModeIndex;
    public int ScanModeIndex
    {
        get => _scanModeIndex;
        set { _scanModeIndex = value; OnPropertyChanged(nameof(ScanModeIndex)); }
    }

    private double _progress;
    public double Progress
    {
        get => _progress;
        set { _progress = value; OnPropertyChanged(nameof(Progress)); }
    }

    private string _statusText = "Выберите диск и нажмите «Начать поиск».";
    public string StatusText
    {
        get => _statusText;
        set { _statusText = value; OnPropertyChanged(nameof(StatusText)); }
    }

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            _isBusy = value;
            OnPropertyChanged(nameof(IsBusy));
            OnPropertyChanged(nameof(IsIdle));
            ScanCommand.RaiseCanExecuteChanged();
            RecoverSelectedCommand.RaiseCanExecuteChanged();
        }
    }

    public bool IsIdle => !IsBusy;

    public bool HasScanned { get; private set; }
    private void SetHasScanned(bool value)
    {
        HasScanned = value;
        OnPropertyChanged(nameof(HasScanned));
    }

    private bool _selectAll;
    public bool SelectAll
    {
        get => _selectAll;
        set
        {
            _selectAll = value;
            OnPropertyChanged(nameof(SelectAll));
            // Only touches items currently passing the date filter — a
            // hidden item shouldn't silently get (de)selected underneath the user.
            foreach (var item in ScanResultsView.Cast<SelectableItem<RecoverableFile>>())
                item.IsSelected = value;
        }
    }

    private DateTime? _dateFrom;
    public DateTime? DateFrom
    {
        get => _dateFrom;
        set
        {
            _dateFrom = value;
            OnPropertyChanged(nameof(DateFrom));
            OnPropertyChanged(nameof(IsDateFilterActive));
            ScanResultsView.Refresh();
        }
    }

    private DateTime? _dateTo;
    public DateTime? DateTo
    {
        get => _dateTo;
        set
        {
            _dateTo = value;
            OnPropertyChanged(nameof(DateTo));
            OnPropertyChanged(nameof(IsDateFilterActive));
            ScanResultsView.Refresh();
        }
    }

    public bool IsDateFilterActive => DateFrom is not null || DateTo is not null;

    public RelayCommand RefreshDrivesCommand { get; }
    public RelayCommand ScanCommand { get; }
    public RelayCommand CancelCommand { get; }
    public RelayCommand RecoverSelectedCommand { get; }
    public RelayCommand ClearDateFilterCommand { get; }

    public MainViewModel()
    {
        ScanResultsView = CollectionViewSource.GetDefaultView(ScanResults);
        ScanResultsView.Filter = FilterByDate;

        RefreshDrivesCommand = new RelayCommand(RefreshDrivesAsync);
        ScanCommand = new RelayCommand(ScanAsync, () => !IsBusy && SelectedDrive is not null);
        CancelCommand = new RelayCommand(CancelAsync, () => IsBusy);
        RecoverSelectedCommand = new RelayCommand(RecoverSelectedAsync, () => !IsBusy);
        ClearDateFilterCommand = new RelayCommand(() => { DateFrom = null; DateTo = null; return Task.CompletedTask; });

        _ = RefreshDrivesAsync();
    }

    /// <summary>
    /// A file with no known timestamp (always true for deep/signature-carved
    /// results) is excluded once any date filter is set — we can't confirm
    /// it falls in range, so it's safer to hide it than to wrongly include it.
    /// </summary>
    private bool FilterByDate(object obj)
    {
        if (DateFrom is null && DateTo is null) return true;
        if (obj is not SelectableItem<RecoverableFile> item) return true;

        DateTime? ts = item.Value.EstimatedDeletionUtc?.ToLocalTime();
        if (ts is null) return false;

        if (DateFrom is { } from && ts < from.Date) return false;
        if (DateTo is { } to && ts >= to.Date.AddDays(1)) return false;
        return true;
    }

    private Task RefreshDrivesAsync()
    {
        Drives.Clear();
        foreach (var drive in DriveEnumerator.ListPhysicalDrives())
            Drives.Add(drive);

        StatusText = Drives.Count > 0
            ? $"Найдено дисков: {Drives.Count}. Выберите диск и нажмите «Начать поиск»."
            : "Диски не найдены. Убедитесь, что приложение запущено от имени администратора.";
        return Task.CompletedTask;
    }

    private async Task ScanAsync()
    {
        if (SelectedDrive is null) return;

        ScanResults.Clear();
        RecoveryLog.Clear();
        SetHasScanned(false);
        _scanCts = new CancellationTokenSource();
        IsBusy = true;
        Progress = 0;
        StatusText = "Идёт поиск удалённых файлов, это может занять время...";

        var progress = new Progress<double>(p => Progress = p * 100.0);

        try
        {
            var found = await Task.Run(() => RunScan(SelectedDrive, progress, _scanCts.Token), _scanCts.Token);
            foreach (var file in found)
                ScanResults.Add(new SelectableItem<RecoverableFile>(file));

            StatusText = ScanResults.Count > 0
                ? $"Найдено файлов для восстановления: {ScanResults.Count}. Отметьте нужные и нажмите «Восстановить»."
                : "Удалённые файлы не найдены. Попробуйте глубокий поиск.";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Поиск остановлен.";
        }
        catch (Exception ex)
        {
            StatusText = "Не удалось выполнить поиск — см. окно ошибки.";
            MessageBox.Show(FriendlyError(ex), "Ошибка поиска", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
            SetHasScanned(true);
        }
    }

    private List<RecoverableFile> RunScan(PhysicalDrive drive, IProgress<double> progress, CancellationToken token)
    {
        using var disk = RawDisk.Open(drive.DevicePath);
        var results = new List<RecoverableFile>();

        if (ScanModeIndex == 0)
        {
            // Быстрый путь: пробуем NTFS, затем FAT32 — по сигнатуре загрузочного сектора.
            try
            {
                results.AddRange(new NtfsRecoveryEngine().FindDeletedFiles(disk, progress, token));
                return results;
            }
            catch (InvalidDataException)
            {
                // Не NTFS — пробуем FAT32.
            }

            try
            {
                results.AddRange(new Fat32RecoveryEngine().FindDeletedFiles(disk, progress, token));
                return results;
            }
            catch (InvalidDataException)
            {
                throw new InvalidOperationException(
                    "Не удалось распознать файловую систему (ожидается NTFS или FAT32). Попробуйте глубокий поиск.");
            }
        }

        // Глубокий поиск: сигнатурный carving по всему устройству.
        long totalSectors = disk.LengthBytes / disk.SectorSize;
        results.AddRange(new SignatureCarver().Scan(disk, 0, totalSectors, progress, token));
        return results;
    }

    private Task CancelAsync()
    {
        _scanCts?.Cancel();
        return Task.CompletedTask;
    }

    private async Task RecoverSelectedAsync()
    {
        if (SelectedDrive is null) return;

        var toRecover = ScanResults.Where(r => r.IsSelected).Select(r => r.Value).ToList();
        if (toRecover.Count == 0)
        {
            MessageBox.Show("Сначала отметьте галочками файлы, которые нужно восстановить.",
                "Ничего не выбрано", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Куда сохранить восстановленные файлы? Выберите ДРУГОЙ диск."
        };
        if (dialog.ShowDialog() != true) return;

        string destination = dialog.FolderName;

        IsBusy = true;
        StatusText = "Восстановление файлов...";
        var progress = new Progress<(int done, int total)>(p => StatusText = $"Восстановлено {p.done} из {p.total}...");

        try
        {
            List<RecoveredFileResult> log = await Task.Run(() =>
            {
                using var disk = RawDisk.Open(SelectedDrive.DevicePath);
                return _orchestrator.RecoverMany(disk, toRecover, destination, progress);
            });

            RecoveryLog.Clear();
            foreach (var entry in log)
                RecoveryLog.Add(entry);

            int signedValid = log.Count(r => r.SignatureStatus == AuthenticodeStatus.Valid);
            int signedInvalid = log.Count(r => r.SignatureStatus == AuthenticodeStatus.Invalid);

            StatusText = $"Готово: восстановлено {log.Count} файл(ов) в {destination}";

            string summary = $"Восстановлено {log.Count} файл(ов) в:\n{destination}\n\n" +
                              "Для каждого файла посчитан SHA-256 и проверена цифровая подпись " +
                              "(см. вкладку «Отчёт восстановления» или recovery_report.csv в папке назначения).\n\n" +
                              (signedInvalid > 0
                                  ? $"⚠ У {signedInvalid} файл(ов) подпись повреждена — возможно, файл восстановлен не полностью.\n"
                                  : "") +
                              "Файлы без подписи — это нормально (документы, фото, видео её обычно не имеют).";

            MessageBox.Show(summary, "Восстановление завершено",
                MessageBoxButton.OK, signedInvalid > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            StatusText = "Не удалось восстановить файлы — см. окно ошибки.";
            MessageBox.Show(FriendlyError(ex), "Ошибка восстановления", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string FriendlyError(Exception ex) =>
        ex is System.ComponentModel.Win32Exception
            ? ex.Message
            : $"{ex.Message}\n\nЕсли ошибка про доступ — перезапустите приложение от имени администратора.";

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
