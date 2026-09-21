using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.OpenSubtitles.API.Models;

/// <summary>
/// Request body for downloading English subtitles for every episode of one or more seasons.
/// </summary>
public class SeasonSubtitleDownloadRequest
{
    /// <summary>
    /// Gets or sets the ids of the seasons to download subtitles for.
    /// </summary>
    public IReadOnlyList<Guid> SeasonIds { get; set; } = Array.Empty<Guid>();
}
