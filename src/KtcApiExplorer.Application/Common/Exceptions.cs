namespace KtcApiExplorer.Application.Common;

/// <summary>Base for errors that are safe to show to the caller.</summary>
public class AppException : Exception
{
    public string Code { get; }
    public AppException(string code, string message, Exception? inner = null) : base(message, inner) => Code = code;
}
public class ValidationException : AppException { public ValidationException(string message) : base("validation_error", message) { } }
public class NotFoundException : AppException { public NotFoundException(string message) : base("not_found", message) { } }
public class ForbiddenException : AppException { public ForbiddenException(string message) : base("forbidden", message) { } }
public class KtcLoginRequiredException : AppException { public KtcLoginRequiredException(string message = "KTC login required. Please sign in to the KTC API first.") : base("ktc_login_required", message) { } }
