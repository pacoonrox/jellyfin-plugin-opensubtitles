using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mime;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.OpenSubtitles;
using Jellyfin.Plugin.OpenSubtitles.API.Models;
using Jellyfin.Plugin.OpenSubtitles.OpenSubtitlesHandler;
using Jellyfin.Plugin.OpenSubtitles.OpenSubtitlesHandler.Models;
using MediaBrowser.Common.Api;
using MediaBrowser.Common.Extensions;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.Subtitles;
using MediaBrowser.Model.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.OpenSubtitles.API;

/// <summary>
/// The open subtitles plugin controller.
/// </summary>
[ApiController]
[Produces(MediaTypeNames.Application.Json)]
[Authorize(Policy = Policies.SubtitleManagement)]
public class OpenSubtitlesController : ControllerBase
{
    private readonly ILibraryManager _libraryManager;
    private readonly ISubtitleManager _subtitleManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="OpenSubtitlesController"/> class.
    /// </summary>
    /// <param name="libraryManager">Instance of the <see cref="ILibraryManager"/> interface.</param>
    /// <param name="subtitleManager">Instance of the <see cref="ISubtitleManager"/> interface.</param>
    public OpenSubtitlesController(ILibraryManager libraryManager, ISubtitleManager subtitleManager)
    {
        _libraryManager = libraryManager;
        _subtitleManager = subtitleManager;
    }

    /// <summary>
    /// Validates login info.
    /// </summary>
    /// <remarks>
    /// Accepts plugin configuration as JSON body.
    /// </remarks>
    /// <response code="200">Login info valid.</response>
    /// <response code="400">Login info is missing data.</response>
    /// <response code="401">Login info not valid.</response>
    /// <param name="body">The request body.</param>
    /// <returns>
    /// An <see cref="NoContentResult"/> if the login info is valid, a <see cref="BadRequestResult"/> if the request body missing is data
    /// or <see cref="UnauthorizedResult"/> if the login info is not valid.
    /// </returns>
    [HttpPost("Jellyfin.Plugin.OpenSubtitles/ValidateLoginInfo")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult> ValidateLoginInfo([FromBody] LoginInfoInput body)
    {
        var response = await OpenSubtitlesApi.LogInAsync(
            body.Username,
            body.Password,
            CancellationToken.None).ConfigureAwait(false);

        if (!response.Ok)
        {
            var msg = $"{response.Code}{(response.Body.Length < 150 ? $" - {response.Body}" : string.Empty)}";

            if (response.Body.Contains("message\":", StringComparison.Ordinal))
            {
                var err = JsonSerializer.Deserialize<ErrorResponse>(response.Body);
                if (err is not null)
                {
                    msg = string.Equals(err.Message, "You cannot consume this service", StringComparison.Ordinal) ? "Invalid API key provided" : err.Message;
                }
            }

            return Unauthorized(new { Message = msg });
        }

        if (response.Data is not null)
        {
            await OpenSubtitlesApi.LogOutAsync(response.Data, CancellationToken.None).ConfigureAwait(false);
        }

        return Ok(new { Downloads = response.Data?.User?.AllowedDownloads ?? 0 });
    }

    /// <summary>
    /// Gets all series in the library, for use with the bulk season subtitle downloader.
    /// </summary>
    /// <response code="200">The list of series.</response>
    /// <returns>An <see cref="IEnumerable{SeriesInfoDto}"/> containing the series.</returns>
    [HttpGet("Jellyfin.Plugin.OpenSubtitles/Series")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<IEnumerable<SeriesInfoDto>> GetSeries()
    {
        var series = _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = new[] { BaseItemKind.Series },
            Recursive = true
        });

        return Ok(series
            .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .Select(s => new SeriesInfoDto
            {
                Id = s.Id,
                Name = s.Name,
                ProductionYear = s.ProductionYear
            }));
    }

    /// <summary>
    /// Gets all seasons for a series, for use with the bulk season subtitle downloader.
    /// </summary>
    /// <param name="seriesId">The series id.</param>
    /// <response code="200">The list of seasons.</response>
    /// <response code="404">The series could not be found.</response>
    /// <returns>An <see cref="IEnumerable{SeasonInfoDto}"/> containing the seasons, or a <see cref="NotFoundResult"/> if the series does not exist.</returns>
    [HttpGet("Jellyfin.Plugin.OpenSubtitles/Series/{seriesId}/Seasons")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<IEnumerable<SeasonInfoDto>> GetSeasons([FromRoute] Guid seriesId)
    {
        if (_libraryManager.GetItemById<Series>(seriesId) is not Series series)
        {
            return NotFound();
        }

        var seasons = series.GetSeasons(null, new DtoOptions(false))
            .OfType<Season>()
            .OrderBy(s => s.IndexNumber ?? int.MaxValue)
            .Select(s => new SeasonInfoDto
            {
                Id = s.Id,
                Name = s.Name,
                IndexNumber = s.IndexNumber,
                EpisodeCount = s.GetEpisodes().OfType<Episode>().Count(e => !e.IsMissingEpisode)
            });

        return Ok(seasons);
    }

    /// <summary>
    /// Downloads the best-matching English subtitle for every episode in the given seasons.
    /// </summary>
    /// <remarks>
    /// For each episode, an English subtitle is searched for on OpenSubtitles. A hash-based "perfect match" is
    /// preferred; if more than one perfect match is available the one with the most downloads is used. If no
    /// perfect match is available, the non-perfect match with the most downloads is used instead. Episodes that
    /// already have an English subtitle are skipped.
    /// </remarks>
    /// <param name="body">The seasons to download subtitles for.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <response code="200">The download results.</response>
    /// <response code="400">No seasons were specified, or the plugin is not configured.</response>
    /// <returns>The per-season, per-episode download results.</returns>
    [HttpPost("Jellyfin.Plugin.OpenSubtitles/Seasons/DownloadSubtitles")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<IEnumerable<SeasonSubtitleDownloadResult>>> DownloadSeasonSubtitles(
        [FromBody] SeasonSubtitleDownloadRequest body,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        if (body.SeasonIds.Count == 0)
        {
            return BadRequest(new { Message = "No seasons were specified" });
        }

        var downloader = OpenSubtitleDownloader.Instance;
        if (downloader is null)
        {
            return BadRequest(new { Message = "The subtitle downloader has not been initialized" });
        }

        var config = OpenSubtitlesPlugin.Instance?.Configuration;
        if (config is null || string.IsNullOrWhiteSpace(config.Username) || string.IsNullOrWhiteSpace(config.Password))
        {
            return BadRequest(new { Message = "Set up your OpenSubtitles account on the plugin settings page first" });
        }

        var results = new List<SeasonSubtitleDownloadResult>();
        var rateLimited = false;

        foreach (var seasonId in body.SeasonIds)
        {
            if (_libraryManager.GetItemById<Season>(seasonId) is not Season season)
            {
                continue;
            }

            var episodeResults = new List<EpisodeSubtitleResult>();

            var episodes = season.GetEpisodes()
                .OfType<Episode>()
                .OrderBy(e => e.IndexNumber ?? int.MaxValue);

            foreach (var episode in episodes)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var episodeResult = new EpisodeSubtitleResult
                {
                    EpisodeId = episode.Id,
                    EpisodeName = episode.Name,
                    IndexNumber = episode.IndexNumber
                };

                if (episode.IsMissingEpisode || string.IsNullOrEmpty(episode.Path))
                {
                    episodeResult.Status = EpisodeSubtitleStatus.MissingEpisode;
                    episodeResults.Add(episodeResult);
                    continue;
                }

                if (rateLimited)
                {
                    episodeResult.Status = EpisodeSubtitleStatus.RateLimited;
                    episodeResults.Add(episodeResult);
                    continue;
                }

                var hasEnglishSubtitle = episode.GetMediaStreams()
                    .Any(s => s.Type == MediaStreamType.Subtitle && string.Equals(s.Language, "eng", StringComparison.OrdinalIgnoreCase));

                if (hasEnglishSubtitle)
                {
                    episodeResult.Status = EpisodeSubtitleStatus.AlreadyHasSubtitle;
                    episodeResults.Add(episodeResult);
                    continue;
                }

                try
                {
                    var searchRequest = new SubtitleSearchRequest
                    {
                        ContentType = VideoContentType.Episode,
                        Language = "eng",
                        TwoLetterISOLanguageName = "en",
                        MediaPath = episode.Path,
                        Name = episode.Name,
                        SeriesName = episode.SeriesName,
                        IndexNumber = episode.IndexNumber,
                        IndexNumberEnd = episode.IndexNumberEnd,
                        ParentIndexNumber = episode.ParentIndexNumber,
                        ProductionYear = episode.ProductionYear,
                        ProviderIds = episode.ProviderIds,
                        RuntimeTicks = episode.RunTimeTicks,
                        IsPerfectMatch = false,
                        IsAutomated = true
                    };

                    // OpenSubtitleDownloader.Search already orders results by hash match first, then by
                    // download count, so the first candidate is the best "perfect match" available, falling
                    // back to the most-downloaded non-perfect match if no perfect match exists.
                    var candidates = await downloader.Search(searchRequest, cancellationToken).ConfigureAwait(false);
                    var best = candidates.FirstOrDefault();

                    if (best is null)
                    {
                        if (downloader.IsRateLimited)
                        {
                            rateLimited = true;
                            episodeResult.Status = EpisodeSubtitleStatus.RateLimited;
                        }
                        else
                        {
                            episodeResult.Status = EpisodeSubtitleStatus.NoMatchFound;
                        }

                        episodeResults.Add(episodeResult);
                        continue;
                    }

                    var subtitle = await downloader.GetSubtitles(best.Id, cancellationToken).ConfigureAwait(false);
                    await _subtitleManager.UploadSubtitle(episode, subtitle).ConfigureAwait(false);

                    episodeResult.Status = EpisodeSubtitleStatus.Downloaded;
                    episodeResult.SubtitleRelease = best.Name;
                    episodeResult.DownloadCount = best.DownloadCount;
                    episodeResult.IsPerfectMatch = best.IsHashMatch;
                }
                catch (RateLimitExceededException)
                {
                    rateLimited = true;
                    episodeResult.Status = EpisodeSubtitleStatus.RateLimited;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    episodeResult.Status = EpisodeSubtitleStatus.Error;
                    episodeResult.Error = ex.Message;
                }

                episodeResults.Add(episodeResult);
            }

            results.Add(new SeasonSubtitleDownloadResult
            {
                SeasonId = season.Id,
                SeriesName = season.FindSeriesName(),
                SeasonName = season.Name,
                Episodes = episodeResults
            });
        }

        return Ok(results);
    }
}
