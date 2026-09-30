namespace AI_Supporter.Tests;

internal static class TestHelper
{
    public static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"ai_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static void CleanupTempDir(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
        catch { /* best effort */ }
    }
}
