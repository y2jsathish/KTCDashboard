/* ============================================================================
   KTC API Explorer - schema (SQL Server 2016+)
   Generated from the EF Core model (dotnet ef dbcontext script) so it matches the code exactly.
   Run ONCE against an empty database, then run 002_seed_roles.sql. Re-running is refused by the guard below.

   Tables: Roles, Users, ApiCollections, ApiFolders, ApiEndpoints, ApiExecutions, ApiFavorites,
           RequestTemplates, TestCases, AuditLogs
   Regenerate after model changes:
     dotnet tool install --global dotnet-ef --version 8.0.11
     dotnet ef dbcontext script --project src/KtcApiExplorer.Infrastructure --startup-project src/KtcApiExplorer.Web
   ============================================================================ */
SET NOCOUNT ON;
IF OBJECT_ID(N'[dbo].[ApiCollections]', N'U') IS NOT NULL
BEGIN
    RAISERROR(N'Schema already exists - refusing to run 001_schema.sql twice.', 16, 1);
    SET NOEXEC ON;
END
GO

CREATE TABLE [ApiCollections] (
    [Id] int NOT NULL IDENTITY,
    [Name] nvarchar(200) NOT NULL,
    [Description] nvarchar(max) NULL,
    [BaseUrl] nvarchar(500) NOT NULL,
    [SchemaVersion] nvarchar(300) NULL,
    [SourceFileName] nvarchar(260) NULL,
    [VariablesJson] nvarchar(max) NULL,
    [IsActive] bit NOT NULL,
    [ImportedByUserId] int NULL,
    [ImportedUtc] datetime2 NOT NULL,
    [UpdatedUtc] datetime2 NOT NULL,
    CONSTRAINT [PK_ApiCollections] PRIMARY KEY ([Id])
);
GO


CREATE TABLE [AuditLogs] (
    [Id] bigint NOT NULL IDENTITY,
    [UserId] int NULL,
    [UserName] nvarchar(100) NOT NULL,
    [Action] nvarchar(100) NOT NULL,
    [EntityType] nvarchar(100) NULL,
    [EntityId] nvarchar(100) NULL,
    [Details] nvarchar(4000) NULL,
    [IpAddress] nvarchar(64) NULL,
    [CorrelationId] nvarchar(64) NULL,
    [CreatedUtc] datetime2 NOT NULL,
    CONSTRAINT [PK_AuditLogs] PRIMARY KEY ([Id])
);
GO


CREATE TABLE [Roles] (
    [Id] int NOT NULL IDENTITY,
    [Name] nvarchar(50) NOT NULL,
    [Description] nvarchar(250) NULL,
    CONSTRAINT [PK_Roles] PRIMARY KEY ([Id])
);
GO


CREATE TABLE [ApiFolders] (
    [Id] int NOT NULL IDENTITY,
    [CollectionId] int NOT NULL,
    [ParentFolderId] int NULL,
    [Name] nvarchar(200) NOT NULL,
    [Description] nvarchar(max) NULL,
    [SortOrder] int NOT NULL,
    CONSTRAINT [PK_ApiFolders] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_ApiFolders_ApiCollections_CollectionId] FOREIGN KEY ([CollectionId]) REFERENCES [ApiCollections] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_ApiFolders_ApiFolders_ParentFolderId] FOREIGN KEY ([ParentFolderId]) REFERENCES [ApiFolders] ([Id]) ON DELETE NO ACTION
);
GO


CREATE TABLE [Users] (
    [Id] int NOT NULL IDENTITY,
    [UserName] nvarchar(100) NOT NULL,
    [DisplayName] nvarchar(200) NOT NULL,
    [Email] nvarchar(256) NULL,
    [PasswordHash] nvarchar(500) NOT NULL,
    [RoleId] int NOT NULL,
    [IsActive] bit NOT NULL,
    [FailedLoginCount] int NOT NULL,
    [LockoutEndUtc] datetime2 NULL,
    [LastLoginUtc] datetime2 NULL,
    [CreatedUtc] datetime2 NOT NULL,
    CONSTRAINT [PK_Users] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_Users_Roles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [Roles] ([Id]) ON DELETE NO ACTION
);
GO


CREATE TABLE [ApiEndpoints] (
    [Id] int NOT NULL IDENTITY,
    [CollectionId] int NOT NULL,
    [FolderId] int NULL,
    [Name] nvarchar(600) NOT NULL,
    [Method] nvarchar(10) NOT NULL,
    [RouteTemplate] nvarchar(300) NOT NULL,
    [QuerySignature] nvarchar(200) NOT NULL,
    [Description] nvarchar(max) NULL,
    [HeadersJson] nvarchar(max) NULL,
    [QueryParamsJson] nvarchar(max) NULL,
    [PathParamsJson] nvarchar(max) NULL,
    [BodyMode] nvarchar(20) NULL,
    [BodyRaw] nvarchar(max) NULL,
    [BodyLanguage] nvarchar(20) NULL,
    [ExamplesJson] nvarchar(max) NULL,
    [IsActive] bit NOT NULL,
    [IsMonitored] bit NOT NULL,
    [SortOrder] int NOT NULL,
    [CreatedUtc] datetime2 NOT NULL,
    [UpdatedUtc] datetime2 NOT NULL,
    CONSTRAINT [PK_ApiEndpoints] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_ApiEndpoints_ApiCollections_CollectionId] FOREIGN KEY ([CollectionId]) REFERENCES [ApiCollections] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_ApiEndpoints_ApiFolders_FolderId] FOREIGN KEY ([FolderId]) REFERENCES [ApiFolders] ([Id]) ON DELETE NO ACTION
);
GO


CREATE TABLE [ApiExecutions] (
    [Id] bigint NOT NULL IDENTITY,
    [EndpointId] int NULL,
    [UserId] int NULL,
    [Source] nvarchar(20) NOT NULL,
    [Method] nvarchar(10) NOT NULL,
    [Url] nvarchar(2000) NOT NULL,
    [RequestHeadersJson] nvarchar(max) NULL,
    [RequestBody] nvarchar(max) NULL,
    [StatusCode] int NULL,
    [DurationMs] bigint NOT NULL,
    [ResponseSizeBytes] bigint NOT NULL,
    [ResponseHeadersJson] nvarchar(max) NULL,
    [ResponseBody] nvarchar(max) NULL,
    [IsSuccess] bit NOT NULL,
    [ErrorMessage] nvarchar(1000) NULL,
    [ExecutedUtc] datetime2 NOT NULL,
    CONSTRAINT [PK_ApiExecutions] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_ApiExecutions_ApiEndpoints_EndpointId] FOREIGN KEY ([EndpointId]) REFERENCES [ApiEndpoints] ([Id]) ON DELETE SET NULL,
    CONSTRAINT [FK_ApiExecutions_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE SET NULL
);
GO


CREATE TABLE [ApiFavorites] (
    [Id] int NOT NULL IDENTITY,
    [UserId] int NOT NULL,
    [EndpointId] int NOT NULL,
    [CreatedUtc] datetime2 NOT NULL,
    CONSTRAINT [PK_ApiFavorites] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_ApiFavorites_ApiEndpoints_EndpointId] FOREIGN KEY ([EndpointId]) REFERENCES [ApiEndpoints] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_ApiFavorites_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE CASCADE
);
GO


CREATE TABLE [RequestTemplates] (
    [Id] int NOT NULL IDENTITY,
    [UserId] int NOT NULL,
    [EndpointId] int NOT NULL,
    [Name] nvarchar(200) NOT NULL,
    [Description] nvarchar(1000) NULL,
    [RequestJson] nvarchar(max) NOT NULL,
    [IsShared] bit NOT NULL,
    [CreatedUtc] datetime2 NOT NULL,
    [UpdatedUtc] datetime2 NOT NULL,
    CONSTRAINT [PK_RequestTemplates] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_RequestTemplates_ApiEndpoints_EndpointId] FOREIGN KEY ([EndpointId]) REFERENCES [ApiEndpoints] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_RequestTemplates_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE CASCADE
);
GO


CREATE TABLE [TestCases] (
    [Id] int NOT NULL IDENTITY,
    [UserId] int NOT NULL,
    [EndpointId] int NOT NULL,
    [Name] nvarchar(200) NOT NULL,
    [RequestJson] nvarchar(max) NOT NULL,
    [ExpectedStatus] int NULL,
    [ExpectedBodyContains] nvarchar(1000) NULL,
    [BaselineResponse] nvarchar(max) NULL,
    [LastResult] nvarchar(10) NULL,
    [LastMessage] nvarchar(1000) NULL,
    [LastRunUtc] datetime2 NULL,
    [CreatedUtc] datetime2 NOT NULL,
    CONSTRAINT [PK_TestCases] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_TestCases_ApiEndpoints_EndpointId] FOREIGN KEY ([EndpointId]) REFERENCES [ApiEndpoints] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_TestCases_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE CASCADE
);
GO


CREATE UNIQUE INDEX [IX_ApiCollections_Name] ON [ApiCollections] ([Name]);
GO


CREATE UNIQUE INDEX [IX_ApiEndpoints_CollectionId_Method_RouteTemplate_QuerySignature] ON [ApiEndpoints] ([CollectionId], [Method], [RouteTemplate], [QuerySignature]);
GO


CREATE INDEX [IX_ApiEndpoints_FolderId] ON [ApiEndpoints] ([FolderId]);
GO


CREATE INDEX [IX_ApiExecutions_EndpointId_ExecutedUtc] ON [ApiExecutions] ([EndpointId], [ExecutedUtc]);
GO


CREATE INDEX [IX_ApiExecutions_ExecutedUtc] ON [ApiExecutions] ([ExecutedUtc]);
GO


CREATE INDEX [IX_ApiExecutions_UserId_ExecutedUtc] ON [ApiExecutions] ([UserId], [ExecutedUtc]);
GO


CREATE INDEX [IX_ApiFavorites_EndpointId] ON [ApiFavorites] ([EndpointId]);
GO


CREATE UNIQUE INDEX [IX_ApiFavorites_UserId_EndpointId] ON [ApiFavorites] ([UserId], [EndpointId]);
GO


CREATE INDEX [IX_ApiFolders_CollectionId_ParentFolderId_Name] ON [ApiFolders] ([CollectionId], [ParentFolderId], [Name]);
GO


CREATE INDEX [IX_ApiFolders_ParentFolderId] ON [ApiFolders] ([ParentFolderId]);
GO


CREATE INDEX [IX_AuditLogs_CreatedUtc] ON [AuditLogs] ([CreatedUtc]);
GO


CREATE INDEX [IX_AuditLogs_UserName] ON [AuditLogs] ([UserName]);
GO


CREATE INDEX [IX_RequestTemplates_EndpointId] ON [RequestTemplates] ([EndpointId]);
GO


CREATE INDEX [IX_RequestTemplates_UserId_EndpointId] ON [RequestTemplates] ([UserId], [EndpointId]);
GO


CREATE UNIQUE INDEX [IX_Roles_Name] ON [Roles] ([Name]);
GO


CREATE INDEX [IX_TestCases_EndpointId] ON [TestCases] ([EndpointId]);
GO


CREATE INDEX [IX_TestCases_UserId_EndpointId] ON [TestCases] ([UserId], [EndpointId]);
GO


CREATE INDEX [IX_Users_RoleId] ON [Users] ([RoleId]);
GO


CREATE UNIQUE INDEX [IX_Users_UserName] ON [Users] ([UserName]);
GO

GO
SET NOEXEC OFF;
GO
