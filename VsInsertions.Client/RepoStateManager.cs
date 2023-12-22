using System.Collections.Immutable;

namespace VsInsertions;

public class RepoStateManager(HttpClient client)
{
    private string? _cookie;
    private RoslynInformation? _roslynInformation;

    public void SetCookie(string? cookie)
    {
        if (_cookie == cookie)
            return;

        if (_cookie is not null)
            client.DefaultRequestHeaders.Remove("Cookie");

        _cookie = cookie;
        client.DefaultRequestHeaders.Add("Cookie", cookie);
    }

    public async Task<RoslynInformation?> GetRoslynInformationAsync()
    {
        if (_roslynInformation is null && _cookie is not null)
            _roslynInformation = await RoslynInformation.CreateAsync(client);

        return _roslynInformation;
    }

    public async Task<ImmutableArray<VsInsertion>> GetInsertionsAsync(string vsBranch, StatusFilter statusFilter, int skipEntries, string githubRepository)
    {
        if (_cookie is null)
            return [];

        return await client.GetInsertionsAsync(vsBranch, statusFilter, skipEntries, githubRepository);
    }

    public async Task<CommitDetails?> GetLastDetailsForRepo(string insertionCommitMessageFilter, string vsBranch)
    {
        if (_cookie is null)
            return default;

        return await client.GetLastDetailsForRepo(insertionCommitMessageFilter, vsBranch);
    }
}
