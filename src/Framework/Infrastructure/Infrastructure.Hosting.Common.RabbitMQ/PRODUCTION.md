# RabbitMQ Production Deployment Guide

This guide provides best practices and recommendations for deploying RabbitMQ in production environments.

## Table of Contents

- [Infrastructure Setup](#infrastructure-setup)
- [High Availability Configuration](#high-availability-configuration)
- [Security Hardening](#security-hardening)
- [Monitoring and Alerting](#monitoring-and-alerting)
- [Performance Tuning](#performance-tuning)
- [Backup and Recovery](#backup-and-recovery)
- [Troubleshooting](#troubleshooting)

## Infrastructure Setup

### Minimum Requirements

**For Production Workloads:**
- **CPU**: 4+ cores (8+ recommended)
- **RAM**: 8GB minimum (16GB+ recommended)
- **Disk**: SSD with at least 100GB
- **Network**: Gigabit ethernet
- **OS**: Ubuntu 20.04+ LTS or RHEL 8+

### Cluster Deployment (Recommended)

Deploy a 3-node or 5-node cluster for high availability:

```bash
# Node 1 (Primary)
docker run -d \
  --name rabbitmq-node1 \
  --hostname rabbitmq-node1 \
  -p 5672:5672 \
  -p 15672:15672 \
  -e RABBITMQ_ERLANG_COOKIE='your-secret-cookie' \
  -e RABBITMQ_NODENAME=rabbit@rabbitmq-node1 \
  -v /data/rabbitmq-node1:/var/lib/rabbitmq \
  rabbitmq:3-management

# Node 2
docker run -d \
  --name rabbitmq-node2 \
  --hostname rabbitmq-node2 \
  -p 5673:5672 \
  -e RABBITMQ_ERLANG_COOKIE='your-secret-cookie' \
  -e RABBITMQ_NODENAME=rabbit@rabbitmq-node2 \
  -v /data/rabbitmq-node2:/var/lib/rabbitmq \
  --link rabbitmq-node1:rabbitmq-node1 \
  rabbitmq:3-management

# Node 3
docker run -d \
  --name rabbitmq-node3 \
  --hostname rabbitmq-node3 \
  -p 5674:5672 \
  -e RABBITMQ_ERLANG_COOKIE='your-secret-cookie' \
  -e RABBITMQ_NODENAME=rabbit@rabbitmq-node3 \
  -v /data/rabbitmq-node3:/var/lib/rabbitmq \
  --link rabbitmq-node1:rabbitmq-node1 \
  --link rabbitmq-node2:rabbitmq-node2 \
  rabbitmq:3-management

# Join nodes to cluster
docker exec -it rabbitmq-node2 rabbitmqctl stop_app
docker exec -it rabbitmq-node2 rabbitmqctl join_cluster rabbit@rabbitmq-node1
docker exec -it rabbitmq-node2 rabbitmqctl start_app

docker exec -it rabbitmq-node3 rabbitmqctl stop_app
docker exec -it rabbitmq-node3 rabbitmqctl join_cluster rabbit@rabbitmq-node1
docker exec -it rabbitmq-node3 rabbitmqctl start_app
```

### Kubernetes Deployment (Recommended for Cloud)

Use the official RabbitMQ Cluster Operator:

```yaml
apiVersion: rabbitmq.com/v1beta1
kind: RabbitmqCluster
metadata:
  name: saastack-rabbitmq
  namespace: production
spec:
  replicas: 3
  image: rabbitmq:3.12-management
  resources:
    requests:
      cpu: 2000m
      memory: 4Gi
    limits:
      cpu: 4000m
      memory: 8Gi
  persistence:
    storageClassName: fast-ssd
    storage: 100Gi
  rabbitmq:
    additionalConfig: |
      vm_memory_high_watermark.relative = 0.6
      disk_free_limit.absolute = 10GB
      cluster_partition_handling = autoheal
```

## High Availability Configuration

### Queue Mirroring (Classic Queues)

Enable queue mirroring for critical queues:

```bash
# Mirror all queues across all nodes
rabbitmqctl set_policy ha-all "^" '{"ha-mode":"all"}'

# Mirror specific queues to 2 nodes
rabbitmqctl set_policy ha-two "^critical" '{"ha-mode":"exactly","ha-params":2,"ha-sync-mode":"automatic"}'
```

### Quorum Queues (Recommended for RabbitMQ 3.8+)

Quorum queues provide better availability and data safety:

```csharp
// In your application, declare quorum queues
var args = new Dictionary<string, object>
{
    { "x-queue-type", "quorum" }
};
channel.QueueDeclare(queue: "critical-queue", durable: true, exclusive: false, autoDelete: false, arguments: args);
```

### Load Balancer Configuration

Use HAProxy or NGINX for load balancing:

```nginx
# NGINX Configuration
upstream rabbitmq {
    least_conn;
    server rabbitmq-node1:5672 max_fails=3 fail_timeout=30s;
    server rabbitmq-node2:5672 max_fails=3 fail_timeout=30s;
    server rabbitmq-node3:5672 max_fails=3 fail_timeout=30s;
}

server {
    listen 5672;
    proxy_pass rabbitmq;
    proxy_connect_timeout 1s;
}
```

**Update appsettings.json to use load balancer:**

```json
{
  "ApplicationServices": {
    "RabbitMQ": {
      "Enabled": true,
      "HostName": "rabbitmq-lb.internal.company.com",
      "Port": 5672,
      "Username": "saastack-app",
      "Password": "${RABBITMQ_PASSWORD}",
      "VirtualHost": "/production"
    }
  }
}
```

## Security Hardening

### 1. User Management

```bash
# Create dedicated application user
rabbitmqctl add_user saastack-app 'strong-password-here'

# Create dedicated vhost
rabbitmqctl add_vhost /production

# Grant permissions
rabbitmqctl set_permissions -p /production saastack-app ".*" ".*" ".*"

# Remove default guest user
rabbitmqctl delete_user guest
```

### 2. TLS/SSL Configuration

Create TLS certificates:

```bash
# Generate CA certificate
openssl genrsa -out ca-key.pem 2048
openssl req -new -x509 -key ca-key.pem -out ca-cert.pem -days 3650

# Generate server certificate
openssl genrsa -out server-key.pem 2048
openssl req -new -key server-key.pem -out server-req.pem
openssl x509 -req -in server-req.pem -CA ca-cert.pem -CAkey ca-key.pem -CAcreateserial -out server-cert.pem -days 3650
```

**RabbitMQ Configuration (`rabbitmq.conf`):**

```erlang
listeners.ssl.default = 5671

ssl_options.cacertfile = /etc/rabbitmq/certs/ca-cert.pem
ssl_options.certfile   = /etc/rabbitmq/certs/server-cert.pem
ssl_options.keyfile    = /etc/rabbitmq/certs/server-key.pem
ssl_options.verify     = verify_peer
ssl_options.fail_if_no_peer_cert = true

# Disable non-TLS port in production
listeners.tcp = none
```

**Application Configuration:**

```json
{
  "ApplicationServices": {
    "RabbitMQ": {
      "Enabled": true,
      "HostName": "rabbitmq-prod.company.com",
      "Port": 5671,
      "Username": "saastack-app",
      "Password": "${RABBITMQ_PASSWORD}",
      "VirtualHost": "/production",
      "UseTls": true
    }
  }
}
```

### 3. Network Security

- **Firewall Rules**: Only allow port 5672/5671 from application servers
- **Private Network**: Deploy RabbitMQ in private subnet
- **VPN**: Use VPN for management UI access (port 15672)

### 4. Authentication Backends

Consider using LDAP or OAuth for centralized authentication:

```erlang
# In rabbitmq.conf
auth_backends.1 = internal
auth_backends.2 = ldap

auth_ldap.servers.1 = ldap.company.com
auth_ldap.port = 389
auth_ldap.use_ssl = true
```

## Monitoring and Alerting

### 1. Prometheus Integration

Enable Prometheus plugin:

```bash
rabbitmq-plugins enable rabbitmq_prometheus
```

**Prometheus Configuration:**

```yaml
scrape_configs:
  - job_name: 'rabbitmq'
    static_configs:
      - targets: ['rabbitmq-node1:15692', 'rabbitmq-node2:15692', 'rabbitmq-node3:15692']
```

### 2. Key Metrics to Monitor

- **Queue Length**: Alert if > 10,000 messages
- **Consumer Utilization**: Alert if < 50%
- **Memory Usage**: Alert if > 80%
- **Disk Space**: Alert if < 10GB free
- **Connection Count**: Monitor for connection leaks
- **Message Rate**: Track publish/consume rates

### 3. Grafana Dashboards

Import official RabbitMQ dashboard: https://grafana.com/grafana/dashboards/10991

### 4. Logging

Configure structured logging:

```erlang
# In rabbitmq.conf
log.file.level = info
log.console = true
log.console.level = warning
log.exchange = true
log.exchange.level = warning
```

Ship logs to centralized logging (ELK, Splunk, CloudWatch):

```bash
# Filebeat configuration
filebeat.inputs:
  - type: log
    paths:
      - /var/log/rabbitmq/*.log
    fields:
      service: rabbitmq
      environment: production
```

## Performance Tuning

### 1. VM Memory Settings

```erlang
# In rabbitmq.conf
vm_memory_high_watermark.relative = 0.6
vm_memory_high_watermark_paging_ratio = 0.75
```

### 2. Disk I/O Optimization

```erlang
# In rabbitmq.conf
disk_free_limit.absolute = 10GB
```

### 3. Prefetch Count

Set appropriate prefetch for consumers:

```csharp
// In your application
channel.BasicQos(prefetchSize: 0, prefetchCount: 10, global: false);
```

### 4. Connection Pooling

Reuse connections and channels:

```csharp
// Implement connection pooling
public class RabbitMQConnectionPool
{
    private readonly IConnection _connection;
    private readonly ConcurrentBag<IModel> _channels;

    public IModel GetChannel()
    {
        if (_channels.TryTake(out var channel) && channel.IsOpen)
        {
            return channel;
        }
        return _connection.CreateModel();
    }

    public void ReturnChannel(IModel channel)
    {
        if (channel.IsOpen)
        {
            _channels.Add(channel);
        }
    }
}
```

### 5. Message TTL

Set TTL for messages to prevent queue buildup:

```csharp
var args = new Dictionary<string, object>
{
    { "x-message-ttl", 86400000 } // 24 hours
};
channel.QueueDeclare("queue-name", durable: true, exclusive: false, autoDelete: false, arguments: args);
```

## Backup and Recovery

### 1. Definition Backup

Regularly export definitions:

```bash
# Export definitions
rabbitmqctl export_definitions /backup/definitions-$(date +%Y%m%d).json

# Scheduled backup (cron)
0 2 * * * /usr/sbin/rabbitmqctl export_definitions /backup/definitions-$(date +\%Y\%m\%d).json
```

### 2. Message Data Backup

For critical data, use quorum queues with replication or:

```bash
# Backup mnesia database
rabbitmqctl stop_app
tar -czf /backup/rabbitmq-data-$(date +%Y%m%d).tar.gz /var/lib/rabbitmq/mnesia
rabbitmqctl start_app
```

### 3. Disaster Recovery Plan

1. **RTO (Recovery Time Objective)**: < 15 minutes
2. **RPO (Recovery Point Objective)**: < 5 minutes

**Recovery Steps:**
1. Deploy new RabbitMQ cluster
2. Restore definitions: `rabbitmqctl import_definitions backup.json`
3. Update application DNS/configuration
4. Restart applications

## Troubleshooting

### Common Issues

**1. Memory Alarms**

```bash
# Check memory status
rabbitmqctl status | grep memory

# Temporary increase
rabbitmqctl set_vm_memory_high_watermark 0.7

# Permanent fix: Increase server RAM or reduce queue sizes
```

**2. Disk Alarms**

```bash
# Check disk space
rabbitmqctl status | grep disk_free

# Clear old logs
find /var/log/rabbitmq -name "*.log.*" -mtime +7 -delete

# Increase disk or reduce message TTL
```

**3. Cluster Partitions**

```bash
# Check for partitions
rabbitmqctl cluster_status

# Heal partition (data loss possible)
rabbitmqctl forget_cluster_node rabbit@lost-node
```

**4. Slow Consumers**

```bash
# List queues with consumers
rabbitmqctl list_queues name messages consumers

# Identify slow consumers
rabbitmqctl list_consumers | grep -v "^$"

# Solution: Increase consumer count or optimize processing
```

### Health Checks

Implement application-level health checks:

```csharp
public async Task<bool> HealthCheck()
{
    try
    {
        using var connection = _connectionFactory.CreateConnection();
        using var channel = connection.CreateModel();
        channel.QueueDeclarePassive("health-check");
        return true;
    }
    catch
    {
        return false;
    }
}
```

## Maintenance Windows

Schedule regular maintenance:

- **Weekly**: Review queue metrics and consumer performance
- **Monthly**: Update RabbitMQ to latest patch version
- **Quarterly**: Review and optimize queue configurations
- **Annually**: Review security certificates and credentials

## Checklist

- [ ] 3+ node cluster deployed
- [ ] Quorum queues enabled for critical data
- [ ] Load balancer configured
- [ ] TLS/SSL enabled
- [ ] Default guest user removed
- [ ] Application-specific users created
- [ ] Monitoring and alerting configured
- [ ] Backup automation in place
- [ ] Disaster recovery plan documented
- [ ] Performance tuning applied
- [ ] Firewall rules configured
- [ ] Logs centralized
- [ ] Health checks implemented
