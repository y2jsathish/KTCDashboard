using KtcApiExplorer.Application.Abstractions;
using KtcApiExplorer.Application.Common;
using KtcApiExplorer.Application.Dtos;
using KtcApiExplorer.Domain.Entities;
using KtcApiExplorer.Domain.Interfaces;
using Newtonsoft.Json;

namespace KtcApiExplorer.Application.Services;

public class ExplorerService : IExplorerService
{
    readonly ICollectionRepository _cols; readonly IEndpointRepository _eps; readonly IFavoriteRepository _favs;
    readonly ICurrentUser _user; readonly IUnitOfWork _uow; readonly IAuditService _audit;

    public ExplorerService(ICollectionRepository cols, IEndpointRepository eps, IFavoriteRepository favs, ICurrentUser user, IUnitOfWork uow, IAuditService audit)
    { _cols = cols; _eps = eps; _favs = favs; _user = user; _uow = uow; _audit = audit; }

    public async Task<List<CollectionSummaryDto>> GetCollectionsAsync(CancellationToken ct = default)
    {
        var list = await _cols.ListAsync(ct);
        var result = new List<CollectionSummaryDto>();
        foreach (var c in list)
        {
            var eps = await _cols.GetEndpointsAsync(c.Id, false, ct);
            result.Add(new CollectionSummaryDto { Id = c.Id, Name = c.Name, BaseUrl = c.BaseUrl, EndpointCount = eps.Count, FolderCount = eps.Select(e => e.FolderId).Distinct().Count(), UpdatedUtc = c.UpdatedUtc, SourceFileName = c.SourceFileName });
        }
        return result;
    }

    public async Task<List<TreeFolderDto>> GetTreeAsync(int collectionId, CancellationToken ct = default)
    {
        var folders = await _cols.GetFoldersAsync(collectionId, ct);
        var eps = await _cols.GetEndpointsAsync(collectionId, false, ct);
        var favs = _user.UserId is int uid ? (await _favs.GetEndpointIdsAsync(uid, ct)).ToHashSet() : new HashSet<int>();
        var byFolder = eps.GroupBy(e => e.FolderId ?? 0).ToDictionary(g => g.Key, g => g.ToList());
        var tree = new List<TreeFolderDto>();
        foreach (var f in folders.OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (!byFolder.TryGetValue(f.Id, out var list)) continue; // hide empty (parent-only) folders
            tree.Add(new TreeFolderDto
            {
                Id = f.Id, Name = FullName(f, folders),
                Endpoints = list.OrderBy(e => e.RouteTemplate, StringComparer.OrdinalIgnoreCase).ThenBy(e => e.Method).ThenBy(e => e.QuerySignature).Select(e => new TreeEndpointDto
                {
                    Id = e.Id, Method = e.Method, Route = RequestBuilder.DisplayRoute(e),
                    Summary = Truncate(e.Description, 160), IsFavorite = favs.Contains(e.Id)
                }).ToList()
            });
        }
        if (byFolder.TryGetValue(0, out var loose))
            tree.Add(new TreeFolderDto { Id = 0, Name = "(Ungrouped)", Endpoints = loose.Select(e => new TreeEndpointDto { Id = e.Id, Method = e.Method, Route = RequestBuilder.DisplayRoute(e), Summary = Truncate(e.Description, 160), IsFavorite = favs.Contains(e.Id) }).ToList() });
        return tree;
    }

    public async Task<EndpointDetailDto> GetEndpointAsync(int endpointId, CancellationToken ct = default)
    {
        var e = await _eps.GetWithCollectionAsync(endpointId, ct) ?? throw new NotFoundException("Endpoint not found.");
        var folders = await _cols.GetFoldersAsync(e.CollectionId, ct);
        var folderName = e.FolderId is int fid && folders.FirstOrDefault(f => f.Id == fid) is { } f ? FullName(f, folders) : "";
        var favs = _user.UserId is int uid ? await _favs.GetEndpointIdsAsync(uid, ct) : new List<int>();
        var body = e.BodyRaw;
        if (e.BodyMode == "raw" && !string.IsNullOrWhiteSpace(body)) body = TryPretty(body);
        return new EndpointDetailDto
        {
            Id = e.Id, CollectionId = e.CollectionId, Folder = folderName, Name = e.Name, Method = e.Method, Route = e.RouteTemplate,
            BaseUrl = e.Collection?.BaseUrl ?? "", DisplayRoute = RequestBuilder.DisplayRoute(e), Description = e.Description ?? "",
            PathParams = RequestBuilder.ReadList(e.PathParamsJson), Query = RequestBuilder.ReadList(e.QueryParamsJson),
            Headers = RequestBuilder.ReadList(e.HeadersJson).Where(h => !h.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase)).ToList(),
            BodyMode = e.BodyMode, Body = body,
            Examples = string.IsNullOrEmpty(e.ExamplesJson) ? new() : JsonConvert.DeserializeObject<List<ExampleDto>>(e.ExamplesJson) ?? new(),
            IsFavorite = favs.Contains(e.Id), IsMonitored = e.IsMonitored,
            RequiresBody = e.Method is "POST" or "PUT" or "PATCH"
        };
    }

    public async Task DeleteCollectionAsync(int collectionId, CancellationToken ct = default)
    {
        var c = await _cols.GetAsync(collectionId, ct) ?? throw new NotFoundException("Collection not found.");
        // Soft-retire: keeps execution history intact (FK from ApiExecutions is SET NULL on hard delete).
        c.IsActive = false; c.UpdatedUtc = DateTime.UtcNow;
        await _uow.SaveChangesAsync(ct);
        await _audit.LogAsync("Collection.Deactivate", "ApiCollection", c.Id.ToString(), c.Name, ct);
    }

    public async Task UpdateBaseUrlAsync(int collectionId, string baseUrl, CancellationToken ct = default)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var u) || (u.Scheme != "http" && u.Scheme != "https"))
            throw new ValidationException("Base URL must be an absolute http(s) URL.");
        var c = await _cols.GetAsync(collectionId, ct) ?? throw new NotFoundException("Collection not found.");
        c.BaseUrl = baseUrl.TrimEnd('/'); c.UpdatedUtc = DateTime.UtcNow;
        await _uow.SaveChangesAsync(ct);
        await _audit.LogAsync("Collection.BaseUrl", "ApiCollection", c.Id.ToString(), c.BaseUrl, ct);
    }

    static string FullName(ApiFolder f, List<ApiFolder> all)
    {
        var parts = new List<string> { f.Name }; var p = f.ParentFolderId; int guard = 0;
        while (p != null && guard++ < 20) { var pf = all.FirstOrDefault(x => x.Id == p); if (pf == null) break; parts.Insert(0, pf.Name); p = pf.ParentFolderId; }
        return string.Join(" / ", parts);
    }
    static string? Truncate(string? s, int n) => s == null ? null : s.Length <= n ? s : s[..n] + "…";
    internal static string TryPretty(string json)
    {
        try { return Newtonsoft.Json.Linq.JToken.Parse(json).ToString(Formatting.Indented); } catch { return json; }
    }
}
