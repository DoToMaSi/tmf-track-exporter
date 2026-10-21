namespace TmfTrackConverter.Export;

public sealed record ExportProgress(
    string Status,
    int Current,
    int Total,
    string? Detail = null)
{
    public double Fraction => Total <= 0 ? 0 : (double)Current / Total;
}
