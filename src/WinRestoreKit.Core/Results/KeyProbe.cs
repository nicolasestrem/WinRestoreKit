namespace WinRestoreKit
{
    /// <summary>
    /// The outcome of looking for a registry key. See <see cref="Utils.ProbeKey"/> for why the
    /// third state matters.
    /// </summary>
    public enum KeyProbe
    {
        Present,
        Absent,
        Indeterminate
    }
}
