/* Idempotent role seed. The first administrator is created by the application on first start
   (password from Seed:AdminPassword, or generated and printed once to the console) because the
   PBKDF2 hash cannot be produced portably in T-SQL. */
SET NOCOUNT ON;
MERGE [Roles] AS t
USING (VALUES
    (N'Admin',     N'Import collections, manage users, view audit log'),
    (N'Developer', N'Execute any API, save templates and test cases'),
    (N'Viewer',    N'Browse and execute read-only (GET/HEAD) requests')
) AS s ([Name], [Description]) ON t.[Name] = s.[Name]
WHEN NOT MATCHED THEN INSERT ([Name], [Description]) VALUES (s.[Name], s.[Description])
WHEN MATCHED AND ISNULL(t.[Description], N'') <> s.[Description] THEN UPDATE SET [Description] = s.[Description];
GO
