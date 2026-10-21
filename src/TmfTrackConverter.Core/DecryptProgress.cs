namespace TmfTrackConverter.Core;

public sealed record DecryptProgress(
    string Status,
    int Current,
    int Total,
    string? CurrentFile = null)
{
    public double Fraction => Total <= 0 ? 0 : (double)Current / Total;
}
