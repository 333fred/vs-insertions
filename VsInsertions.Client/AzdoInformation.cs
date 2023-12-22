using System.Collections.Immutable;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace VsInsertions
{
    public static class AzdoInformation
    {
        public static async Task<CommitDetails?> GetLastDetailsForRepo(this HttpClient client, string insertionCommitMessageFilter, string vsBranch)
        {
            // https://learn.microsoft.com/en-us/rest/api/azure/devops/git/commits/get-commits?view=azure-devops-rest-7.1&tabs=HTTP#gitcommitref
            var url = $"https://dev.azure.com/devdiv/devdiv/_apis/git/repositories/a290117c-5a8a-40f7-bc2c-f14dbe3acf6d/commits?searchCriteria.itemVersion.version={vsBranch}&searchCriteria.author=DotNet Bot&api-version=7.1";

            var lastCommits = await client.GetStringAsync(url);

            var json = JsonNode.Parse(lastCommits);
            var commits = json!["value"]!.AsArray();

            foreach (var commit in commits)
            {
                string comment = commit!["comment"]!.ToString()!;
                if (comment.Contains(insertionCommitMessageFilter))
                    return new(commit["commitId"]!.ToString()!, DateTimeOffset.Parse(commit["committer"]!["date"]!.ToString()!), comment, commit["remoteUrl"]!.ToString()!);
            }

            // Couldn't find a last insertion date for this branch
            return null;
        }

        public static async Task<ImmutableArray<VsInsertion>> GetInsertionsAsync(this HttpClient client, string vsBranch, StatusFilter statusFilter, int skipEntries, string githubRepository)
        {
            // https://learn.microsoft.com/en-us/rest/api/azure/devops/git/pull-requests/get-pull-requests?view=azure-devops-rest-7.1&tabs=HTTP
            string url = $"https://dev.azure.com/devdiv/devdiv/_apis/git/repositories/a290117c-5a8a-40f7-bc2c-f14dbe3acf6d/pullrequests?searchCriteria.creatorId=122d5278-3e55-4868-9d40-1e28c2515fc4&searchCriteria.reviewerId=6c25b447-1d90-4840-8fde-d8b22cb8733e&api-version=7.1&searchCriteria.status={statusFilter.ToString().ToLowerInvariant()}&$top=100&$skip={skipEntries}&searchCriteria.targetRefName=refs/heads/{vsBranch}";


            Console.WriteLine(url);
            var response = await client.GetStringAsync(url);
            Console.WriteLine("Loaded");

            JsonNode? node;
            node = JsonNode.Parse(response);

            return node!["value"]!.AsArray().Select(x => new VsInsertion(x!)).Where(v => v.Repo == githubRepository).ToImmutableArray();
        }
    }

    public record struct CommitDetails(string? Commit, DateTimeOffset? Date, string Comment, string Url);

    public class VsInsertion(JsonNode node)
    {
        private Match? parsedTitle;

        public bool DisplayJson { get; set; }
        public bool Abandoning { get; private set; }

        public string PullRequestId => node["pullRequestId"]!.ToString();
        public string Url => $"https://dev.azure.com/devdiv/DevDiv/_git/VS/pullrequest/{PullRequestId}";
        public string Title => node["title"]!.ToString();
        public Match ParsedTitle => (parsedTitle ??= Regex.Match(Title, @"(?<repo>\w+) '(?<source>[^']+)/(?<build>[\d.]+)' Insertion into (?<target>.*)"));
        public string Json => node.ToJsonString(new() { WriteIndented = true });
        public PullRequestStatus Status { get; private set; } = Enum.Parse<PullRequestStatus>(node["status"]!.ToString(), ignoreCase: true);
        public bool IsDraft => (bool)node["isDraft"]!;
        public string Repo => ParsedTitle.Groups["repo"].Value;
        public string SourceBranch => ParsedTitle.Groups["source"].Value;
        public string BuildNumber => ParsedTitle.Groups["build"].Value;
        public string TargetBranch => node["targetRefName"]!.ToString();
        public Review[] Reviews { get; } = node["reviewers"]?.AsArray().Select(x => new Review(x!)).ToArray() ?? Array.Empty<Review>();
        public RpsSummary? RpsSummary { get; private set; }

        public void RefreshRpsSummary(HttpClient client)
        {
            RpsSummary = new RpsSummary();
            LoadRpsSummary(RpsSummary, client);
        }

        public async Task AbandonAsync(HttpClient client)
        {
            Abandoning = true;
            try
            {
                var response = await client.PatchAsJsonAsync(
                    $"https://dev.azure.com/devdiv/devdiv/_apis/git/repositories/a290117c-5a8a-40f7-bc2c-f14dbe3acf6d/pullrequests/{PullRequestId}?api-version=7.0",
                    new { status = "abandoned" });
                Console.WriteLine(response);
                Console.WriteLine(await response.Content.ReadAsStringAsync());

                if (response.IsSuccessStatusCode)
                {
                    Status = PullRequestStatus.Abandoned;
                }
            }
            finally
            {
                Abandoning = false;
            }
        }

        private async void LoadRpsSummary(RpsSummary rpsSummary, HttpClient client)
        {
            try
            {
                var json = await client.GetStringAsync($"https://dev.azure.com/devdiv/devdiv/_apis/git/repositories/a290117c-5a8a-40f7-bc2c-f14dbe3acf6d/pullrequests/{PullRequestId}/threads?api-version=6.0");
                var node = JsonNode.Parse(json);
                var threads = node!["value"]!.AsArray();
                rpsSummary.Ddrit = getRunResults(threads, "We've started **VS64** Perf DDRITs");
                rpsSummary.Speedometer = getRunResults(threads, "We've started Speedometer");
                rpsSummary.Loaded = true;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                rpsSummary.Loaded = true;
            }

            static RpsRun? getRunResults(JsonArray threads, string text)
            {
                var latestThread = threads.Where(x => x!["comments"]!.AsArray().Any(x => x!["content"]?.ToString().Contains(text) ?? false)).LastOrDefault();
                if (latestThread == null)
                {
                    return null;
                }

                var latestComment = latestThread["comments"]!.AsArray().Where(x => x!["author"]!["displayName"]!.ToString() == "VSEng Perf Automation Account").LastOrDefault();
                if (latestComment == null)
                {
                    return new RpsRun(InProgress: true, Regressions: 0, BrokenTests: 0);
                }

                var latestText = latestComment["content"]!.ToString();
                if (latestText.Contains("Test Run **PASSED**"))
                {
                    return new RpsRun(InProgress: false, Regressions: 0, BrokenTests: 0);
                }

                return new RpsRun(InProgress: false, Regressions: tryGetCount(latestText, "regression"), BrokenTests: tryGetCount(latestText, "broken test"));
            }

            static int tryGetCount(string text, string label)
            {
                var match = Regex.Match(text, @$"(\d+) {label}");
                if (!match.Success || !int.TryParse(match.Groups[1].Value, out var result))
                {
                    return -1;
                }

                return result;
            }
        }
    }

    public class Review(JsonNode node)
    {
        public string DisplayName => node["displayName"]!.ToString();
        public string ImageUrl => node["imageUrl"]!.ToString();
        public Vote Vote => (Vote)(int)node["vote"]!;
    }

    public class RpsSummary
    {
        public bool Loaded { get; set; }
        public RpsRun? Ddrit { get; set; }
        public RpsRun? Speedometer { get; set; }
    }

    public record RpsRun(bool InProgress, int Regressions, int BrokenTests);

    public enum PullRequestStatus
    {
        Abandoned,
        Active,
        Completed
    }

    public enum StatusFilter
    {
        All,
        Active
    }

    public enum Vote
    {
        Approved = 10,
        ApprovedWithSuggestions = 5,
        NoVote = 0,
        WaitingForAuthor = -5,
        Rejected = -10,
    }
}
