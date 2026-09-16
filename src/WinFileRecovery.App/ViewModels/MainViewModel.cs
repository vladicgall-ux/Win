using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.Versioning;
using System.Windows;
using WinFileRecovery.Core.Carving;
using WinFileRecovery.Core.Disks;
using WinFileRecovery.Core.FileSystems.Fat32;
using WinFileRecovery.Core.FileSystems.Ntfs;
using WinFileRecovery.Core.Native;
using WinFileRecovery.Core.Recovery;

namespace WinFileRecovery.App.ViewModels;

[SupportedOSPlatform("windows")]
public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly RecoveryOrchestrator _orchestrator = new();
    private CancellationTokenSource? _scanCts;

    public ObservableCollection<PhysicalDrive> Drives { get; } = new();
    public ObservableCollection<RecoverableFile> Results { get; } = new();
    public ObservableCollection<RecoverableFile> SelectedResults { get; } = new();

    private PhysicalDrive? _selectedDrive;
    public PhysicalDrive? SelectedDrive
    {
        get => _selectedDrive;
        set { _selectedDrive = value; OnPropertyChanged(nameof(SelectedDrive)); }
    }

    public string[] ScanModes { get; } = { "Быстрое сканирование (NTFS/FAT32 MFT)", "Глубокое сканирование (сигнатуры)" };

    private string _selectedScanMode;
    public string SelectedScanMode
    {
        get => _selectedScanMode;
        set { _selectedScanMode = value; OnPropertyChanged(nameof(SelectedScanMode)); }
    }

    private double _progress;
    public double Progress
    {
        get => _progress;
        set { _progress = value; OnPropertyChanged(nameof(Progress)); }
    }

    private string _statusText = "Готово.";
    public string StatusText
    {
        get => _statusText;
        set { _statusText = value; OnPropertyChanged(nameof(StatusText)); }
    }

    private bool _isScanning;
    public bool IsScanning
    {
        get => _isScanning;
        set { _isScanning = value; OnPropertyChanged(nameof(IsScanning)); ScanCommand.RaiseCanExecuteChanged(); }
    }

    public RelayCommand RefreshDrivesCommand { get; }
    public RelayCommand ScanCommand { get; }
    public RelayCommand CancelScanCommand { get; }
    public RelayCommand RecoverSelectedCommand { get; }

    public MainViewModel()
    {
        _selectedScanMode = ScanModes[0];

        RefreshDrivesCommand = new RelayCommand(RefreshDrivesAsync);
        ScanCommand = new RelayCommand(ScanAsync, () => !IsScanning && SelectedDrive is not null);
        CancelScanCommand = new RelayCommand(CancelScanAsync, () => IsScanning);
        RecoverSelectedCommand = new RelayCommand(RecoverSelectedAsync, () => SelectedResults.Count > 0);

        _ = RefreshDrivesAsync();
    }

    private Task RefreshDrivesAsync()
    {
        Drives.Clear();
        foreach (var drive in DriveEnumerator.ListPhysicalDrives())
            Drives.Add(drive);
        StatusText = $"Найдено дисков: {Drives.Count}";
        return Task.CompletedTask;
    }

    private async Task ScanAsync()
    {
        if (SelectedDrive is null) return;

        Results.Clear();
        _scanCts = new CancellationTokenSource();
        IsScanning = true;
        Progress = 0;
        StatusText = "Сканирование...";

        var progress = new Progress<double>(p => Progress = p * 100.0);

        try
        {
            var found = await Task.Run(() => RunScan(SelectedDrive, progress, _scanCts.Token), _scanCts.Token);
            foreach (var file in found)
                Results.Add(file);

            StatusText = $"Готово. Найдено файлов: {Results.Count}";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Сканирование отменено.";
        }
        catch (Exception ex)
        {
            StatusText = $"Ошибка: {ex.Message}";
            MessageBox.Show(ex.Message, "Ошибка сканирования", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsScanning = false;
        }
    }

    private List<RecoverableFile> RunScan(PhysicalDrive drive, IProgress<double> progress, CancellationToken token)
    {
        using var disk = RawDisk.Open(drive.DevicePath);
        var results = new List<RecoverableFile>();

        if (SelectedScanMode == ScanModes[0])
        {
            // Fast path: try NTFS first, then FAT32; whichever boot sector
            // signature matches determines the filesystem.
            try
            {
                results.AddRange(new NtfsRecoveryEngine().FindDeletedFiles(disk, progress, token));
                return results;
            }
            catch (InvalidDataException)
            {
                // Not NTFS — fall through to FAT32.
            }

            try
            {
                results.AddRange(new Fat32RecoveryEngine().FindDeletedFiles(disk, progress, token));
                return results;
            }
            catch (InvalidDataException)
            {
                throw new InvalidOperationException(
                    "Не удалось распознать файловую систему (ожидается NTFS или FAT32). Попробуйте глубокое сканирование.");
            }
        }

        // Deep scan: signature carving across the whole device.
        long totalSectors = disk.LengthBytes / disk.SectorSize;
        results.AddRange(new SignatureCarver().Scan(disk, 0, totalSectors, progress, token));
        return results;
    }

    private Task CancelScanAsync()
    {
        _scanCts?.Cancel();
        return Task.CompletedTask;
    }

    private async Task RecoverSelectedAsync()
    {
        if (SelectedDrive is null || SelectedResults.Count == 0) return;

        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Выберите папку для восстановленных файлов (на ДРУГОМ диске)."
        };
        if (dialog.ShowDialog() != true) return;

        string destination = dialog.FolderName;
        var toRecover = SelectedResults.ToList();

        IsScanning = true;
        StatusText = "Восстановление...";
        var progress = new Progress<(int done, int total)>(p => StatusText = $"Восстановлено {p.done}/{p.total}");

        try
        {
            await Task.Run(() =>
            {
                using var disk = RawDisk.Open(SelectedDrive.DevicePath);
                _orchestrator.RecoverMany(disk, toRecover, destination, progress);
            });

            StatusText = $"Восстановлено файлов: {toRecover.Count} -> {destination}";
            MessageBox.Show($"Восстановлено {toRecover.Count} файл(ов) в {destination}", "Готово",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            StatusText = $"Ошибка восстановления: {ex.Message}";
            MessageBox.Show(ex.Message, "Ошибка восстановления", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsScanning = false;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
