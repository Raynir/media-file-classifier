namespace MediaFileClassifier.Domain.Metadata;

/// <summary>
/// GPS position where a photo or video was taken, in WGS 84 degrees.
/// </summary>
public sealed record GeoLocation
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GeoLocation"/> class.
    /// </summary>
    /// <param name="latitude">Latitude, from -90 to 90.</param>
    /// <param name="longitude">Longitude, from -180 to 180.</param>
    /// <param name="altitudeMeters">Altitude above sea level, if known.</param>
    public GeoLocation(double latitude, double longitude, double? altitudeMeters = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(latitude, -90);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(latitude, 90);
        ArgumentOutOfRangeException.ThrowIfLessThan(longitude, -180);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(longitude, 180);

        Latitude = latitude;
        Longitude = longitude;
        AltitudeMeters = altitudeMeters;
    }

    public double Latitude { get; }

    public double Longitude { get; }

    public double? AltitudeMeters { get; }
}
