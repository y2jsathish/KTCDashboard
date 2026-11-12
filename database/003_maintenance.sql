/* Optional housekeeping - schedule with SQL Agent. ApiExecutions grows with every call.
   Adjust the retention to your compliance requirements; AuditLogs is deliberately NOT purged here. */
DECLARE @keepDays int = 90;
DELETE FROM [ApiExecutions] WHERE [ExecutedUtc] < DATEADD(DAY, -@keepDays, SYSUTCDATETIME());
GO
/* Useful reports */
-- Slowest endpoints, last 7 days
SELECT TOP 20 e.[Method], e.[RouteTemplate], COUNT(*) AS Calls, AVG(x.[DurationMs]) AS AvgMs, MAX(x.[DurationMs]) AS MaxMs
FROM [ApiExecutions] x JOIN [ApiEndpoints] e ON e.[Id] = x.[EndpointId]
WHERE x.[ExecutedUtc] >= DATEADD(DAY, -7, SYSUTCDATETIME())
GROUP BY e.[Method], e.[RouteTemplate] ORDER BY AVG(x.[DurationMs]) DESC;
-- Error rate per day
SELECT CAST([ExecutedUtc] AS date) AS Day, COUNT(*) AS Total, SUM(CASE WHEN [IsSuccess] = 0 THEN 1 ELSE 0 END) AS Failures
FROM [ApiExecutions] GROUP BY CAST([ExecutedUtc] AS date) ORDER BY Day DESC;
GO
