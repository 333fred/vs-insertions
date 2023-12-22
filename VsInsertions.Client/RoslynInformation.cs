using System.Collections.Immutable;
using System.Text.Json.Nodes;

namespace VsInsertions;

public record class RoslynInformation(ImmutableArray<BranchInformation> Branches) : IRepositoryInformation
{
    private const string publishJsonPath = @"https://raw.githubusercontent.com/dotnet/roslyn/main/eng/config/PublishData.json";

    public static async Task<RoslynInformation> CreateAsync(HttpClient client)
    {
        var response = await client.GetStringAsync(publishJsonPath);
        var json = JsonNode.Parse(response);

        var branches = (JsonObject)json!["branches"]!;
        var builder = ImmutableArray.CreateBuilder<BranchInformation>();
        foreach (var branch in branches.AsEnumerable())
        {
            string roslynBranch = branch.Key!;
            string vsBranch = branch.Value!["vsBranch"]!.ToString()!;

            // TODO: Dev16 was a different committer, so we'd need to do something different for that if we want to support it
            if (roslynBranch == "main" || (roslynBranch.StartsWith("release/dev") && !roslynBranch.StartsWith("release/dev16")))
                builder.Add(new BranchInformation(branch.Key, vsBranch));
        }

        builder.Sort((a, b) =>
        {
            // Put main at the top, then sort by name descending to put most recent branches first in the list
            if (a.GitHubBranch == "main")
                return -1;

            return GetVersionNumber(b).CompareTo(GetVersionNumber(a));

            static Version GetVersionNumber(BranchInformation a)
            {
                ReadOnlySpan<char> versionSpan = a.GitHubBranch.AsSpan()["release/dev".Length..];
                if (versionSpan.IndexOf('-') is > 0 and var dash)
                    versionSpan = versionSpan[..dash];
                return Version.Parse(versionSpan);
            }
        });

        return new(builder.DrainToImmutable());
    }

    public string RepositoryName => "Roslyn";
}
