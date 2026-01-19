using System.Text.RegularExpressions;
using jihub.Base;
using jihub.Github.Models;
using jihub.Github.Services;
using jihub.Jira;
using jihub.Jira.DependencyInjection;
using jihub.Jira.Models;
using jihub.Parsers.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace jihub.Parsers.Jira;

public class JiraParser : IJiraParser
{
    private readonly Regex _regex = new(@"!(.+?)!", RegexOptions.Compiled, TimeSpan.FromSeconds(5));
    private readonly Regex _linkRegex = new(@"\[.{1,255}\](?!\([^)]*\))", RegexOptions.Compiled, TimeSpan.FromSeconds(5));

    private readonly ILogger<JiraParser> _logger;
    private readonly IGithubService _githubService;
    private readonly IJiraService _jiraService;
    private readonly ParserSettings _settings;
    private readonly JiraServiceSettings _jiraSettings;

    public JiraParser(ILogger<JiraParser> logger, IGithubService githubService, IJiraService jiraService, IOptions<ParserSettings> options, IOptions<JiraServiceSettings> jiraOptions)
    {
        _logger = logger;
        _githubService = githubService;
        _jiraService = jiraService;
        _settings = options.Value;
        _jiraSettings = jiraOptions.Value;
    }

    public async Task<IEnumerable<CreateGitHubIssue>> ConvertIssues(IEnumerable<JiraIssue> jiraIssues, JihubOptions options, IEnumerable<GithubContent> content, List<GitHubLabel> labels, List<GitHubMilestone> milestones, CancellationToken cts)
    {
        _logger.LogInformation("converting {Count} jira issues to github issues", jiraIssues.Count());

        var issues = new List<CreateGitHubIssue>();
        foreach (var issue in jiraIssues)
        {
            issues.Add(await CreateGithubIssue(issue, options, content, labels, milestones, cts).ConfigureAwait(false));
        }

        _logger.LogInformation("finish converting issues");
        return issues;
    }

    private async Task<CreateGitHubIssue> CreateGithubIssue(JiraIssue jiraIssue, JihubOptions options, IEnumerable<GithubContent> content, List<GitHubLabel> existingLabels, List<GitHubMilestone> milestones, CancellationToken cts)
    {
        var (assets, linkedAttachments) = await HandleAttachments(jiraIssue, options, content, jiraIssue.Fields.Description, cts).ConfigureAwait(false);
        var state = GetGithubState(jiraIssue);
        var milestoneNumber = await GetMilestoneNumber(jiraIssue, options, milestones, cts).ConfigureAwait(false);
        var mailMapping = jiraIssue.Fields.Assignee != null
            ? _settings.Jira.EmailMappings.SingleOrDefault(x => x.JiraUsername.Equals(jiraIssue.Fields.Assignee.DisplayName, StringComparison.OrdinalIgnoreCase))
            : null;
        var assignee = mailMapping != null ? Enumerable.Repeat(mailMapping.GithubName, 1) : Enumerable.Empty<string>();

        var reporterName = jiraIssue.Fields.Reporter?.DisplayName ?? "N/A";

        var description = GetDescription(jiraIssue, options, linkedAttachments, assets, reporterName);
        var labels = await GetGithubLabels(jiraIssue, options, existingLabels, cts).ConfigureAwait(false);
        var comments = GetGithubComments(jiraIssue);
        var pullRequests = ExtractPullRequestReferences(jiraIssue, options);

        return new CreateGitHubIssue(
            $"{jiraIssue.Fields.Summary} (jira: {jiraIssue.Key})",
            description,
            milestoneNumber,
            state,
            labels.Select(x => x.Name),
            assignee,
            assets,
            comments,
            jiraIssue.Fields.Status.Name,
            jiraIssue.Fields.Priority.Name,
            pullRequests
        );
    }

    private IEnumerable<GitHubPullRequestReference> ExtractPullRequestReferences(JiraIssue jiraIssue, JihubOptions options)
    {
        if (!options.LinkPrs || jiraIssue.Fields.RemoteLinks == null)
        {
            return Enumerable.Empty<GitHubPullRequestReference>();
        }

        var prReferences = new List<GitHubPullRequestReference>();
        foreach (var link in jiraIssue.Fields.RemoteLinks)
        {
            var url = link.Object.Url;
            if (!url.Contains("github.com") || !url.Contains("/pull/"))
            {
                continue;
            }

            try
            {
                // Parse URL like: https://github.com/owner/repo/pull/123
                var uri = new Uri(url);
                var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
                
                if (segments.Length >= 4 && segments[2] == "pull" && int.TryParse(segments[3], out var prNumber))
                {
                    prReferences.Add(new GitHubPullRequestReference(
                        segments[0],  // owner
                        segments[1],  // repo
                        prNumber,
                        url
                    ));
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse PR URL: {Url}", url);
            }
        }

        return prReferences;
    }

    private IEnumerable<string> GetGithubComments(JiraIssue jiraIssue)
    {
        return jiraIssue.Fields.Comment?.Comments?.Select(comment =>
        {
            var authorName = comment.Author?.DisplayName ?? "N/A";
            if (!DateTime.TryParse(comment.Created, out var created))
            {
                created = DateTime.Now;
            }

            return $"*{authorName}* a écrit le {created:dd.MM.yyyy HH:mm} :\n\n{comment.Body}";
        }) ?? Enumerable.Empty<string>();
    }

    private async Task<(List<GithubAsset>, List<(string, GithubAsset)> linkedAttachments)> HandleAttachments(JiraIssue jiraIssue, JihubOptions options, IEnumerable<GithubContent> githubContent, string description, CancellationToken cts)
    {
        var matches = _regex.Matches(description);
        var assets = new List<GithubAsset>();
        foreach (var attachment in jiraIssue.Fields.Attachment)
        {
            var content = githubContent.SingleOrDefault(x => x.Name.Equals($"{jiraIssue.Key}-{attachment.Filename}", StringComparison.OrdinalIgnoreCase));
            if (!options.Export || content != null)
            {
                assets.Add(new GithubAsset(
                    content == null ? attachment.Url : content.Url,
                    content == null ? attachment.Url : content.DownloadUrl,
                    $"{jiraIssue.Key}-{attachment.Filename}"));
                continue;
            }

            var fileData = await _jiraService.GetAttachmentAsync(attachment.Url, cts).ConfigureAwait(false);
            try
            {
                var asset = await _githubService
                    .CreateAttachmentAsync(options.ImportOwner!, options.UploadRepo!, options.ImportPath, options.Branch, fileData, $"{jiraIssue.Key}-{attachment.Filename}", cts)
                    .ConfigureAwait(false);
                assets.Add(asset);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Couldn't create asset: {AssetName}. Error: {Error}", attachment.Filename, ex.Message);
            }
        }

        var linkedAttachments = new List<ValueTuple<string, GithubAsset>>();
        if (!matches.Any())
        {
            return (assets, linkedAttachments);
        }

        foreach (var groups in _regex.Matches(description).Select(m => m.Groups))
        {
            var asset = assets.Find(x => x.Name.Contains(groups[1].Value.Split("|")[0]));
            if (asset == null)
            {
                _logger.LogError("Asset {AssetName} couldn't be found", groups[1].Value);
            }

            linkedAttachments.Add(new(groups[0].Value, asset!));
            assets.Remove(asset!);
        }

        return (assets, linkedAttachments);
    }

    private string GetDescription(JiraIssue jiraIssue, JihubOptions options, ICollection<(string, GithubAsset)> attachmentsToReplace, IEnumerable<GithubAsset> assets, string reporterName)
    {
        var description = _regex.Replace(jiraIssue.Fields.Description.Replace(@"\u{a0}", ""),
            x => ReplaceMatch(x, attachmentsToReplace, options.Link));
        description = _linkRegex.Replace(description, ReplaceLinks);
        var components = jiraIssue.Fields.Components.Any() ?
            string.Join(", ", jiraIssue.Fields.Components.Select(x => x.Name)) :
            "N/A";
        var sprints = jiraIssue.Fields.Sprints != null && jiraIssue.Fields.Sprints.Any() ?
            string.Join(", ", jiraIssue.Fields.Sprints.Select(x => x.Split("name=").LastOrDefault()?.Split(",").FirstOrDefault()?.Trim())) :
            "N/A";
        var fixVersions = jiraIssue.Fields.Versions.Any() ?
            string.Join(", ", jiraIssue.Fields.Versions.Select(x => x.Name)) :
            "N/A";
        var storyPoints = jiraIssue.Fields.StoryPoints == null ?
            "N/A"
            : jiraIssue.Fields.StoryPoints.ToString();

        var linkAsContent = options.Link ? string.Empty : "!";
        var attachments = assets.Any() ?
            string.Join(", ", assets.Select(a => $"{linkAsContent}[{a.Name}]({a.DownloadUrl})")) :
            "N/A";

        var prLinks = "N/A";
        if (options.LinkPrs && jiraIssue.Fields.RemoteLinks != null)
        {
            var githubPrs = jiraIssue.Fields.RemoteLinks
                .Where(link => link.Object.Url.Contains("github.com") && link.Object.Url.Contains("/pull/"))
                .Select(link => $"[{link.Object.Title}]({link.Object.Url})");

            if (githubPrs.Any())
            {
                prLinks = string.Join(", ", githubPrs);
            }
        }

        var jiraLink = $"{_jiraSettings.JiraInstanceUrl.TrimEnd('/')}/browse/{jiraIssue.Key}";
        var result = _settings.Jira.DescriptionTemplate
            .Replace("{{Description}}", description)
            .Replace("{{Components}}", components)
            .Replace("{{Sprints}}", sprints)
            .Replace("{{FixVersions}}", fixVersions)
            .Replace("{{StoryPoints}}", storyPoints)
            .Replace("{{Attachments}}", attachments)
            .Replace("{{Reporter}}", reporterName)
            .Replace("{{JiraLink}}", jiraLink)
            .Replace("{{PullRequests}}", prLinks);

        return result;
    }

    private async Task<IEnumerable<GitHubLabel>> GetGithubLabels(JiraIssue jiraIssue, JihubOptions options, List<GitHubLabel> existingLabels, CancellationToken cts)
    {
        var labels = jiraIssue.Fields.Labels
            .Select(x => new GitHubLabel(x, string.Empty, "c5c5c5"))
            .Concat(new GitHubLabel[]
        {
            new(
                $"type: {jiraIssue.Fields.Issuetype.Name}",
                jiraIssue.Fields.Issuetype.Description?.Length > 100
                    ? jiraIssue.Fields.Issuetype.Description[..97] + "..."
                    : jiraIssue.Fields.Issuetype.Description ?? string.Empty,
                "d4ecff"
            ),
            new(
                $"status: {jiraIssue.Fields.Status.Name}",
                $"Jira status: {jiraIssue.Fields.Status.Name}",
                GetStatusColor(jiraIssue.Fields.Status.StatusCategory.ColorName)
            ),
            new(
                $"priority: {jiraIssue.Fields.Priority.Name}",
                $"Jira priority: {jiraIssue.Fields.Priority.Name}",
                GetPriorityColor(jiraIssue.Fields.Priority.Name)
            )
        });

        if (!string.IsNullOrEmpty(options.AdditionalLabel))
        {
            labels = labels.Concat(new[] { new GitHubLabel(options.AdditionalLabel, "Static label for import source", "d4c5f9") });
        }

        var missingLabels = labels
            .Where(l => !existingLabels.Select(el => el.Name)
                .Any(el => el.Equals(l.Name, StringComparison.OrdinalIgnoreCase)))
            .DistinctBy(l => l.Name);
        var createdLabels = await _githubService.CreateLabelsAsync(options.Owner, options.Repo, missingLabels, cts)
            .ConfigureAwait(false);
        existingLabels.AddRange(createdLabels);
        return labels;
    }

    private GithubState GetGithubState(JiraIssue jiraIssue)
    {
        var state = GithubState.Open;
        var stateKey = _settings.Jira.StateMapping.Any(kvp => kvp.Value.Contains(jiraIssue.Fields.Status.Name, StringComparer.OrdinalIgnoreCase));
        if (!stateKey)
        {
            _logger.LogError("Could not find {State} in state mapping, automatically set to open", jiraIssue.Fields.Status.Name);
        }
        else
        {
            state = _settings.Jira.StateMapping.FirstOrDefault(kvp => kvp.Value.Contains(jiraIssue.Fields.Status.Name)).Key;
        }

        return state;
    }

    private async Task<int?> GetMilestoneNumber(JiraIssue jiraIssue, JihubOptions options, List<GitHubMilestone> milestones, CancellationToken cts)
    {
        int? milestoneNumber = null;
        if (!jiraIssue.Fields.FixVersions.Any())
        {
            return milestoneNumber;
        }

        var fixVersion = jiraIssue.Fields.FixVersions.Last();
        var milestone = milestones.SingleOrDefault(x => x.Title.Equals(fixVersion.Name, StringComparison.OrdinalIgnoreCase));
        if (milestone != null)
        {
            return milestone.Number;
        }

        milestone = await _githubService
            .CreateMilestoneAsync(fixVersion.Name, options.Owner, options.Repo, cts)
            .ConfigureAwait(false);
        milestones.Add(milestone);

        return milestone.Number;
    }

    private static string ReplaceMatch(Match match, ICollection<(string JiraDescriptionUrl, GithubAsset Asset)> linkedAttachments, bool authorizedLink)
    {
        var url = match.Groups[1].Value;
        var matchingAttachment = linkedAttachments.SingleOrDefault(x => x.JiraDescriptionUrl == match.Groups[0].Value);
        var linkAsContent = authorizedLink ? string.Empty : "!";
        var result = matchingAttachment == default ?
            $"{linkAsContent}[{url.Split("/").LastOrDefault()}]({url})" :
            $"{linkAsContent}[{matchingAttachment.Asset.Name}]({matchingAttachment.Asset.DownloadUrl})";
        linkedAttachments.Remove(matchingAttachment);
        return result;
    }

    private static string ReplaceLinks(Match match)
    {
        var link = match.Groups[0].Value
            .Replace("[", string.Empty)
            .Replace("]", string.Empty);
        if (link.Contains("|"))
        {
            var linkElements = link.Split("|");
            return $"[{linkElements[0]}]({linkElements[1]})";
        }

        return $"[{link.Split("/").Last()}]({link})";
    }

    private static string GetPriorityColor(string priorityName)
    {
        return priorityName.ToLower() switch
        {
            "haute" or "high" or "critical" or "urgent" => "b60205",
            "moyenne" or "medium" or "normal" => "fbca04",
            "basse" or "low" or "trivial" => "0e8a16",
            _ => "c5c5c5"
        };
    }

    private static string GetStatusColor(string colorName)
    {
        return colorName?.ToLower() switch
        {
            "green" => "0e8a16",
            "yellow" => "fbca04",
            "medium-gray" => "d4c5f9",
            "blue-gray" => "d4c5f9",
            "red" => "b60205",
            _ => "c5c5c5"
        };
    }
}
