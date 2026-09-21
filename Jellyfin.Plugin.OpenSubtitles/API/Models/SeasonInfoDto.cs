using System;

namespace Jellyfin.Plugin.OpenSubtitles.API.Models;

/// <summary>
/// Basic season information used to populate the bulk season subtitle picker.
/// </summary>
public class SeasonInfoDto
{
    /// <summary>
    /// Gets or sets the season id.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the season name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the season index number.
    /// </summary>
    public int? IndexNumber { get; set; }

    /// <summary>
    /// Gets or sets the number of non-missing episodes in the season.
    /// </summary>
    public int EpisodeCount { get; set; }
}
