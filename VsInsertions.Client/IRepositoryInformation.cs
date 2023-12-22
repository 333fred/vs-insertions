using System.Collections.Immutable;

namespace VsInsertions;

public interface IRepositoryInformation
{
    string RepositoryName { get; }
    ImmutableArray<BranchInformation> Branches { get; }
}

public record class BranchInformation(string GitHubBranch, string VsBranch);
