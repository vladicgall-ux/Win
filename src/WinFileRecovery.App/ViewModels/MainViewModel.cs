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
    private bool _lastScanWasTruncated;

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
        set
        {
            _scanModeIndex = value;
            OnPropertyChanged(nameof(ScanModeIndex));
            OnPropertyChanged(nameof(IsDateFilterUsable));
        }
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

    /// <summary>Whether the date filter can apply at all — deep/signature scans carry no timestamp metadata.</summary>
    public bool IsDateFilterUsable => ScanModeIndex == 0;

    public ObservableCollection<SelectableItem<FileCategory>> FileCategories { get; } = new();

    public RelayCommand RefreshDrivesCommand { get; }
    public RelayCommand ScanCommand { get; }
    public RelayCommand CancelCommand { get; }
    public RelayCommand RecoverSelectedCommand { get; }
    public RelayCommand ClearDateFilterCommand { get; }

    public MainViewModel()
    {
        ScanResultsView = CollectionViewSource.GetDefaultView(ScanResults);
        ScanResultsView.Filter = FilterByDate;

        foreach (var category in FileCategory.All)
            FileCategories.Add(new SelectableItem<FileCategory>(category) { IsSelected = true });

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
            _lastScanWasTruncated = false;
            var found = await Task.Run(() => RunScan(SelectedDrive, progress, _scanCts.Token), _scanCts.Token);
            foreach (var file in found)
                ScanResults.Add(new SelectableItem<RecoverableFile>(file));

            StatusText = ScanResults.Count > 0
                ? $"Найдено файлов для восстановления: {ScanResults.Count}. Отметьте нужные и нажмите «Восстановить»."
                : "Удалённые файлы не найдены. Попробуйте глубокий поиск.";

            if (_lastScanWasTruncated)
            {
                StatusText += " Достигнут лимит результатов — список может быть неполным.";
                MessageBox.Show(
                    $"Поиск остановлен при достижении лимита ({SignatureCarver.DefaultMaxResults:N0} файлов или " +
                    $"{SignatureCarver.DefaultMaxTotalRecoveredSizeBytes / 1024 / 1024 / 1024 / 1024} ТБ суммарного размера).\n\n" +
                    "Это защита от переполнения памяти при большом числе случайных совпадений сигнатур. " +
                    "Сузьте поиск по типу файла или периоду и запустите снова, чтобы найти остальное.",
                    "Список найденных файлов неполный", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
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

        var allowedExtensions = GetAllowedExtensions();
        bool ExtensionAllowed(string ext) => allowedExtensions is null || allowedExtensions.Contains(ext.ToLowerInvariant());

        if (ScanModeIndex == 0)
        {
            // Быстрый путь: пробуем NTFS, затем FAT32 — по сигнатуре загрузочного сектора.
            // Дата/тип применяются сразу при переборе — меньше памяти и не
            // приходится хранить лишние результаты, которые всё равно будут скрыты.
            try
            {
                foreach (var f in new NtfsRecoveryEngine().FindDeletedFiles(disk, progress, token))
                    if (ExtensionAllowed(f.Extension) && MatchesDateFilter(f))
                        results.Add(f);
                return results;
            }
            catch (InvalidDataException)
            {
                // Не NTFS — пробуем FAT32.
            }

            results.Clear();
            try
            {
                foreach (var f in new Fat32RecoveryEngine().FindDeletedFiles(disk, progress, token))
                    if (ExtensionAllowed(f.Extension) && MatchesDateFilter(f))
                        results.Add(f);
                return results;
            }
            catch (InvalidDataException)
            {
                throw new InvalidOperationException(
                    "Не удалось распознать файловую систему (ожидается NTFS или FAT32). Попробуйте глубокий поиск.");
            }
        }

        // Глубокий поиск: сигнатурный carving по всему устройству. Дата
        // недоступна (нет метаданных ФС), но ограничение по типу файла
        // реально ускоряет поиск — меньше сигнатур ищется в каждом окне.
        var signatures = allowedExtensions is null
            ? SignatureCatalog.Default
            : SignatureCatalog.Default.Where(s => allowedExtensions.Contains(s.Extension)).ToList();

        long totalSectors = disk.LengthBytes / disk.SectorSize;
        var carver = new SignatureCarver(signatures);
        results.AddRange(carver.Scan(disk, 0, totalSectors, progress, token));
        _lastScanWasTruncated = carver.WasTruncated;
        return results;
    }

    private HashSet<string>? GetAllowedExtensions()
    {
        var checkedCategories = FileCategories.Where(c => c.IsSelected).ToList();
        if (checkedCategories.Count == FileCategories.Count || checkedCategories.Count == 0)
            return null; // всё выбрано (или ничего не тронуто) — фильтр по типу не сужает поиск

        return checkedCategories.SelectMany(c => c.Value.Extensions).ToHashSet();
    }

    private bool MatchesDateFilter(RecoverableFile file)
    {
        if (DateFrom is null && DateTo is null) return true;

        DateTime? ts = file.EstimatedDeletionUtc?.ToLocalTime();
        if (ts is null) return false;

        if (DateFrom is { } from && ts < from.Date) return false;
        if (DateTo is { } to && ts >= to.Date.AddDays(1)) return false;
        return true;
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

        if (DiskIdentity.IsSamePhysicalDisk(destination, SelectedDrive.Index))
        {
            MessageBox.Show(
                "Нельзя сохранять восстановленные данные на исходный диск.\n\n" +
                "Запись новых файлов на тот же физический диск, с которого выполняется восстановление, " +
                "может навсегда затереть данные удалённых файлов, которые ещё не восстановлены — " +
                "включая те, что вы только что выбрали.\n\n" +
                "Выберите папку на другом физическом накопителе (внешний диск, флешка, другой встроенный SSD/HDD).",
                "Опасный выбор папки", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

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

            int succeeded = log.Count(r => r.Succeeded);
            int failed = log.Count(r => !r.Succeeded);
            int signedValid = log.Count(r => r.SignatureStatus == AuthenticodeStatus.Valid);
            int signedInvalid = log.Count(r => r.SignatureStatus == AuthenticodeStatus.Invalid);

            StatusText = failed > 0
                ? $"Готово: восстановлено {succeeded} из {log.Count} файл(ов) в {destination} ({failed} с ошибкой)"
                : $"Готово: восстановлено {log.Count} файл(ов) в {destination}";

            string summary = $"Восстановлено успешно: {succeeded} из {log.Count}\nКуда: {destination}\n\n" +
                              "Для каждого файла посчитан SHA-256 и проверена цифровая подпись " +
                              "(см. вкладку «Отчёт восстановления» или recovery_report.csv в папке назначения).\n\n" +
                              (failed > 0
                                  ? $"⚠ Не удалось восстановить {failed} файл(ов) — повреждены метаданные или недостаточно места. Причина указана в отчёте.\n"
                                  : "") +
                              (signedInvalid > 0
                                  ? $"⚠ У {signedInvalid} файл(ов) подпись повреждена — возможно, файл восстановлен не полностью.\n"
                                  : "") +
                              "Файлы без подписи — это нормально (документы, фото, видео её обычно не имеют).";

            MessageBox.Show(summary, "Восстановление завершено",
                MessageBoxButton.OK, (failed > 0 || signedInvalid > 0) ? MessageBoxImage.Warning : MessageBoxImage.Information);
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
