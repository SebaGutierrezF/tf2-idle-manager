namespace TF2IdleManager;

/// <summary>Coordinates for the four corners of the monitor hosting the manager.</summary>
public static class ToastPlacement
{
    public const string DefaultCorner = "BottomLeft";
    public static readonly (string Value, string Label)[] Choices =
    {
        ("TopLeft", "Top left"),
        ("TopRight", "Top right"),
        ("BottomLeft", "Bottom left"),
        ("BottomRight", "Bottom right")
    };

    public static string Normalize(string? corner) =>
        Choices.FirstOrDefault(c => string.Equals(c.Value, corner, StringComparison.OrdinalIgnoreCase)).Value
        ?? DefaultCorner;

    // Always opt out of WinForms' automatic initial placement. If a toast is
    // positioned before Show() with the default WindowsDefaultLocation,
    // the first native ShowWindow can silently overwrite that location.
    public static Point Apply(Form toast, Rectangle area, string? corner, int stackedOffset = 0)
    {
        toast.StartPosition = FormStartPosition.Manual;
        var location = Position(area, toast.Size, corner, stackedOffset);
        toast.Location = location;
        return location;
    }

    public static Point Position(Rectangle area, Size size, string? corner, int stackedOffset = 0)
    {
        var normalized = Normalize(corner);
        const int edge = 16;
        var left = normalized.EndsWith("Left", StringComparison.Ordinal);
        var upper = normalized.StartsWith("Top", StringComparison.Ordinal);
        var x = left ? area.Left + edge : area.Right - size.Width - edge;
        var y = upper ? area.Top + edge + stackedOffset : area.Bottom - size.Height - edge - stackedOffset;
        return new Point(
            Math.Clamp(x, area.Left, Math.Max(area.Left, area.Right - size.Width)),
            Math.Clamp(y, area.Top, Math.Max(area.Top, area.Bottom - size.Height)));
    }
}
