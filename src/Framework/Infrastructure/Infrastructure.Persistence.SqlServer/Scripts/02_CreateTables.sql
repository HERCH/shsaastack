-- =============================================
-- Script: Create SaaStack Tables
-- Description: Creates the necessary tables for data storage and event sourcing
-- Prerequisites: Database must exist (run 01_CreateDatabase.sql first)
-- =============================================

USE [SaaStack];
GO

-- =============================================
-- Event Streams Table (for Event Sourcing)
-- =============================================
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[EventStreams]') AND type in (N'U'))
BEGIN
    CREATE TABLE [dbo].[EventStreams] (
        [Id] BIGINT IDENTITY(1,1) PRIMARY KEY,
        [AggregateRootId] NVARCHAR(100) NOT NULL,
        [AggregateType] NVARCHAR(200) NOT NULL,
        [EventType] NVARCHAR(200) NOT NULL,
        [EventData] NVARCHAR(MAX) NOT NULL,
        [Version] INT NOT NULL,
        [CreatedAtUtc] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        CONSTRAINT [UQ_EventStreams_AggregateVersion] UNIQUE ([AggregateRootId], [Version])
    );

    CREATE INDEX [IX_EventStreams_AggregateRootId] ON [dbo].[EventStreams]([AggregateRootId]);
    CREATE INDEX [IX_EventStreams_AggregateType] ON [dbo].[EventStreams]([AggregateType]);
    CREATE INDEX [IX_EventStreams_CreatedAtUtc] ON [dbo].[EventStreams]([CreatedAtUtc]);

    PRINT 'Table [EventStreams] created successfully.';
END
ELSE
BEGIN
    PRINT 'Table [EventStreams] already exists.';
END
GO

-- =============================================
-- Create a stored procedure to create container tables dynamically
-- =============================================
IF EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[sp_CreateContainerTable]') AND type in (N'P', N'PC'))
BEGIN
    DROP PROCEDURE [dbo].[sp_CreateContainerTable];
END
GO

CREATE PROCEDURE [dbo].[sp_CreateContainerTable]
    @ContainerName NVARCHAR(128)
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Sql NVARCHAR(MAX);
    DECLARE @QuotedName NVARCHAR(130) = QUOTENAME(@ContainerName);

    -- Check if table already exists
    IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(@QuotedName) AND type in (N'U'))
    BEGIN
        SET @Sql = N'
            CREATE TABLE ' + @QuotedName + ' (
                [Id] NVARCHAR(100) PRIMARY KEY,
                [Data] NVARCHAR(MAX) NOT NULL,
                [IsDeleted] BIT NOT NULL DEFAULT 0,
                [LastPersistedAtUtc] DATETIME2 NOT NULL DEFAULT GETUTCDATE()
            );

            CREATE INDEX IX_' + @ContainerName + '_IsDeleted ON ' + @QuotedName + '([IsDeleted]);
            CREATE INDEX IX_' + @ContainerName + '_LastPersistedAtUtc ON ' + @QuotedName + '([LastPersistedAtUtc]);
        ';

        EXEC sp_executesql @Sql;

        PRINT 'Container table [' + @ContainerName + '] created successfully.';
    END
    ELSE
    BEGIN
        PRINT 'Container table [' + @ContainerName + '] already exists.';
    END
END
GO

-- =============================================
-- Create common container tables
-- =============================================
EXEC [dbo].[sp_CreateContainerTable] 'Users';
EXEC [dbo].[sp_CreateContainerTable] 'Organizations';
EXEC [dbo].[sp_CreateContainerTable] 'Subscriptions';
EXEC [dbo].[sp_CreateContainerTable] 'Bookings';
EXEC [dbo].[sp_CreateContainerTable] 'Cars';
EXEC [dbo].[sp_CreateContainerTable] 'Images';
EXEC [dbo].[sp_CreateContainerTable] 'EndUsers';
EXEC [dbo].[sp_CreateContainerTable] 'UserProfiles';
EXEC [dbo].[sp_CreateContainerTable] 'Identities';
EXEC [dbo].[sp_CreateContainerTable] 'Ancillaries';
EXEC [dbo].[sp_CreateContainerTable] 'EventNotifications';
GO

PRINT 'Table creation complete.';
GO
