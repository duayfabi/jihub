using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using jihub.Jira.Models;
using Microsoft.Extensions.Logging;

namespace jihub.Jira;

public class JiraService : IJiraService
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly ILogger<JiraService> _logger;
    private readonly HttpClient _httpClient;
    private readonly HttpClient _httpDownloadClient;

    public JiraService(IHttpClientFactory httpClientFactory, ILogger<JiraService> logger)
    {
        _logger = logger;
        _httpClient = httpClientFactory.CreateClient(nameof(JiraService));
        _httpDownloadClient = httpClientFactory.CreateClient($"{nameof(JiraService)}Download");
    }

    public async Task<IEnumerable<JiraIssue>> GetAsync(string searchQuery, int maxResults, CancellationToken cts)
    {
        var result = await RequestJiraIssues(searchQuery, null, maxResults, cts).ConfigureAwait(false);
        var issues = result.Issues.ToList();
        
        while (!string.IsNullOrEmpty(result.NextPageToken) && issues.Count < result.Total)
        {
            var queryMaxResults = result.Total - issues.Count;
            queryMaxResults = maxResults <= queryMaxResults ? maxResults : queryMaxResults;
            result = await RequestJiraIssues(searchQuery, result.NextPageToken, queryMaxResults, cts).ConfigureAwait(false);
            issues.AddRange(result.Issues);
        }

        _logger.LogInformation("Received {Count} Jira Issues", issues.Count);

        var enrichedIssues = await Task.WhenAll(issues.Select(async issue =>
        {
            var remoteLinks = await GetRemoteLinksAsync(issue, cts).ConfigureAwait(false);
            return issue with { Fields = issue.Fields with { RemoteLinks = remoteLinks } };
        })).ConfigureAwait(false);

        return enrichedIssues;
    }

    private async Task<IEnumerable<JiraRemoteLink>> GetRemoteLinksAsync(JiraIssue issue, CancellationToken cts)
    {
        var issueKey = issue.Key;
        var issueId = issue.Id;
        // Get traditional remote links
        var url = $"rest/api/3/issue/{issueKey}/remotelink";
        _logger.LogInformation("Requesting Remote Links for issue {IssueKey}", issueKey);
        var result = await _httpClient.GetFromJsonAsync<IEnumerable<JiraRemoteLink>>(url, Options, cts).ConfigureAwait(false);
        var remoteLinks = result ?? Enumerable.Empty<JiraRemoteLink>();
        
        _logger.LogInformation("Found {Count} Remote Links for issue {IssueKey}", remoteLinks.Count(), issueKey);
        foreach (var link in remoteLinks)
        {
            _logger.LogInformation("Remote Link: {Title} - {Url}", link.Object.Title, link.Object.Url);
        }
        
        // Get development information (PR links from Development panel)
        var devLinks = await GetDevelopmentInfoAsync(issueKey, issueId, cts).ConfigureAwait(false);
        
        // Merge both sources
        return remoteLinks.Concat(devLinks);
    }

    private async Task<IEnumerable<JiraRemoteLink>> GetDevelopmentInfoAsync(string issueKey, string issueId, CancellationToken cts)
    {
        // Try different endpoint variations for Jira Cloud
        var endpoints = new[]
        {
            $"rest/dev-status/1.0/issue/details?issueId={issueId}&applicationType=github&dataType=pullrequest", // Verified by user
            $"rest/dev-status/1.0/issue/details?issueId={issueId}", // Fallback without filters
        };

        foreach (var endpoint in endpoints)
        {
            try
            {
                _logger.LogInformation("Trying Development Info endpoint: {Endpoint}", endpoint);
                
                // Read content for debugging
                // var response = await _httpClient.GetAsync(endpoint, cts).ConfigureAwait(false);
                // response.EnsureSuccessStatusCode();
                // var json = await response.Content.ReadAsStringAsync(cts).ConfigureAwait(false);
                
                // _logger.LogInformation("Raw Dev Info Response: {Json}", json); // Uncomment for deep debugging

                // var result = JsonSerializer.Deserialize<JiraDevStatusResponse>(json, Options);
                var result = await _httpClient.GetFromJsonAsync<JiraDevStatusResponse>(endpoint, Options, cts).ConfigureAwait(false);
                
                // Detail is now IEnumerable, we need to collect all PRs from all details
                var prs = result?.Detail?.Where(d => d.PullRequests != null).SelectMany(d => d.PullRequests!).ToArray();

                if (prs == null || !prs.Any())
                {
                    _logger.LogInformation("Found 0 PRs with endpoint: {Endpoint}", endpoint);
                    continue;
                }

                _logger.LogInformation("✓ Found {Count} PRs in Development panel using {Endpoint}", prs.Length, endpoint);
                
                // Convert dev-status PRs to RemoteLink format
                var devLinks = prs.Select(pr =>
                {
                    _logger.LogInformation("Development PR: {Name} - {Url} (Status: {Status})", pr.Name, pr.Url, pr.Status);
                    return new JiraRemoteLink(
                        pr.Id,
                        pr.Url,
                        new RemoteLinkObject(pr.Url, pr.Name, $"PR Status: {pr.Status}", null)
                    );
                });

                return devLinks;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Endpoint {Endpoint} failed, trying next...", endpoint);
            }
        }
        
        _logger.LogWarning("Could not fetch development info using any known endpoint. PRs from the Development panel won't be linked.");
        _logger.LogInformation("Tip: Check if your Jira instance has the GitHub for Jira app installed and configured.");
        return Enumerable.Empty<JiraRemoteLink>();
    }

    private async Task<JiraResult> RequestJiraIssues(string searchQuery, string? nextPageToken, int maxResults, CancellationToken cts)
    {
        var url = $"rest/api/3/search/jql?jql={searchQuery}&maxResults={maxResults}&fields=id,key,labels,issuetype,project,status,description,summary,components,fixVersions,versions,customfield_10028,customfield_10020,attachment,assignee,issuelinks,reporter,comment,priority";
        if (!string.IsNullOrEmpty(nextPageToken))
        {
            url += $"&nextPageToken={nextPageToken}";
        }

        _logger.LogInformation("[Jira] Requesting Issues with JQL (raw from CLI): {Jql}", searchQuery);
        var result = await _httpClient.GetFromJsonAsync<JiraResult>(url, Options, cts).ConfigureAwait(false);
        if (result == null)
        {
            throw new("Jira request failed");
        }

        _logger.LogInformation("[Jira] Actually received {Count} Jira Issues from API", result.Issues.Count());
        return result;
    }

    public async Task<(string Hash, string Content)> GetAttachmentAsync(string url, CancellationToken cts)
    {
        var response = await _httpDownloadClient.GetAsync(url, cts).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        using var sha512Hash = SHA512.Create();
        using var contentStream = await response.Content.ReadAsStreamAsync(cts);
        using var ms = new MemoryStream((int)contentStream.Length);
        await contentStream.CopyToAsync(ms, cts).ConfigureAwait(false);

        var h = await sha512Hash.ComputeHashAsync(ms, cts).ConfigureAwait(false);
        var c = ms.GetBuffer();
        var hash = Convert.ToBase64String(h);
        var content = Convert.ToBase64String(c);
        if (ms.Length != contentStream.Length || c.Length != contentStream.Length)
        {
            throw new($"asset {url.Split("/").Last()} transmitted length {contentStream.Length} doesn't match actual length {ms.Length}.");
        }

        return (hash, content);
    }
}
