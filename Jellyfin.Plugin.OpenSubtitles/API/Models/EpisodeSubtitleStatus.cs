namespace Jellyfin.Plugin.OpenSubtitles.API.Models;

/// <summary>
/// The outcome of a single-episode subtitle download attempt.
/// </summary>
public enum EpisodeSubtitleStatus
{
    /// <summary>
    /// A subtitle was found and downloaded.
    /// </summary>
    Downloaded,

    /// <summary>
    /// The episode already had an English subtitle, so it was skipped.
    /// </summary>
    AlreadyHasSubtitle,

    /// <summary>
    /// No matching subtitle could be found.
    /// </summary>
    NoMatchFound,

    /// <summary>
    /// The episode has no available media file (e.g. a missing/virtual episode) and was skipped.
    /// </summary>
    MissingEpisode,

    /// <summary>
    /// The daily OpenSubtitles download limit was reached; this and any remaining episodes were skipped.
    /// </summary>
    RateLimited,

    /// <summary>
    /// An unexpected error occurred while searching for or downloading the subtitle.
    /// </summary>
    Error
}
