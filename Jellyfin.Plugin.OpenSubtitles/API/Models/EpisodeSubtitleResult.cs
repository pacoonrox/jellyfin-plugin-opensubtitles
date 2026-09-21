using System;

namespace Jellyfin.Plugin.OpenSubtitles.API.Models;

/// <summary>
/// The result of attempting to download a subtitle for a single episode.
/// </summary>
public class EpisodeSubtitleResult
{
    /// <summary>
    /// Gets or sets the episode id.
    /// </summary>
    public Guid EpisodeId { get; set; }

    /// <summary>
    /// Gets or sets the episode name.
    /// </summary>
    public string? EpisodeName { get; set; }

    /// <summary>
    /// Gets or sets the episode index number.
    /// </summary>
    public int? IndexNumber { get; set; }

    /// <summary>
    /// Gets or sets the outcome of the download attempt.
    /// </summary>
    public EpisodeSubtitleStatus Status { get; set; }

    /// <summary>
    /// Gets or sets the release name of the downloaded subtitle, if any.
    /// </summary>
    public string? SubtitleRelease { get; set; }

    /// <summary>
    /// Gets or sets the download count of the chosen subtitle, if any.
    /// </summary>
    public int? DownloadCount { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the chosen subtitle was a hash-based perfect match.
    /// </summary>
    public bool? IsPerfectMatch { get; set; }

    /// <summary>
    /// Gets or sets the error message, populated when <see cref="Status"/> is <see cref="EpisodeSubtitleStatus.Error"/>.
    /// </summary>
    public string? Error { get; set; }
}
