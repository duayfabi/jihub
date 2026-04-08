using System.Text.Json.Serialization;

namespace jihub.Github.Models;

public record GitHubRepository(
    [property: JsonPropertyName("default_branch")]
    string DefaultBranch
);
