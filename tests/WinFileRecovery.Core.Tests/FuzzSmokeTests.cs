using WinFileRecovery.Core.FileSystems.Fat32;
using WinFileRecovery.Core.FileSystems.Ntfs;
using Xunit;

namespace WinFileRecovery.Core.Tests;

/// <summary>
/// Not a real fuzzer (no coverage-guided input generation, no corpus) —
/// a lightweight, deterministic smoke test that feeds a large number of
/// random/garbage byte buffers through every parser that consumes
/// untrusted on-disk data, and asserts that none of them ever throws
/// anything other than the deliberate "this data is corrupt" exceptions
/// (InvalidDataException). Anything else escaping — IndexOutOfRange,
/// NullReference, OverflowException, StackOverflow, an infinite loop —
/// is exactly the class of bug the security hardening pass closed, and
/// this test exists to catch a regression of it.
/// </summary>
public class FuzzSmokeTests
{
    private const int Iterations = 2000;

    [Fact]
    public void NtfsBootSector_Parse_NeverThrowsUnexpectedException()
    {
        var random = new Random(12345); // fixed seed: deterministic, reproducible failures

        for (int i = 0; i < Iterations; i++)
        {
            byte[] buffer = new byte[512];
            random.NextBytes(buffer);

            try
            {
                NtfsBootSector.Parse(buffer);
            }
            catch (InvalidDataException)
            {
                // expected outcome for garbage input
            }
        }
    }

    [Fact]
    public void Fat32BootSector_Parse_NeverThrowsUnexpectedException()
    {
        var random = new Random(23456);

        for (int i = 0; i < Iterations; i++)
        {
            byte[] buffer = new byte[512];
            random.NextBytes(buffer);

            try
            {
                Fat32BootSector.Parse(buffer);
            }
            catch (InvalidDataException)
            {
            }
        }
    }

    [Fact]
    public void DataRunParser_Parse_NeverThrows()
    {
        var random = new Random(34567);

        for (int i = 0; i < Iterations; i++)
        {
            byte[] buffer = new byte[random.Next(0, 64)];
            random.NextBytes(buffer);

            // DataRunParser has no "expected" exception path at all — any
            // input, however malformed, must come back as a (possibly
            // empty) list, never throw.
            DataRunParser.Parse(buffer, random.Next(-5, buffer.Length + 5), random.Next(-5, buffer.Length + 5));
        }
    }

    [Fact]
    public void MftRecord_Parse_NeverThrowsUnexpectedException()
    {
        var random = new Random(45678);

        for (int i = 0; i < Iterations; i++)
        {
            byte[] buffer = new byte[1024];
            random.NextBytes(buffer);
            // Force the "FILE" signature so the parser proceeds past the
            // initial check into the attribute-walking logic being fuzzed.
            buffer[0] = (byte)'F';
            buffer[1] = (byte)'I';
            buffer[2] = (byte)'L';
            buffer[3] = (byte)'E';

            // MftRecord.Parse has no legitimate exception path — corrupt
            // attributes must degrade to null/best-effort fields, not throw.
            MftRecord.Parse(buffer, recordIndex: 0, bytesPerSector: 512, maxCluster: 1_000_000);
        }
    }
}
