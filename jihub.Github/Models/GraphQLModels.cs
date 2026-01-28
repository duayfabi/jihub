using System.Text.Json.Serialization;

namespace jihub.Github.Models;

public record GraphQLResponse<T>(T Data, IEnumerable<GraphQLError>? Errors);

public record GraphQLError(string Message);

public record ProjectV2Response(ProjectV2Owner? User, ProjectV2Owner? Organization);

public record ProjectV2Owner(ProjectV2? ProjectV2);

public record ProjectV2(string Id, ProjectV2Fields Fields);

public record ProjectV2Fields(IEnumerable<ProjectV2FieldNode> Nodes);

public record ProjectV2FieldNode(
    string Id,
    string Name,
    IEnumerable<ProjectV2Option>? Options
);

public record ProjectV2Option(string Id, string Name);

public record AddProjectV2ItemResponse(AddProjectV2ItemPayload AddProjectV2ItemById);

public record AddProjectV2ItemPayload(ProjectV2Item Item);

public record ProjectV2Item(string Id);

public record UpdateProjectV2ItemResponse(UpdateProjectV2ItemPayload UpdateProjectV2ItemFieldValue);

public record UpdateProjectV2ItemPayload(ProjectV2Item ProjectV2Item);

public record AddProjectV2DraftIssueResponse(AddProjectV2DraftIssuePayload AddProjectV2DraftIssue);

public record AddProjectV2DraftIssuePayload(ProjectV2Item ProjectItem);
