namespace jihub.Github.Models;

/// <summary>
/// Represents a parsed GitHub Pull Request reference
/// </summary>
public record GitHubPullRequestReference
(
    string Owner,
    string Repo,
    int Number,
    string Url
);
