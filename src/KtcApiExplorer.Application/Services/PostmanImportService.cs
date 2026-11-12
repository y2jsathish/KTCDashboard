using KtcApiExplorer.Application.Abstractions;
using KtcApiExplorer.Application.Common;
using KtcApiExplorer.Application.Dtos;
using KtcApiExplorer.Application.Options;
using KtcApiExplorer.Application.Parsing;
using KtcApiExplorer.Domain.Entities;
using KtcApiExplorer.Domain.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace KtcApiExplorer.Application.Services;

public class PostmanImportService : IPostmanImportService
{
    readonly ICollectionRepository _repo; readonly IUnitOfWork _uow; readonly IAuditService _audit; readonly ICurrentUser _user;
    readonly KtcOptions _ktc; readonly ILogger<PostmanImportService> _log;

    public PostmanImportService(ICollectionRepository repo, IUnitOfWork uow, IAuditService audit, ICurrentUser user, IOptions<KtcOptions> ktc, ILogger<PostmanImportService> log)
    { _repo = repo; _uow = uow; _audit = audit; _user = user; _ktc = ktc.Value; _log = log; }

    public async Task<ImportResultDto> ImportAsync(ImportRequest req, CancellationToken ct = default)
    {
        var parsed = PostmanCollectionParser.Parse(req.CollectionJson);
        if (parsed.Endpoints.Count == 0) throw new ValidationException("The collection contains no requests.");

        var result = new ImportResultDto { Warnings = parsed.Warnings };

        // Optional help document -> real controller names + descriptions
        Dictionary<string, HelpDocEntry>? byKey = null;
        if (!string.IsNullOrWhiteSpace(req.HelpHtml))
        {
            var entries = KtcHelpDocParser.Parse(req.HelpHtml);
            if (entries.Count == 0) result.Warnings.Add("The help document was supplied but no API tables were recognised; it was ignored.");
            byKey = new Dictionary<string, HelpDocEntry>();
            foreach (var e in entries)
            {
                var (m, r) = KtcHelpDocParser.SplitApi(e.Api);
                byKey.TryAdd(KtcHelpDocParser.Key(m, r), e);
            }
        }

        // Folder assignment: Postman folders > help-doc group > 3rd route segment (api/v1/<Controller>) > "General"
        foreach (var ep in parsed.Endpoints)
        {
            if (byKey != null && byKey.TryGetValue(KtcHelpDocParser.Key(ep.Method, ep.Route, ep.Query.Select(q => q.Key)), out var doc))
            {
                if (ep.FolderPath.Count == 0) ep.FolderPath.Add(doc.Group);
                if (string.IsNullOrWhiteSpace(ep.Description) && doc.Description.Length > 0) { ep.Description = doc.Description; result.DescriptionsFromHelpDoc++; }
            }
            if (ep.FolderPath.Count == 0) ep.FolderPath.Add(GuessGroup(ep.Route));
        }

        var name = string.IsNullOrWhiteSpace(req.CollectionName) ? parsed.Name : req.CollectionName.Trim();
        var baseUrl = (req.BaseUrl ?? parsed.DetectedBaseUrl ?? _ktc.DefaultBaseUrl).Trim();
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var bu) || (bu.Scheme != "http" && bu.Scheme != "https"))
            throw new ValidationException("Base URL must be an absolute http(s) URL.");

        var col = await _repo.GetByNameAsync(name, ct);
        result.Updated = col != null;
        if (col == null) { col = new ApiCollection { Name = name, ImportedByUserId = _user.UserId }; _repo.Add(col); }
        col.Description = parsed.Description; col.BaseUrl = baseUrl.TrimEnd('/'); col.SchemaVersion = parsed.SchemaVersion;
        col.SourceFileName = req.FileName; col.VariablesJson = JsonConvert.SerializeObject(parsed.Variables);
        col.UpdatedUtc = DateTime.UtcNow; col.IsActive = true;

        // Upsert folders (flat+nested by path), keyed by path
        var existingFolders = col.Id == 0 ? new List<ApiFolder>() : await _repo.GetFoldersAsync(col.Id, ct);
        var folderByPath = new Dictionary<string, ApiFolder>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in existingFolders) folderByPath[FolderPathOf(f, existingFolders)] = f;
        int order = 0;
        ApiFolder EnsureFolder(List<string> path)
        {
            ApiFolder? parent = null;
            for (int i = 0; i < path.Count; i++)
            {
                var key = string.Join(" / ", path.Take(i + 1));
                if (!folderByPath.TryGetValue(key, out var f))
                {
                    f = new ApiFolder { Collection = col, ParentFolder = parent, Name = path[i], SortOrder = order++ };
                    _repo.AddFolder(f); folderByPath[key] = f;
                }
                parent = f;
            }
            return parent!;
        }

        var existing = col.Id == 0 ? new List<ApiEndpoint>() : await _repo.GetEndpointsAsync(col.Id, includeInactive: true, ct);
        var epByKey = existing.GroupBy(e => Identity(e.Method, e.RouteTemplate, e.QuerySignature)).ToDictionary(g => g.Key, g => g.First());
        var seen = new HashSet<string>();
        int sort = 0;
        foreach (var p in parsed.Endpoints)
        {
            var key = Identity(p.Method, p.Route, p.QuerySignature);
            if (!seen.Add(key)) { result.Warnings.Add($"Duplicate endpoint ignored: {p.Name}"); continue; }
            var folder = EnsureFolder(p.FolderPath);
            var headers = JsonConvert.SerializeObject(p.Headers);
            if (!epByKey.TryGetValue(key, out var e))
            {
                e = new ApiEndpoint { Collection = col, Method = p.Method, RouteTemplate = p.Route, QuerySignature = p.QuerySignature };
                _repo.AddEndpoint(e); result.Added++;
            }
            else if (e.BodyRaw != p.BodyRaw || e.Description != p.Description || e.Folder?.Name != folder.Name || !e.IsActive) result.Changed++;
            e.Folder = folder; e.Name = p.Name; e.Description = p.Description; e.HeadersJson = headers;
            e.QueryParamsJson = JsonConvert.SerializeObject(p.Query); e.PathParamsJson = JsonConvert.SerializeObject(p.PathParams);
            e.BodyMode = p.BodyMode; e.BodyRaw = p.BodyRaw; e.BodyLanguage = p.BodyLanguage;
            e.ExamplesJson = p.Examples.Count > 0 ? JsonConvert.SerializeObject(p.Examples) : null;
            e.IsActive = true; e.SortOrder = sort++; e.UpdatedUtc = DateTime.UtcNow;
        }
        foreach (var gone in existing.Where(e => e.IsActive && !seen.Contains(Identity(e.Method, e.RouteTemplate, e.QuerySignature))))
        { gone.IsActive = false; result.Deactivated++; }

        await _uow.SaveChangesAsync(ct);
        result.CollectionId = col.Id; result.CollectionName = col.Name;
        result.Folders = folderByPath.Count; result.Endpoints = seen.Count;
        await _audit.LogAsync("Collection.Import", "ApiCollection", col.Id.ToString(),
            $"{col.Name}: {result.Endpoints} endpoints (+{result.Added}, ~{result.Changed}, -{result.Deactivated}) from '{req.FileName}'", ct);
        _log.LogInformation("Imported collection {Name}: {Endpoints} endpoints in {Folders} folders", col.Name, result.Endpoints, result.Folders);
        return result;
    }

    static string Identity(string method, string route, string sig) => method + " " + route.ToLowerInvariant() + "?" + sig;

    static string FolderPathOf(ApiFolder f, List<ApiFolder> all)
    {
        var parts = new List<string> { f.Name };
        var p = f.ParentFolderId;
        while (p != null) { var pf = all.First(x => x.Id == p); parts.Insert(0, pf.Name); p = pf.ParentFolderId; }
        return string.Join(" / ", parts);
    }

    static string GuessGroup(string route)
    {
        var seg = route.Split('/', StringSplitOptions.RemoveEmptyEntries).Where(s => !s.StartsWith('{')).ToList();
        // api/v1/<Controller>/...
        if (seg.Count >= 3 && seg[0].Equals("api", StringComparison.OrdinalIgnoreCase)) return seg[2];
        return seg.Count > 0 ? seg[0] : "General";
    }
}
