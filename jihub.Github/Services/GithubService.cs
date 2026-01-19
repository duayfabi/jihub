using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Web;
using jihub.Base;
using jihub.Github.Models;
using Microsoft.Extensions.Logging;

namespace jihub.Github.Services;

public class GithubService : IGithubService
{
    private const int batchSize = 10;
    private const int delaySeconds = 20;
    private int _mutationCounter = 0;
    private ProjectV2? _cachedProject;

    private async Task EnsureRateLimit(CancellationToken ct)
    {
        _mutationCounter++;
        if (_mutationCounter % batchSize == 0)
        {
            _logger.LogInformation("Delaying {Delay} seconds for github to catch some air (Global Rate Limiting)...", delaySeconds);
            await Task.Delay(TimeSpan.FromSeconds(delaySeconds), ct).ConfigureAwait(false);
        }
    }

    private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private readonly ILogger<GithubService> _logger;
    private readonly HttpClient _httpClient;

    public GithubService(IHttpClientFactory httpClientFactory, ILogger<GithubService> logger)
    {
        _logger = logger;
        _httpClient = httpClientFactory.CreateClient(nameof(GithubService));
    }

    public async Task<IEnumerable<GithubContent>> GetRepoContent(string owner, string repo, string path, CancellationToken cts)
    {
        var content = await Get<GithubContent>(path, owner, repo, cts).ConfigureAwait(false);
        var files = content.Where(x => x.Type == "file").ToList();
        foreach (var dir in content.Where(x => x.Type == "dir"))
        {
            files.AddRange(await GetRepoContent(owner, repo, $"{path}/{dir.Name}", cts).ConfigureAwait(false));
        }

        return files;
    }

    public async Task LinkChildren(string owner, string repo,
        Dictionary<string, List<string>> linkedIssues,
        IEnumerable<GitHubIssue> existingIssues,
        IEnumerable<GitHubIssue> createdIssues,
        CancellationToken cancellationToken)
    {
        var allIssues = existingIssues.Concat(createdIssues).ToArray();
        foreach (var linkedIssue in linkedIssues)
        {
            if (!linkedIssue.Value.Any())
            {
                continue;
            }

            var matchingIssues = linkedIssue.Value
                .Select(key => createdIssues.FirstOrDefault(i => i.Title.Contains($"(jira: {key})")))
                .Where(i => i != null)
                .Select(i => $"- [ ] #{i!.Number}");
            var issueToUpdate = allIssues.FirstOrDefault(x => x.Title.Contains($"(jira: {linkedIssue.Key})"));
            if (issueToUpdate == null || !matchingIssues.Any())
            {
                continue;
            }

            var updatedBody = issueToUpdate.Body ?? string.Empty;
            if (!updatedBody.Contains("### Children"))
            {
                updatedBody = $"{updatedBody}\n\n### Children";
            }

            updatedBody = $"{updatedBody}\n{string.Join("\n", matchingIssues)}";
            await UpdateIssue(owner, repo, issueToUpdate.Number, new { body = updatedBody }, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task AddRelatesComment(string owner, string repo, Dictionary<string, List<string>> relatedIssues, IEnumerable<GitHubIssue> existingIssues,
        IEnumerable<GitHubIssue> createdIssues, CancellationToken cancellationToken)
    {
        var allIssues = existingIssues.Concat(createdIssues).ToArray();
        foreach (var relatedIssue in relatedIssues)
        {
            if (!relatedIssue.Value.Any())
            {
                continue;
            }

            var matchingIssues = relatedIssue.Value
                .Select(key => createdIssues.FirstOrDefault(i => i.Title.Contains($"(jira: {key})")))
                .Where(i => i != null)
                .Select(i => $"#{i!.Number}");
            var issueToUpdate = allIssues.FirstOrDefault(x => x.Title.Contains($"(jira: {relatedIssue.Key})"));
            if (issueToUpdate == null || !matchingIssues.Any())
            {
                continue;
            }

            await CreateComment(owner, repo, issueToUpdate.Number, $"Relates to: {string.Join(", ", matchingIssues)}", cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<GitHubInformation> GetRepositoryData(string owner, string repo, CancellationToken cts)
    {
        var allIssues = new List<GitHubIssue>();
        var page = 1;
        const int issuesPerPage = 100;
        while (true)
        {
            var issues = await Get<GitHubIssue>("issues", owner, repo, cts, $"state=all&per_page={issuesPerPage}&page={page}").ConfigureAwait(false);
            allIssues.AddRange(issues);

            if (!issues.Any() || issues.Count() < issuesPerPage)
            {
                break;
            }

            page++;
        }

        var allLabels = new List<GitHubLabel>();
        page = 1;
        while (true)
        {
            var labels = await Get<GitHubLabel>("labels", owner, repo, cts, $"per_page={issuesPerPage}&page={page}").ConfigureAwait(false);
            allLabels.AddRange(labels);
            if (!labels.Any() || labels.Count() < issuesPerPage)
            {
                break;
            }
            page++;
        }

        var allMilestones = new List<GitHubMilestone>();
        page = 1;
        while (true)
        {
            var milestones = await Get<GitHubMilestone>("milestones", owner, repo, cts, $"state=all&per_page={issuesPerPage}&page={page}").ConfigureAwait(false);
            allMilestones.AddRange(milestones);
            if (!milestones.Any() || milestones.Count() < issuesPerPage)
            {
                break;
            }
            page++;
        }

        return new GitHubInformation(
            allIssues,
            allLabels,
            allMilestones);
    }

    private async Task<IEnumerable<T>> Get<T>(string urlPath, string owner, string repo, CancellationToken cts, string? queryParameter = null)
    {
        var url = $"repos/{owner}/{repo}/{urlPath}";
        if (queryParameter != null)
        {
            url = $"{url}?{queryParameter}";
        }

        _logger.LogInformation("[GitHub] Requesting {urlPath}", urlPath);
        var response = await _httpClient.GetAsync(url, cts).ConfigureAwait(false);
        if (response.IsSuccessStatusCode)
        {
            var jsonResponse = await response.Content.ReadAsStringAsync(cts).ConfigureAwait(false);
            var result = JsonSerializer.Deserialize<IEnumerable<T>>(jsonResponse, Options);
            if (result is null)
            {
                throw new("Github request failed");
            }

            _logger.LogInformation("[GitHub] Received {Count} {urlPath}", result.Count(), urlPath);
            return result;
        }

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogInformation("{urlPath} not found (this is normal if it's the first run).", urlPath);
            return new List<T>();
        }

        _logger.LogError("Couldn't receive {urlPath} because of status code {StatusCode}", urlPath, response.StatusCode);
        return new List<T>();
    }

    public async Task<ICollection<GitHubLabel>> CreateLabelsAsync(string owner, string repo, IEnumerable<GitHubLabel> missingLabels, CancellationToken cts)
    {
        var createdLabels = new List<GitHubLabel>();
        foreach (var label in missingLabels)
        {
            await EnsureRateLimit(cts).ConfigureAwait(false);
            var url = $"repos/{owner}/{repo}/labels";

            _logger.LogInformation("Creating label: {label}", label.Name);
            var result = await _httpClient.PostAsJsonAsync(url, label, Options, cts).ConfigureAwait(false);
            if (result == null)
            {
                throw new("Label creation request failed");
            }

            if (result.StatusCode == HttpStatusCode.Created)
            {
                var content = await result.Content.ReadAsStringAsync(cts).ConfigureAwait(false);
                createdLabels.Add(JsonSerializer.Deserialize<GitHubLabel>(content, Options) ?? throw new InvalidOperationException());
            }
            else
            {
                var errorContent = await result.Content.ReadAsStringAsync(cts).ConfigureAwait(false);
                if (result.StatusCode == System.Net.HttpStatusCode.UnprocessableEntity)
                {
                    _logger.LogWarning("Couldn't create label: {Label}. It likely already exists.", label.Name);
                    continue;
                }

                _logger.LogError("Couldn't create label: {Label}. Status Code: {StatusCode}. Response: {Response}", label.Name, result.StatusCode, errorContent);
            }
        }

        return createdLabels;
    }

    public async Task<GitHubMilestone> CreateMilestoneAsync(string name, string owner, string repo, CancellationToken cts)
    {
        await EnsureRateLimit(cts).ConfigureAwait(false);
        var url = $"repos/{owner}/{repo}/milestones";

        _logger.LogInformation("Creating milestone: {milestone}", name);
        var result = await _httpClient.PostAsJsonAsync(url, new
        {
            title = name
        }, Options, cts).ConfigureAwait(false);
        if (result is null)
        {
            throw new("Milestone creation failed");
        }

        if (result.StatusCode == HttpStatusCode.Created)
        {
            var content = await result.Content.ReadAsStringAsync(cts).ConfigureAwait(false);
            return JsonSerializer.Deserialize<GitHubMilestone>(content, Options) ?? throw new InvalidOperationException();
        }

        _logger.LogError("Couldn't create milestone: {milestone}", name);
        throw new($"Couldn't create milestone: {name}");
    }

    public async Task<IEnumerable<GitHubIssue>> CreateIssuesAsync(string owner, string repo, IEnumerable<CreateGitHubIssue> issues, JihubOptions options, CancellationToken cts)
    {
        var createdIssues = new List<GitHubIssue>();
        foreach (var issue in issues)
        {
            var createdIssue = await CreateIssue(owner, repo, issue, cts).ConfigureAwait(false);
            if (createdIssue != null)
            {
                createdIssues.Add(createdIssue);
                if (options.ProjectNumber.HasValue)
                {
                    await AddIssueToProjectV2(options.ProjectOwner!, options.ProjectNumber.Value, createdIssue, issue.OriginalStatus, issue.OriginalPriority, cts).ConfigureAwait(false);
                }

                // Link PRs to the newly created issue
                if (issue.PullRequests != null && issue.PullRequests.Any())
                {
                    foreach (var pr in issue.PullRequests)
                    {
                        var issueReference = pr.Owner == owner && pr.Repo == repo
                            ? $"#{createdIssue.Number}"
                            : $"{owner}/{repo}#{createdIssue.Number}";
                        
                        var comment = $"Relates to {issueReference}";
                        await CommentOnPullRequest(pr.Owner, pr.Repo, pr.Number, comment, cts).ConfigureAwait(false);
                    }
                }
            }
        }

        return createdIssues;
    }

    private async Task<GitHubIssue?> CreateIssue(string owner, string repo, CreateGitHubIssue issue, CancellationToken ct)
    {
        await EnsureRateLimit(ct).ConfigureAwait(false);
        var url = $"repos/{owner}/{repo}/issues";
        _logger.LogInformation("Creating issue: {issue}", issue.Title);
        var result = await _httpClient.PostAsJsonAsync(url, issue, Options, ct).ConfigureAwait(false);
        if (!result.IsSuccessStatusCode)
        {
            var error = await result.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            
            // If it's a validation error related to assignees, retry without them
            if (result.StatusCode == System.Net.HttpStatusCode.UnprocessableEntity && error.Contains("assignees", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Couldn't assign issue: {issue}. Retrying without assignees. Original error: {Error}", issue.Title, error);
                var issueWithoutAssignees = issue with { Assignees = null };
                result = await _httpClient.PostAsJsonAsync(url, issueWithoutAssignees, Options, ct).ConfigureAwait(false);
                
                if (!result.IsSuccessStatusCode)
                {
                    error = await result.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    _logger.LogError("Couldn't create issue even without assignees: {issue}. Error: {Error}", issue.Title, error);
                    return null;
                }
            }
            else
            {
                _logger.LogError("Couldn't create issue: {issue}. Error: {Error}", issue.Title, error);
                return null;
            }
        }

        var response = await result.Content.ReadFromJsonAsync<GitHubIssue>(Options, ct).ConfigureAwait(false);
        if (response == null)
        {
            _logger.LogError("State of issue {IssueId} could not be determined", issue.Title);
            return null;
        }

        // Migrate comments
        if (issue.Comments != null)
        {
            foreach (var comment in issue.Comments)
            {
                await CreateComment(owner, repo, response.Number, comment, ct).ConfigureAwait(false);
            }
        }

        if (issue.State == GithubState.Open)
        {
            _logger.LogInformation("Issue #{Number} created successfully.", response.Number);
            return response;
        }

        await UpdateIssue(owner, repo, response.Number, new { state = "closed" }, ct);
        _logger.LogInformation("Issue #{Number} created and closed successfully.", response.Number);

        return response;
    }

    private async Task CreateComment(string owner, string repo, int issueNumber, string body, CancellationToken ct)
    {
        await EnsureRateLimit(ct).ConfigureAwait(false);
        var url = $"repos/{owner}/{repo}/issues/{issueNumber}/comments";
        var result = await _httpClient.PostAsJsonAsync(url, new { body }, Options, ct).ConfigureAwait(false);
        if (result.IsSuccessStatusCode)
        {
            _logger.LogInformation("Comment created successfully on issue #{Number}.", issueNumber);
        }
        else
        {
            var error = await result.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            _logger.LogError("Couldn't create comment on issue #{Number}: {Status}. Error: {Error}", issueNumber, result.StatusCode, error);
        }
    }

    private async Task UpdateIssue(string owner, string repo, int id, object data, CancellationToken ct)
    {
        await EnsureRateLimit(ct).ConfigureAwait(false);
        var url = $"repos/{owner}/{repo}/issues/{id}";
        var updateResult = await _httpClient.PatchAsJsonAsync(url, data, Options, ct).ConfigureAwait(false);
        if (!updateResult.IsSuccessStatusCode)
        {
            var error = await updateResult.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            _logger.LogError("Update of issue {IssueId} failed. Error: {Error}", id, error);
        }
    }

    public async Task<GithubAsset> CreateAttachmentAsync(string owner, string repo, string? importPath, string? branch, (string Hash, string FileContent) fileData, string name, CancellationToken cts)
    {
        await EnsureRateLimit(cts).ConfigureAwait(false);
        var directory = importPath == null ? string.Empty : $"{importPath}/";
        var url = $"repos/{owner}/{repo}/contents/{directory}{name}";
        var content = new UploadFileContent(
            $"Upload file {name}",
            HttpUtility.HtmlEncode(fileData.FileContent),
            branch ?? "main"
        );
        var response = await _httpClient.PutAsJsonAsync(url, content, cts).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new("Asset creation failed");
        }

        var assetContent = await response.Content.ReadFromJsonAsync<GithubAssetContent>(Options, cts).ConfigureAwait(false);
        if (assetContent == null)
        {
            throw new("Couldn't parse Github Asset");
        }

        _logger.LogInformation("Asset {AssetName} uploaded successfully to GitHub.", name);
        return assetContent.Content;
    }

    public async Task<Committer> GetCommitter()
    {
        var userResponse = await _httpClient.GetFromJsonAsync<GithubUser>("user").ConfigureAwait(false);
        var emailsResponse = await _httpClient.GetFromJsonAsync<IEnumerable<GithubUserEmail>>("user/emails").ConfigureAwait(false);

        if (userResponse == null)
        {
            throw new("Couldn't receive user information");
        }

        if (emailsResponse == null)
        {
            throw new("Couldn't receive user email information");
        }

        var primaryEmails = emailsResponse.Where(x => x.Primary);
        if (primaryEmails.Count() != 1)
        {
            throw new("There must be exactly one primary email");
        }

        return new Committer(userResponse.Name, primaryEmails.Single().Email);
    }

    public async Task AddIssueToProjectV2(string projectOwner, int projectNumber, GitHubIssue issue, string? status, string? priority, CancellationToken cts)
    {
        if (_cachedProject == null)
        {
            _cachedProject = await GetProjectV2Metadata(projectOwner, projectNumber, cts).ConfigureAwait(false);
        }

        if (_cachedProject == null)
        {
            _logger.LogError("Could not find Project v2 {ProjectNumber} for {Owner}", projectNumber, projectOwner);
            return;
        }

        // Add item to project
        var itemId = await AddItemToProject(_cachedProject.Id, issue.NodeId, cts).ConfigureAwait(false);
        if (itemId == null) return;

        // Set status
        if (!string.IsNullOrEmpty(status))
        {
            await SetProjectV2Field(itemId, "Status", status, cts).ConfigureAwait(false);
        }

        // Set priority
        if (!string.IsNullOrEmpty(priority))
        {
            await SetProjectV2Field(itemId, "Priority", priority, cts).ConfigureAwait(false);
        }
    }

    private async Task SetProjectV2Field(string itemId, string fieldName, string optionName, CancellationToken cts)
    {
        var field = _cachedProject!.Fields.Nodes.FirstOrDefault(f => f.Name.Equals(fieldName, StringComparison.OrdinalIgnoreCase));
        if (field == null) return;

        var option = field.Options?.FirstOrDefault(o => o.Name.Equals(optionName, StringComparison.OrdinalIgnoreCase));
        if (option == null)
        {
            // If exact match not found, try to find one that contains the Jira status (e.g. "To Do" matches "To Do")
            option = field.Options?.FirstOrDefault(o => o.Name.Contains(optionName, StringComparison.OrdinalIgnoreCase) || optionName.Contains(o.Name, StringComparison.OrdinalIgnoreCase));
        }

        if (option == null)
        {
            var availableOptions = string.Join(", ", field.Options?.Select(o => $"'{o.Name}'") ?? Enumerable.Empty<string>());
            _logger.LogWarning("Could not find option '{OptionName}' for Project field '{FieldName}'. Available options: {AvailableOptions}", optionName, fieldName, availableOptions);
            return;
        }

        await UpdateProjectV2ItemFieldValue(_cachedProject.Id, itemId, field.Id, option.Id, cts).ConfigureAwait(false);
    }

    private async Task<ProjectV2?> GetProjectV2Metadata(string owner, int number, CancellationToken cts)
    {
        var query = @"
query($owner: String!, $number: Int!) {
  user(login: $owner) {
    projectV2(number: $number) {
      id
      fields(first: 50) {
        nodes {
          ... on ProjectV2Field { id name }
          ... on ProjectV2SingleSelectField {
            id
            name
            options { id name }
          }
        }
      }
    }
  }
  organization(login: $owner) {
     projectV2(number: $number) {
      id
      fields(first: 50) {
        nodes {
          ... on ProjectV2Field { id name }
          ... on ProjectV2SingleSelectField {
            id
            name
            options { id name }
          }
        }
      }
    }
  }
}";
        var variables = new { owner, number };
        var response = await PostGraphQL<ProjectV2Response>(query, variables, cts).ConfigureAwait(false);
        return response?.User?.ProjectV2 ?? response?.Organization?.ProjectV2;
    }

    private async Task<string?> AddItemToProject(string projectId, string contentId, CancellationToken cts)
    {
        var mutation = @"
mutation($projectId: ID!, $contentId: ID!) {
  addProjectV2ItemById(input: {projectId: $projectId, contentId: $contentId}) {
    item { id }
  }
}";
        var variables = new { projectId, contentId };
        var response = await PostGraphQL<AddProjectV2ItemResponse>(mutation, variables, cts).ConfigureAwait(false);
        return response?.AddProjectV2ItemById?.Item?.Id;
    }

    private async Task UpdateProjectV2ItemFieldValue(string projectId, string itemId, string fieldId, string optionId, CancellationToken cts)
    {
        var mutation = @"
mutation($projectId: ID!, $itemId: ID!, $fieldId: ID!, $optionId: String!) {
  updateProjectV2ItemFieldValue(input: {
    projectId: $projectId,
    itemId: $itemId,
    fieldId: $fieldId,
    value: { singleSelectOptionId: $optionId }
  }) {
    projectV2Item { id }
  }
}";
        var variables = new { projectId, itemId, fieldId, optionId };
        await PostGraphQL<UpdateProjectV2ItemResponse>(mutation, variables, cts).ConfigureAwait(false);
    }

    private async Task<T?> PostGraphQL<T>(string query, object variables, CancellationToken cts)
    {
        await EnsureRateLimit(cts).ConfigureAwait(false);
        var request = new { query, variables };
        var response = await _httpClient.PostAsJsonAsync("graphql", request, Options, cts).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cts).ConfigureAwait(false);
            _logger.LogError("GraphQL request failed with {StatusCode}: {Error}", response.StatusCode, errorBody);
            return default;
        }

        var gqlResponse = await response.Content.ReadFromJsonAsync<GraphQLResponse<T>>(Options, cts).ConfigureAwait(false);
        if (gqlResponse?.Errors != null && gqlResponse.Errors.Any())
        {
            foreach (var error in gqlResponse.Errors)
            {
                // Skip logging "Could not resolve to a User/Organization" as it's expected during project metadata probing
                if (error.Message.Contains("Could not resolve to a User", StringComparison.OrdinalIgnoreCase) || 
                    error.Message.Contains("Could not resolve to an Organization", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                _logger.LogError("GraphQL error: {Message}", error.Message);
            }
        }
        return gqlResponse != null ? gqlResponse.Data : default;
    }

    public async Task CommentOnPullRequest(string owner, string repo, int prNumber, string comment, CancellationToken cts)
    {
        await EnsureRateLimit(cts).ConfigureAwait(false);
        var url = $"repos/{owner}/{repo}/issues/{prNumber}/comments";
        _logger.LogInformation("Adding comment to PR #{PrNumber} in {Owner}/{Repo}", prNumber, owner, repo);
        
        var result = await _httpClient.PostAsJsonAsync(url, new { body = comment }, Options, cts).ConfigureAwait(false);
        if (result.IsSuccessStatusCode)
        {
            _logger.LogInformation("Successfully linked PR #{PrNumber} to issue", prNumber);
        }
        else
        {
            var error = await result.Content.ReadAsStringAsync(cts).ConfigureAwait(false);
            _logger.LogWarning("Failed to comment on PR #{PrNumber}: {Error}. The PR link will only appear in the issue description.", prNumber, error);
        }
    }
}
