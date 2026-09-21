using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.OpenSubtitles.API.Models;

/// <summary>
/// The result of downloading subtitles for every episode of a season.
/// </summary>
public class SeasonSubtitleDownloadResult
{
    /// <summary>
    /// Gets or sets the season id.
    /// </summary>
    public Guid SeasonId { get; set; }

    /// <summary>
    /// Gets or sets the series name.
    /// </summary>
    public string? SeriesName { get; set; }

    /// <summary>
    /// Gets or sets the season name.
    /// </summary>
    public string? SeasonName { get; set; }

    /// <summary>
    /// Gets or sets the per-episode results.
    /// </summary>
    public IReadOnlyList<EpisodeSubtitleResult> Episodes { get; set; } = Array.Empty<EpisodeSubtitleResult>();
}
