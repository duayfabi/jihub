using System.Text.Json.Serialization;

namespace jihub.Jira.Models;

public record JiraResult
(
    int StartAt,
    int MaxResults,
    int Total,
    string? NextPageToken,
    IEnumerable<JiraIssue> Issues
);

public record JiraIssue(string Id, string Key, IssueFields Fields);

public record IssueFields
(
    IssueType Issuetype,
    IEnumerable<JiraAttachment> Attachment,
    [property: JsonConverter(typeof(JiraDescriptionConverter))]
    string Description,
    string Summary,
    IEnumerable<Component> Components,
    Project Project,
    Assignee Assignee,
    Assignee Reporter,
    IssueStatus Status,
    IEnumerable<string> Labels,
    IEnumerable<FixVersion> FixVersions,
    IEnumerable<Version> Versions,
    [property: JsonPropertyName("customfield_10028")]
    double? StoryPoints,
    [property: JsonPropertyName("customfield_10020")]
    IEnumerable<string>? Sprints,
    [property: JsonPropertyName("issuelinks")]
    IEnumerable<JiraIssueLink>? IssueLinks,
    JiraCommentPage Comment,
    JiraPriority Priority,
    IEnumerable<JiraRemoteLink>? RemoteLinks
);

public record JiraPriority(string Name, string Id);

public record JiraRemoteLink
(
    string Id,
    string Self,
    RemoteLinkObject Object
);

public record RemoteLinkObject
(
    string Url,
    string Title,
    string? Summary,
    [property: JsonPropertyName("iconUrl")]
    string? IconUrl
);

public record JiraAttachment
(
    string Filename,
    [property: JsonPropertyName("content")]
    string Url
);

public record IssueType(string Name, string Description);

public record Project(string Key, string Name);

public record Assignee(string? Name, string? Email, string DisplayName, string AccountId);

public record IssueStatus(string Name, StatusCategory StatusCategory);

public record StatusCategory(int Id, string Key, string ColorName, string Name);

public record FixVersion(string Name);

public record Version(string Name);

public record Component(string Name);

public record JiraIssueLink
(
    JiraIssueLinkType Type,
    LinkedIssue? OutwardIssue,
    LinkedIssue? InwardIssue
);

public record JiraIssueLinkType(string Inward, string Outward);

public record LinkedIssue(string Key);

public record JiraCommentPage
(
    int Total,
    IEnumerable<JiraComment> Comments
);

public record JiraComment
(
    string Id,
    Assignee Author,
    [property: JsonConverter(typeof(JiraDescriptionConverter))]
    string Body,
    string Created
);
