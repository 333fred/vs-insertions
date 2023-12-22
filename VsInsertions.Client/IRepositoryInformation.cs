using System.Collections.Immutable;

namespace VsInsertions;

public interface IRepositoryInformation
{
    string RepositoryName { get; }
    ImmutableArray<BranchInformation> Branches { get; }
}

public record struct BranchInformation(string GitHubBranch, string VsBranch);
