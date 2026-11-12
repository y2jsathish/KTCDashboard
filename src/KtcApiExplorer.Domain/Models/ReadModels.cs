namespace KtcApiExplorer.Domain.Models;

public record MethodCount(string Method, int Count);
public record UsageByEndpoint(int? EndpointId, string Name, int Calls);
public record DailyStat(DateTime Day, int Total, int Failures, double AvgMs);
public record EndpointHealth(int EndpointId, string Name, string Method, string Route, int? LastStatus, bool? LastSuccess, long? LastDurationMs, DateTime? LastCheckedUtc, double AvgMs, int Samples, int Failures);
public record ExecutionFilter(int? EndpointId = null, int? UserId = null, bool? Success = null, string? Source = null, int Skip = 0, int Take = 50);
