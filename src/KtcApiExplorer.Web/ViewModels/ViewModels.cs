using System.ComponentModel.DataAnnotations;
using KtcApiExplorer.Application.Dtos;

namespace KtcApiExplorer.Web.ViewModels;

public class LoginViewModel
{
    [Required, StringLength(100)] public string UserName { get; set; } = "";
    [Required, DataType(DataType.Password), StringLength(200)] public string Password { get; set; } = "";
    public string? ReturnUrl { get; set; }
    public string? Error { get; set; }
}
public class ErrorViewModel { public int Code { get; set; } public string Title { get; set; } = ""; public string? CorrelationId { get; set; } }
public class KtcLoginViewModel { public List<CollectionSummaryDto> Collections { get; set; } = new(); public KtcStatusDto Status { get; set; } = new(); }
public class CollectionsViewModel { public List<CollectionSummaryDto> Collections { get; set; } = new(); public ImportResultDto? LastImport { get; set; } }
public class ImportFormModel
{
    [Required] public IFormFile? CollectionFile { get; set; }
    public IFormFile? HelpFile { get; set; }
    [StringLength(500)] public string? BaseUrl { get; set; }
    [StringLength(200)] public string? CollectionName { get; set; }
}
public class UsersViewModel { public List<UserDto> Users { get; set; } = new(); public List<string> Roles { get; set; } = new(); }
public class AuditViewModel { public PagedResult<AuditLogDto> Page { get; set; } = new(); public string? Action { get; set; } public string? User { get; set; } public int Skip { get; set; } public int Take { get; set; } }
