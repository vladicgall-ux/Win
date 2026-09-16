namespace WinFileRecovery.Core.Disks;

public sealed record PhysicalDrive(
    string DevicePath,
    int Index,
    string Model,
    long SizeBytes,
    string MediaType);

public sealed record Volume(
    string DevicePath,
    string DriveLetter,
    string FileSystem,
    long SizeBytes,
    string Label);
