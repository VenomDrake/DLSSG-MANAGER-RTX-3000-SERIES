namespace DlssgManager.TestFixtures;

/// <summary>
/// This test-only assembly supplies a PE file version and x64 machine header.
/// It is not NVIDIA software, exports no NGX entry points, and is never loaded into a game.
/// </summary>
public static class SyntheticRuntime
{
    public const string Purpose = "Synthetic file metadata for read-only scanner tests";
}
