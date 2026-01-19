using System.Text.Json.Serialization;

namespace jihub.Jira.Models;

public record JiraDevStatusResponse
(
    IEnumerable<Detail> Detail
);

public record Detail
(
    [property: JsonPropertyName("pullRequests")]
    PullRequest[]? PullRequests
);

public record PullRequest
(
    string Id,
    string Name,
    string Url,
    string Status
);
