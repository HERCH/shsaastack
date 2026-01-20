# Infrastructure.Persistence.SqlServer

SQL Server persistence implementation for SaaStack on-premise deployments.

## Overview

This project provides production-ready persistence implementations for on-premise deployments using:
- **SQL Server** (2019+) or **Azure SQL Database** for data and event storage
- **Local File System** for blob storage

## Features

✅ **IDataStore** - Entity persistence with JSON document storage
✅ **IEventStore** - Event sourcing with full event stream support
✅ **IBlobStore** - File system-based blob storage
✅ **Health Checks** - Database connectivity monitoring
✅ **Multi-Tenancy** - Supports both single-tenant and multi-tenant configurations

## Architecture

### Data Storage (IDataStore)
- Stores entities as JSON documents in SQL Server tables
- Each aggregate root type gets its own table (container)
- Tables are created automatically on first use
- Supports CRUD operations with soft deletes
- Optimized for query performance with indexes

### Event Storage (IEventStore)
- Stores events in a dedicated `EventStreams` table
- Preserves event ordering with version numbers
- Supports event replay and aggregate reconstruction
- Enables full Event Sourcing patterns

### Blob Storage (IBlobStore)
- Stores binary data on local file system
- Organizes files by container name
- Stores content type metadata separately
- Supports streaming for large files

## Database Schema

### EventStreams Table
```sql
CREATE TABLE [EventStreams] (
    [Id] BIGINT IDENTITY PRIMARY KEY,
    [AggregateRootId] NVARCHAR(100) NOT NULL,
    [AggregateType] NVARCHAR(200) NOT NULL,
    [EventType] NVARCHAR(200) NOT NULL,
    [EventData] NVARCHAR(MAX) NOT NULL,
    [Version] INT NOT NULL,
    [CreatedAtUtc] DATETIME2 NOT NULL
)
```

### Container Tables (Dynamic)
```sql
CREATE TABLE [ContainerName] (
    [Id] NVARCHAR(100) PRIMARY KEY,
    [Data] NVARCHAR(MAX) NOT NULL,
    [IsDeleted] BIT NOT NULL DEFAULT 0,
    [LastPersistedAtUtc] DATETIME2 NOT NULL
)
```

## Setup Instructions

### 1. SQL Server Prerequisites

**Minimum Requirements:**
- SQL Server 2019 or later (Express, Standard, or Enterprise)
- OR Azure SQL Database (S0 tier or higher)
- SQL Server Authentication or Windows Authentication
- CREATE DATABASE permissions for initial setup

**Recommended:**
- SQL Server 2022 for best performance
- Standard Edition or higher for production
- Dedicated database server or VM
- At least 4GB RAM allocated to SQL Server
- SSD storage for best I/O performance

### 2. Database Setup

#### Option A: Automated Setup (Recommended)

Run the provided SQL scripts in order:

```powershell
# Using sqlcmd (Windows/Linux)
sqlcmd -S sql.internal.company.com -U saastack_admin -P YourPassword -i Scripts/01_CreateDatabase.sql
sqlcmd -S sql.internal.company.com -U saastack_admin -P YourPassword -i Scripts/02_CreateTables.sql
```

```bash
# Using Azure Data Studio or SQL Server Management Studio
# 1. Connect to your SQL Server instance
# 2. Open and execute Scripts/01_CreateDatabase.sql
# 3. Open and execute Scripts/02_CreateTables.sql
```

#### Option B: Manual Setup

1. Create the database:
```sql
CREATE DATABASE [SaaStack];
```

2. Create application user:
```sql
USE [SaaStack];
CREATE LOGIN [saastack_app] WITH PASSWORD = 'YourSecurePassword123!';
CREATE USER [saastack_app] FOR LOGIN [saastack_app];
GRANT SELECT, INSERT, UPDATE, DELETE, EXECUTE ON SCHEMA::dbo TO [saastack_app];
```

3. Run the table creation script:
```sql
-- Run Scripts/02_CreateTables.sql
```

### 3. Configure Connection String

Update `appsettings.OnPremise.json`:

```json
{
  "ApplicationServices": {
    "Persistence": {
      "SqlServer": {
        "Enabled": true,
        "ConnectionString": "Server=sql.internal.company.com;Database=SaaStack;User Id=saastack_app;Password=YOUR_PASSWORD;TrustServerCertificate=True;Encrypt=True;",
        "CommandTimeout": 30
      },
      "BlobStorage": {
        "FileSystem": {
          "RootPath": "/var/saastack/blobs"
        }
      }
    }
  }
}
```

**Connection String Parameters:**
- `Server`: SQL Server hostname or IP address
- `Database`: Always "SaaStack"
- `User Id`: Application database user
- `Password`: User password (use environment variables in production!)
- `TrustServerCertificate`: Set to True for self-signed certificates
- `Encrypt`: Set to True to encrypt connections (recommended)
- `MultipleActiveResultSets`: Optional, set to True if needed
- `ConnectTimeout`: Optional, default is 15 seconds

### 4. Configure Blob Storage

Create the blob storage directory:

```bash
# Linux/macOS
sudo mkdir -p /var/saastack/blobs
sudo chown appuser:appuser /var/saastack/blobs
sudo chmod 750 /var/saastack/blobs
```

```powershell
# Windows
New-Item -Path "C:\saastack\blobs" -ItemType Directory -Force
# Grant permissions to the application service account
icacls "C:\saastack\blobs" /grant "IIS_IUSRS:(OI)(CI)F"
```

### 5. Register Services

The SQL Server stores are automatically registered when enabled in configuration. In `HostExtensions.cs`:

```csharp
// Register SQL Server store if enabled in configuration
services.RegisterSqlServerStoreIfEnabled(appBuilder.Configuration, isMultiTenanted);
```

## Configuration Options

### SQL Server Settings

| Setting | Type | Default | Description |
|---------|------|---------|-------------|
| `Enabled` | bool | false | Enable SQL Server persistence |
| `ConnectionString` | string | - | SQL Server connection string |
| `CommandTimeout` | int | 30 | Command timeout in seconds |

### Blob Storage Settings

| Setting | Type | Default | Description |
|---------|------|---------|-------------|
| `FileSystem.RootPath` | string | - | Root directory for blob files |

## Production Considerations

### Security

1. **Never hardcode passwords** in configuration files
   ```bash
   # Use environment variables
   export ApplicationServices__Persistence__SqlServer__ConnectionString="Server=...;Password=SecurePassword;"
   ```

2. **Use SQL Server Authentication** with strong passwords
   - Minimum 12 characters
   - Mix of uppercase, lowercase, numbers, symbols
   - Rotate passwords regularly

3. **Enable SSL/TLS** for database connections
   ```
   Encrypt=True;TrustServerCertificate=False;
   ```

4. **Restrict database user permissions**
   - Grant only necessary permissions (SELECT, INSERT, UPDATE, DELETE)
   - Do NOT grant db_owner or sysadmin roles
   - Use separate users for application and maintenance

5. **Secure blob storage directory**
   ```bash
   chmod 750 /var/saastack/blobs
   chown appuser:appgroup /var/saastack/blobs
   ```

### Performance

1. **Database Maintenance**
   ```sql
   -- Regular index maintenance
   ALTER INDEX ALL ON [dbo].[EventStreams] REBUILD;

   -- Update statistics
   UPDATE STATISTICS [dbo].[EventStreams] WITH FULLSCAN;
   ```

2. **Monitor query performance**
   - Enable Query Store for SQL Server 2016+
   - Monitor long-running queries
   - Add indexes based on query patterns

3. **Connection pooling** (automatic with Microsoft.Data.SqlClient)
   ```
   Min Pool Size=5;Max Pool Size=100;Pooling=True;
   ```

4. **Optimize file I/O for blob storage**
   - Use SSD storage for blob directory
   - Enable file system caching
   - Consider using dedicated storage volume

### Backup and Recovery

1. **Database Backups**
   ```sql
   -- Full backup
   BACKUP DATABASE [SaaStack]
   TO DISK = 'C:\Backups\SaaStack_Full.bak'
   WITH COMPRESSION, INIT;

   -- Transaction log backup
   BACKUP LOG [SaaStack]
   TO DISK = 'C:\Backups\SaaStack_Log.trn'
   WITH COMPRESSION;
   ```

2. **Blob Storage Backups**
   ```bash
   # Incremental backup with rsync
   rsync -av --delete /var/saastack/blobs/ /backup/saastack/blobs/
   ```

3. **Automated Backup Schedule**
   - Full backup: Daily
   - Transaction log backup: Every hour
   - Blob storage backup: Daily
   - Test restore procedures regularly

### Monitoring

1. **Database Health**
   ```sql
   -- Check database size
   SELECT
       DB_NAME() AS DatabaseName,
       SUM(size * 8 / 1024) AS SizeMB
   FROM sys.database_files;

   -- Check active connections
   SELECT
       DB_NAME(database_id) AS DatabaseName,
       COUNT(*) AS ConnectionCount
   FROM sys.dm_exec_sessions
   WHERE database_id = DB_ID('SaaStack')
   GROUP BY database_id;
   ```

2. **Performance Metrics**
   - Monitor CPU usage
   - Track query execution times
   - Watch for blocking/deadlocks
   - Monitor disk I/O

3. **Application Logging**
   - Enable SQL Server logging
   - Monitor failed connection attempts
   - Track slow queries (> 1 second)

## Troubleshooting

### Connection Issues

**Problem:** Cannot connect to SQL Server

**Solutions:**
1. Verify SQL Server is running
   ```bash
   # Windows
   Get-Service MSSQLSERVER

   # Linux
   systemctl status mssql-server
   ```

2. Check firewall allows port 1433
   ```bash
   # Linux
   sudo firewall-cmd --add-port=1433/tcp --permanent
   sudo firewall-cmd --reload

   # Windows
   netsh advfirewall firewall add rule name="SQL Server" dir=in action=allow protocol=TCP localport=1433
   ```

3. Verify SQL Server authentication mode
   ```sql
   -- Enable mixed mode authentication
   EXEC xp_instance_regwrite
       N'HKEY_LOCAL_MACHINE',
       N'Software\Microsoft\MSSQLServer\MSSQLServer',
       N'LoginMode', REG_DWORD, 2;
   -- Restart SQL Server service after this change
   ```

### Performance Issues

**Problem:** Slow query performance

**Solutions:**
1. Check for missing indexes
   ```sql
   SELECT
       dm_mid.database_id,
       dm_mid.object_id,
       dm_mid.index_handle,
       dm_migs.user_seeks,
       dm_migs.user_scans,
       dm_mid.equality_columns,
       dm_mid.inequality_columns,
       dm_mid.included_columns
   FROM sys.dm_db_missing_index_details AS dm_mid
   INNER JOIN sys.dm_db_missing_index_groups AS dm_mig
       ON dm_mid.index_handle = dm_mig.index_handle
   INNER JOIN sys.dm_db_missing_index_group_stats AS dm_migs
       ON dm_mig.index_group_handle = dm_migs.group_handle
   WHERE dm_mid.database_id = DB_ID('SaaStack');
   ```

2. Update statistics
   ```sql
   UPDATE STATISTICS [dbo].[EventStreams] WITH FULLSCAN;
   ```

3. Check for blocking
   ```sql
   SELECT * FROM sys.dm_exec_requests WHERE blocking_session_id <> 0;
   ```

### Blob Storage Issues

**Problem:** Cannot write files

**Solutions:**
1. Check directory permissions
   ```bash
   ls -la /var/saastack/
   # Should show rwx permissions for application user
   ```

2. Check disk space
   ```bash
   df -h /var/saastack/blobs
   ```

3. Check SELinux/AppArmor policies (Linux)
   ```bash
   # SELinux
   chcon -R -t httpd_sys_rw_content_t /var/saastack/blobs

   # AppArmor
   aa-complain /path/to/app
   ```

## Migration from LocalMachineJsonFileStore

If you're currently using `LocalMachineJsonFileStore` for development:

1. **Backup your data**
   ```bash
   cp -r ./saastack/local ./saastack/local.backup
   ```

2. **Set up SQL Server** (follow setup instructions above)

3. **Enable SQL Server** in configuration
   ```json
   {
     "ApplicationServices": {
       "Persistence": {
         "SqlServer": {
           "Enabled": true
         }
       }
     }
   }
   ```

4. **Migrate data** (custom migration script required - not provided)
   - Read JSON files from LocalMachineJsonFileStore
   - Convert to CommandEntity format
   - Insert into SQL Server using AddAsync

5. **Test thoroughly** before decommissioning JSON file store

## Testing

### Integration Tests

```bash
dotnet test Infrastructure.Persistence.SqlServer.IntegrationTests
```

### Manual Testing

```csharp
// Test data store
var settings = new SqlServerSettings
{
    ConnectionString = "Server=...;Database=SaaStack;..."
};
var store = SqlServerStore.Create(settings);

var entity = CommandEntity.FromDto(new TestDto { Id = "test123", Name = "Test" });
var result = await store.AddAsync("TestContainer", entity, CancellationToken.None);

// Verify
Assert.True(result.IsSuccessful);
```

## Support

For issues specific to SQL Server persistence:
1. Check SQL Server error logs
2. Enable SQL Server Profiler/Extended Events
3. Review application logs with LogLevel set to Debug
4. Consult the main SaaStack documentation

## See Also

- [On-Premise Configuration Guide](../../../docs/deployment/on-premise-configuration.md)
- [RabbitMQ Integration](../Infrastructure.Hosting.Common.RabbitMQ/README.md)
- [Persistence Design Principles](../../../docs/design-principles/0070-persistence.md)
