namespace Gwendoline;

internal static class Paths
{
    /// <summary>%LOCALAPPDATA%\Gwendoline: state.json and telegram.json, for this PC only, never roamed.</summary>
    public static string Data { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Gwendoline");
}
