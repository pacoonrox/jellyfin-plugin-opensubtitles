using System;

namespace Jellyfin.Plugin.OpenSubtitles.API.Models;

/// <summary>
/// Basic series information used to populate the bulk season subtitle picker.
/// </summary>
public class SeriesInfoDto
{
    /// <summary>
    /// Gets or sets the series id.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the series name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the production year.
    /// </summary>
    public int? ProductionYear { get; set; }
}
