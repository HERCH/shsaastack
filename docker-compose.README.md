# Docker Compose Setup for On-Premise Testing

This Docker Compose configuration provides a complete testing infrastructure for on-premise deployments, including SQL Server and RabbitMQ.

## Services Included

### 1. SQL Server (2022 Developer Edition)
- **Container Name:** `saastack-sqlserver`
- **Port:** 1433
- **Username:** `sa`
- **Password:** `YourStrong!Passw0rd`
- **Edition:** Developer (free for development/testing)

### 2. RabbitMQ (with Management Plugin)
- **Container Name:** `saastack-rabbitmq`
- **AMQP Port:** 5672
- **Management UI:** http://localhost:15672
- **Username:** `app_user`
- **Password:** `app_password`
- **Virtual Host:** `/saastack`

## Quick Start

### Start All Services

```bash
# Start in detached mode
docker-compose -f docker-compose.onpremise.yml up -d

# View logs
docker-compose -f docker-compose.onpremise.yml logs -f

# Check service health
docker-compose -f docker-compose.onpremise.yml ps
```

### Stop All Services

```bash
# Stop but keep data
docker-compose -f docker-compose.onpremise.yml stop

# Stop and remove containers (data persists in volumes)
docker-compose -f docker-compose.onpremise.yml down

# Stop and remove everything including volumes (⚠️ data loss!)
docker-compose -f docker-compose.onpremise.yml down -v
```

## Setup Instructions

### 1. Initialize SQL Server Database

Once SQL Server is running, initialize the database:

```bash
# Wait for SQL Server to be ready
docker-compose -f docker-compose.onpremise.yml exec sqlserver /opt/mssql-tools/bin/sqlcmd -S localhost -U sa -P "YourStrong!Passw0rd" -Q "SELECT @@VERSION"

# Create the SaaStack database
docker-compose -f docker-compose.onpremise.yml exec sqlserver /opt/mssql-tools/bin/sqlcmd -S localhost -U sa -P "YourStrong!Passw0rd" -Q "CREATE DATABASE SaaStack"

# Run database schema scripts
docker cp src/Framework/Infrastructure/Infrastructure.Persistence.SqlServer/Scripts/01_CreateDatabase.sql saastack-sqlserver:/tmp/
docker cp src/Framework/Infrastructure/Infrastructure.Persistence.SqlServer/Scripts/02_CreateTables.sql saastack-sqlserver:/tmp/

docker-compose -f docker-compose.onpremise.yml exec sqlserver /opt/mssql-tools/bin/sqlcmd -S localhost -U sa -P "YourStrong!Passw0rd" -i /tmp/01_CreateDatabase.sql
docker-compose -f docker-compose.onpremise.yml exec sqlserver /opt/mssql-tools/bin/sqlcmd -S localhost -U sa -P "YourStrong!Passw0rd" -i /tmp/02_CreateTables.sql
```

### 2. Verify RabbitMQ

Access RabbitMQ Management UI:
- URL: http://localhost:15672
- Username: `app_user`
- Password: `app_password`

Check that the virtual host `/saastack` exists.

## Configuration for Testing

Update your `appsettings.Development.json` or test configuration:

```json
{
  "ApplicationServices": {
    "RabbitMQ": {
      "Enabled": true,
      "HostName": "localhost",
      "Port": 5672,
      "Username": "app_user",
      "Password": "app_password",
      "VirtualHost": "/saastack"
    },
    "Persistence": {
      "SqlServer": {
        "Enabled": true,
        "ConnectionString": "Server=localhost,1433;Database=SaaStack;User Id=sa;Password=YourStrong!Passw0rd;TrustServerCertificate=True;Encrypt=True;",
        "CommandTimeout": 30
      },
      "BlobStorage": {
        "FileSystem": {
          "RootPath": "./test-blobs"
        }
      }
    }
  }
}
```

## Health Checks

Both services include health checks that automatically verify they're running correctly.

**Check health status:**
```bash
docker-compose -f docker-compose.onpremise.yml ps

# Example output:
# NAME                   STATUS                    PORTS
# saastack-sqlserver     Up (healthy)              0.0.0.0:1433->1433/tcp
# saastack-rabbitmq      Up (healthy)              0.0.0.0:5672->5672/tcp, 0.0.0.0:15672->15672/tcp
```

## Data Persistence

Data is stored in Docker volumes and persists across container restarts:

- **sqlserver-data**: SQL Server database files
- **rabbitmq-data**: RabbitMQ configuration and messages

**View volumes:**
```bash
docker volume ls | grep saastack
```

**Backup data:**
```bash
# Backup SQL Server
docker-compose -f docker-compose.onpremise.yml exec sqlserver /opt/mssql-tools/bin/sqlcmd -S localhost -U sa -P "YourStrong!Passw0rd" -Q "BACKUP DATABASE SaaStack TO DISK='/var/opt/mssql/backup/SaaStack.bak'"

# Copy backup out of container
docker cp saastack-sqlserver:/var/opt/mssql/backup/SaaStack.bak ./backups/
```

## Troubleshooting

### SQL Server Won't Start

1. Check if port 1433 is already in use:
   ```bash
   # Linux/macOS
   lsof -i :1433

   # Windows
   netstat -ano | findstr :1433
   ```

2. Check logs:
   ```bash
   docker-compose -f docker-compose.onpremise.yml logs sqlserver
   ```

3. Verify password meets complexity requirements (must be at least 8 characters with uppercase, lowercase, numbers, and symbols)

### RabbitMQ Issues

1. Check if port 5672 or 15672 is already in use
2. Verify virtual host creation:
   ```bash
   docker-compose -f docker-compose.onpremise.yml exec rabbitmq rabbitmqctl list_vhosts
   ```

3. If virtual host doesn't exist, create it:
   ```bash
   docker-compose -f docker-compose.onpremise.yml exec rabbitmq rabbitmqctl add_vhost /saastack
   docker-compose -f docker-compose.onpremise.yml exec rabbitmq rabbitmqctl set_permissions -p /saastack app_user ".*" ".*" ".*"
   ```

### Connection Refused Errors

1. Ensure containers are healthy:
   ```bash
   docker-compose -f docker-compose.onpremise.yml ps
   ```

2. Wait for health checks to pass (can take 30-60 seconds after startup)

3. Test connections:
   ```bash
   # Test SQL Server
   docker-compose -f docker-compose.onpremise.yml exec sqlserver /opt/mssql-tools/bin/sqlcmd -S localhost -U sa -P "YourStrong!Passw0rd" -Q "SELECT 1"

   # Test RabbitMQ
   curl -u app_user:app_password http://localhost:15672/api/overview
   ```

## Production Notes

⚠️ **This configuration is for DEVELOPMENT/TESTING ONLY!**

For production deployments:
1. Use strong, unique passwords
2. Enable SSL/TLS for both services
3. Configure proper networking and firewall rules
4. Set up regular backups
5. Use persistent storage with proper backup solutions
6. Configure monitoring and alerting
7. Review and apply security hardening

## Integration Tests

Run integration tests against these services:

```bash
# Ensure services are running
docker-compose -f docker-compose.onpremise.yml up -d

# Wait for services to be healthy
docker-compose -f docker-compose.onpremise.yml ps

# Run tests (example)
dotnet test src/Framework/Infrastructure/Infrastructure.Persistence.SqlServer.IntegrationTests/
```

## Cleanup

```bash
# Stop containers and remove volumes
docker-compose -f docker-compose.onpremise.yml down -v

# Remove dangling images
docker image prune -f

# Remove unused volumes
docker volume prune -f
```

## Resources

- [SQL Server on Docker](https://hub.docker.com/_/microsoft-mssql-server)
- [RabbitMQ on Docker](https://hub.docker.com/_/rabbitmq)
- [Docker Compose Documentation](https://docs.docker.com/compose/)
