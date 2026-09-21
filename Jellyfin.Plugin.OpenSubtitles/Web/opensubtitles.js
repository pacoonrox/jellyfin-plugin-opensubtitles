const OpenSubtitlesConfig = {
    pluginUniqueId: '4b9ed42f-5185-48b5-9803-6ff2989014c4'
};

const EpisodeStatusLabels = {
    Downloaded: 'Downloaded',
    AlreadyHasSubtitle: 'Already had an English subtitle',
    NoMatchFound: 'No match found',
    MissingEpisode: 'Missing episode file, skipped',
    RateLimited: 'Skipped - daily download limit reached',
    Error: 'Error'
};

function escapeHtml(value) {
    return String(value == null ? '' : value).replace(/[&<>"']/g, function (c) {
        return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c];
    });
}

function getJson(url) {
    return ApiClient.ajax({ type: 'GET', url }).then(function (response) {
        return response.json();
    });
}

export default function (view, params) {
    let credentialsWarning;

    view.addEventListener('viewshow', function () {
        Dashboard.showLoadingMsg();
        const page = this;
        credentialsWarning = page.querySelector("#expiredCredentialsWarning");

        ApiClient.getPluginConfiguration(OpenSubtitlesConfig.pluginUniqueId).then(function (config) {
            page.querySelector('#username').value = config.Username || '';
            page.querySelector('#password').value = config.Password || '';
            if (config.CredentialsInvalid) {
                credentialsWarning.style.display = null;
            }
            Dashboard.hideLoadingMsg();
        }).catch(function () {
            Dashboard.hideLoadingMsg();
            Dashboard.processErrorResponse({ statusText: "Failed to load plugin configuration" });
        });

        const seriesSelect = page.querySelector('#bulkSeriesSelect');
        seriesSelect.innerHTML = '<option value="">Loading series...</option>';
        getJson(ApiClient.getUrl('Jellyfin.Plugin.OpenSubtitles/Series')).then(function (series) {
            seriesSelect.innerHTML = '<option value="">Select a series...</option>' +
                series.map(function (s) {
                    const year = s.ProductionYear ? ` (${s.ProductionYear})` : '';
                    return `<option value="${escapeHtml(s.Id)}">${escapeHtml(s.Name + year)}</option>`;
                }).join('');
        }).catch(function () {
            seriesSelect.innerHTML = '<option value="">Failed to load series</option>';
        });
    });

    view.querySelector('#OpenSubtitlesConfigForm').addEventListener('submit', function (e) {
        e.preventDefault();
        Dashboard.showLoadingMsg();
        const form = this;
        ApiClient.getPluginConfiguration(OpenSubtitlesConfig.pluginUniqueId).then(function (config) {
            const username = form.querySelector('#username').value.trim();
            const password = form.querySelector('#password').value.trim();

            if (!username || !password) {
                Dashboard.hideLoadingMsg();
                Dashboard.processErrorResponse({statusText: "Account info is incomplete"});
                return;
            }

            const el = form.querySelector('#ossresponse');
            const saveButton = form.querySelector('#save-button');

            const data = JSON.stringify({ Username: username, Password: password });
            const url = ApiClient.getUrl('Jellyfin.Plugin.OpenSubtitles/ValidateLoginInfo');

            const handler = response => response.json().then(res => {
                saveButton.disabled = false;
                Dashboard.hideLoadingMsg();

                if (response.ok) {
                    el.innerText = `Login info validated, this account can download ${res.Downloads} subtitles per day`;

                    config.Username = username;
                    config.Password = password;
                    config.CredentialsInvalid = false;

                    ApiClient.updatePluginConfiguration(OpenSubtitlesConfig.pluginUniqueId, config).then(function (result) {
                        credentialsWarning.style.display = 'none';
                        Dashboard.processPluginConfigurationUpdateResult(result);
                    }).catch(function () {
                        Dashboard.processErrorResponse({ statusText: "Failed to update plugin configuration" });
                    });
                } else {
                    let msg = res.Message ?? JSON.stringify(res, null, 2);

                    if (msg == 'You cannot consume this service') {
                        msg = 'Invalid API key provided';
                    }

                    Dashboard.processErrorResponse({statusText: `Request failed - ${msg}`});
                }
            }).catch(function () {
                saveButton.disabled = false;
                Dashboard.hideLoadingMsg();
                Dashboard.processErrorResponse({ statusText: "Request failed. Please check your network or server." });
            });

            saveButton.disabled = true;
            ApiClient.ajax({ type: 'POST', url, data, contentType: 'application/json'}).then(handler).catch(handler);

        }).catch(function () {
            Dashboard.hideLoadingMsg();
            Dashboard.processErrorResponse({ statusText: "Failed to load plugin configuration" });
        });
        return false;
    });

    view.querySelector('#bulkSeriesSelect').addEventListener('change', function () {
        const page = view;
        const seriesId = this.value;
        const section = page.querySelector('#bulkSeasonsSection');
        const list = page.querySelector('#bulkSeasonsList');

        page.querySelector('#bulkResults').innerHTML = '';
        list.innerHTML = '';
        section.style.display = 'none';

        if (!seriesId) {
            return;
        }

        list.innerHTML = '<p>Loading seasons...</p>';
        section.style.display = 'block';

        getJson(ApiClient.getUrl(`Jellyfin.Plugin.OpenSubtitles/Series/${seriesId}/Seasons`)).then(function (seasons) {
            if (seasons.length === 0) {
                list.innerHTML = '<p>This series has no seasons.</p>';
                return;
            }

            list.innerHTML = seasons.map(function (s) {
                const episodeLabel = s.EpisodeCount === 1 ? '1 episode' : `${s.EpisodeCount} episodes`;
                return '<label class="checkboxContainer">' +
                    `<input is="emby-checkbox" type="checkbox" class="bulkSeasonCheckbox" data-seasonid="${escapeHtml(s.Id)}" checked />` +
                    `<span>${escapeHtml(s.Name)} (${episodeLabel})</span>` +
                    '</label>';
            }).join('');
        }).catch(function () {
            list.innerHTML = '<p>Failed to load seasons.</p>';
        });
    });

    view.querySelector('#bulkSelectAll').addEventListener('click', function (e) {
        e.preventDefault();
        view.querySelectorAll('.bulkSeasonCheckbox').forEach(function (cb) {
            cb.checked = true;
        });
    });

    view.querySelector('#bulkSelectNone').addEventListener('click', function (e) {
        e.preventDefault();
        view.querySelectorAll('.bulkSeasonCheckbox').forEach(function (cb) {
            cb.checked = false;
        });
    });

    view.querySelector('#OpenSubtitlesBulkSeasonForm').addEventListener('submit', function (e) {
        e.preventDefault();

        const page = view;
        const resultsEl = page.querySelector('#bulkResults');
        const seasonIds = Array.from(page.querySelectorAll('.bulkSeasonCheckbox:checked')).map(function (cb) {
            return cb.getAttribute('data-seasonid');
        });

        if (seasonIds.length === 0) {
            resultsEl.innerText = 'Select at least one season first.';
            return false;
        }

        const downloadButton = page.querySelector('#bulkDownloadButton');
        downloadButton.disabled = true;
        resultsEl.innerText = 'Downloading subtitles, this may take a while...';
        Dashboard.showLoadingMsg();

        const url = ApiClient.getUrl('Jellyfin.Plugin.OpenSubtitles/Seasons/DownloadSubtitles');
        const data = JSON.stringify({ SeasonIds: seasonIds });

        ApiClient.ajax({ type: 'POST', url, data, contentType: 'application/json' }).then(function (response) {
            return response.json().then(function (body) {
                return { ok: response.ok, body: body };
            });
        }).then(function (result) {
            downloadButton.disabled = false;
            Dashboard.hideLoadingMsg();

            if (!result.ok) {
                resultsEl.innerText = `Request failed - ${result.body && result.body.Message ? result.body.Message : JSON.stringify(result.body)}`;
                return;
            }

            renderBulkResults(resultsEl, result.body);
        }).catch(function () {
            downloadButton.disabled = false;
            Dashboard.hideLoadingMsg();
            resultsEl.innerText = 'Request failed. Please check your network or server.';
        });

        return false;
    });
}

function renderBulkResults(container, seasonResults) {
    let downloaded = 0;
    let total = 0;

    const seasonsHtml = seasonResults.map(function (season) {
        const episodesHtml = season.Episodes.map(function (ep) {
            total++;
            if (ep.Status === 'Downloaded') {
                downloaded++;
            }

            let detail = EpisodeStatusLabels[ep.Status] || ep.Status;
            if (ep.Status === 'Downloaded' && ep.SubtitleRelease) {
                const downloads = ep.DownloadCount != null ? `, ${ep.DownloadCount} downloads` : '';
                const perfect = ep.IsPerfectMatch ? ', perfect match' : '';
                detail += ` - ${escapeHtml(ep.SubtitleRelease)}${downloads}${perfect}`;
            } else if (ep.Status === 'Error' && ep.Error) {
                detail += `: ${escapeHtml(ep.Error)}`;
            }

            const epNumber = ep.IndexNumber != null ? `Episode ${ep.IndexNumber}` : 'Episode';
            const epName = ep.EpisodeName ? ` - ${escapeHtml(ep.EpisodeName)}` : '';
            return `<div>${escapeHtml(epNumber)}${epName}: ${detail}</div>`;
        }).join('');

        return `<h4>${escapeHtml(season.SeriesName)} - ${escapeHtml(season.SeasonName)}</h4>${episodesHtml}`;
    }).join('');

    container.innerHTML = `<p><b>${downloaded} of ${total} episode(s) downloaded</b></p>${seasonsHtml}`;
}
