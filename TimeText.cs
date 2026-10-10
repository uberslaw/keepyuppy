namespace KeepyUppy;

internal static class TimeText
{
    public static string Hms(TimeSpan value)
    {
        if (value < TimeSpan.Zero)
            value = TimeSpan.Zero;

        var hours = (int)value.TotalHours;
        if (hours > 99)
            hours = 99;

        return $"{hours:00}:{value.Minutes:00}:{value.Seconds:00}";
    }
}
