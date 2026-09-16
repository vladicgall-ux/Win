using System.Runtime.InteropServices;

namespace WinFileRecovery.Core.Native;

/// <summary>
/// Unmanaged buffer whose usable pointer is aligned to a given boundary,
/// as required by FILE_FLAG_NO_BUFFERING reads/writes. Tracks the original
/// (unaligned) allocation so it can be freed correctly.
/// </summary>
internal sealed class AlignedBuffer : IDisposable
{
    private IntPtr _base;
    public IntPtr Pointer { get; private set; }
    public int Size { get; }

    public AlignedBuffer(int size, int alignment)
    {
        Size = size;
        _base = Marshal.AllocHGlobal(size + alignment);
        long addr = _base.ToInt64();
        long alignedAddr = (addr + alignment - 1) & ~((long)alignment - 1);
        Pointer = new IntPtr(alignedAddr);
    }

    public byte[] ToManagedArray(int length)
    {
        var result = new byte[length];
        Marshal.Copy(Pointer, result, 0, length);
        return result;
    }

    public void Dispose()
    {
        if (_base != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_base);
            _base = IntPtr.Zero;
            Pointer = IntPtr.Zero;
        }
    }
}
