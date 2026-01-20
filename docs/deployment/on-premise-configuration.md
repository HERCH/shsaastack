# On-Premise Deployment Configuration

This document describes how to configure and deploy SaaStack for on-premise environments.

## Overview

The `HOSTEDONPREMISE` hosting platform provides a dedicated configuration for on-premise deployments, allowing you to use local or internal infrastructure instead of cloud services.

## Key Differences from Cloud Deployments

| Component | Cloud (Azure/AWS) | On-Premise |
|-----------|------------------|------------|
| **Message Broker** | Azure Service Bus / AWS SQS | RabbitMQ (self-hosted) |
| **Database** | Azure SQL / AWS RDS | SQL Server / PostgreSQL (self-hosted) |
| **Blob Storage** | Azure Blob / AWS S3 | Local File System or MinIO |
| **Configuration** | appsettings.Azure.json | appsettings.OnPremise.json |

## Building for On-Premise

### Using MSBuild Property

```bash
dotnet build -p:HostingPlatform=HOSTEDONPREMISE --configuration Release
```

### Changing Default Platform

Edit `src/Directory.Build.props`:

```xml
<HostingPlatform>HOSTEDONPREMISE</HostingPlatform>
```

## Configuration Files

### ApiHost1: appsettings.OnPremise.json

Located at: `src/Hosts/ApiHost1/appsettings.OnPremise.json`

This file contains:
- RabbitMQ connection settings
- SQL Server connection strings
- Local blob storage configuration
- On-premise specific settings

### WebsiteHost: appsettings.OnPremise.json

Located at: `src/Hosts/WebsiteHost/appsettings.OnPremise.json`

Similar configuration for the WebsiteHost.

## Required Infrastructure Components

### 1. RabbitMQ Message Broker

**Minimum Version:** 3.12+

**Configuration:**
```json
{
  "ApplicationServices": {
    "RabbitMQ": {
      "Enabled": true,
      "HostName": "rabbitmq.internal.company.com",
      "Port": 5672,
      "Username": "app_user",
      "Password": "SECURE_PASSWORD_HERE",
      "VirtualHost": "/saastack"
    }
  }
}
```

**Setup Steps:**
1. Install RabbitMQ on your internal infrastructure
2. Create a virtual host: `/saastack`
3. Create a user with appropriate permissions
4. Enable management plugin (optional but recommended)
5. Configure SSL/TLS for production

**Docker Quick Start:**
```bash
docker run -d \
  --name rabbitmq \
  -p 5672:5672 \
  -p 15672:15672 \
  -e RABBITMQ_DEFAULT_USER=app_user \
  -e RABBITMQ_DEFAULT_PASS=secure_password \
  -e RABBITMQ_DEFAULT_VHOST=/saastack \
  rabbitmq:3-management
```

### 2. SQL Server Database

**Minimum Version:** SQL Server 2019+ or PostgreSQL 13+

**Configuration:**
```json
{
  "ApplicationServices": {
    "Persistence": {
      "SqlServer": {
        "ConnectionString": "Server=sql.internal.company.com;Database=SaaStack;User Id=saastack_app;Password=SECURE_PASSWORD_HERE;TrustServerCertificate=True;",
        "CommandTimeout": 30
      }
    }
  }
}
```

**Setup Steps:**
1. Create database: `SaaStack`
2. Create application user with appropriate permissions
3. Run database migrations (if available)
4. Configure backup strategy
5. Enable SSL/TLS for production

### 3. Blob Storage

**Option A: Local File System** (Development/Single Server)

```json
{
  "ApplicationServices": {
    "Persistence": {
      "BlobStorage": {
        "Type": "FileSystem",
        "FileSystem": {
          "RootPath": "/var/saastack/blobs"
        }
      }
    }
  }
}
```

**Setup Steps:**
1. Create directory: `/var/saastack/blobs`
2. Set appropriate permissions for the application user
3. Configure backup strategy for this directory
4. Ensure sufficient disk space

**Option B: MinIO** (Production/Multi-Server)

MinIO provides S3-compatible API for on-premise object storage.

```json
{
  "ApplicationServices": {
    "Persistence": {
      "BlobStorage": {
        "Type": "MinIO",
        "MinIO": {
          "Endpoint": "minio.internal.company.com:9000",
          "AccessKey": "MINIO_ACCESS_KEY",
          "SecretKey": "MINIO_SECRET_KEY",
          "BucketName": "saastack-blobs",
          "UseSSL": true
        }
      }
    }
  }
}
```

**Docker Quick Start:**
```bash
docker run -d \
  --name minio \
  -p 9000:9000 \
  -p 9001:9001 \
  -e MINIO_ROOT_USER=admin \
  -e MINIO_ROOT_PASSWORD=secure_password \
  -v /data/minio:/data \
  minio/minio server /data --console-address ":9001"
```

## Security Considerations

### 1. Secure Secrets Management

**DO NOT** store passwords directly in `appsettings.OnPremise.json` in production.

**Option A: Environment Variables**

```bash
export ApplicationServices__RabbitMQ__Password="secure_password"
export ApplicationServices__Persistence__SqlServer__ConnectionString="Server=..."
```

**Option B: User Secrets (Development)**

```bash
dotnet user-secrets set "ApplicationServices:RabbitMQ:Password" "secure_password"
```

**Option C: Configuration Management System**

Use tools like:
- HashiCorp Vault
- Azure Key Vault (accessible from on-premise)
- Kubernetes Secrets
- Docker Secrets

### 2. Network Security

- Place all infrastructure components in a private network
- Use firewalls to restrict access
- Enable SSL/TLS for all connections:
  - RabbitMQ: Use `amqps://` protocol
  - SQL Server: `Encrypt=True` in connection string
  - MinIO: `UseSSL=true`

### 3. Authentication & Authorization

- Use strong passwords for all services
- Rotate credentials regularly
- Use separate service accounts for each component
- Follow principle of least privilege

## Deployment Checklist

- [ ] RabbitMQ installed and configured
- [ ] SQL Server/PostgreSQL database created
- [ ] Blob storage solution configured
- [ ] All connection strings tested
- [ ] Secrets managed securely (not in config files)
- [ ] SSL/TLS enabled for all connections
- [ ] Firewall rules configured
- [ ] Backup strategy implemented
- [ ] Monitoring and logging configured
- [ ] Build with `HOSTEDONPREMISE` platform
- [ ] Test deployment in staging environment
- [ ] Document disaster recovery procedures

## Monitoring and Logging

### RabbitMQ Monitoring

Access RabbitMQ Management UI:
```
http://rabbitmq.internal.company.com:15672
```

Monitor:
- Queue lengths
- Message rates
- Connection count
- Memory usage

### Application Logging

Configure logging in `appsettings.OnPremise.json`:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  }
}
```

Consider integrating with:
- ELK Stack (Elasticsearch, Logstash, Kibana)
- Seq
- Splunk
- Graylog

## Troubleshooting

### RabbitMQ Connection Issues

**Symptom:** Application fails to start with RabbitMQ connection errors

**Solutions:**
1. Verify RabbitMQ is running: `systemctl status rabbitmq-server`
2. Check hostname resolution: `ping rabbitmq.internal.company.com`
3. Verify credentials are correct
4. Check firewall allows port 5672
5. Review RabbitMQ logs: `/var/log/rabbitmq/`

### Database Connection Issues

**Symptom:** Database connection timeouts or authentication failures

**Solutions:**
1. Verify SQL Server is running
2. Test connection string with SQL Server Management Studio
3. Check user permissions
4. Verify firewall allows port 1433 (SQL Server)
5. Review SQL Server error logs

### Blob Storage Issues

**Symptom:** File upload/download failures

**Solutions:**
1. Verify directory exists and has correct permissions
2. Check disk space: `df -h`
3. Review application logs for specific error messages
4. For MinIO: Check MinIO server is running and accessible

## Support

For issues specific to on-premise deployment, please:
1. Check RabbitMQ, SQL Server, and storage logs
2. Review application logs with `LogLevel` set to `Debug`
3. Verify all infrastructure components are accessible from the application server
4. Consult the main SaaStack documentation

## Migration from Development to On-Premise

If you're currently using `LocalMachineJsonFileStore` for development:

1. **Backup your data** from `./saastack/local`
2. Set up RabbitMQ and SQL Server
3. Configure `appsettings.OnPremise.json`
4. Build with `HOSTEDONPREMISE` platform
5. Run database migrations (if available)
6. Migrate data from JSON files to SQL Server (custom migration required)
7. Test thoroughly before going to production

## Performance Tuning

### RabbitMQ

- Adjust prefetch count based on message processing speed
- Monitor queue lengths and adjust consumer count
- Configure persistence based on durability requirements

### SQL Server

- Index frequently queried columns
- Monitor query performance with Query Store
- Adjust connection pool size based on load
- Consider read replicas for high-traffic scenarios

### Blob Storage

- For high-traffic scenarios, use MinIO with multiple nodes
- Configure caching for frequently accessed files
- Monitor disk I/O and add dedicated storage if needed

## See Also

- [RabbitMQ Integration Documentation](../design-principles/rabbitmq-integration.md) (if exists)
- [Database Configuration](../design-principles/0070-persistence.md)
- [Deployment Guide](../deployment-guide.md) (if exists)
