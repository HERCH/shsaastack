# Migration Guide: From Testing Stores to RabbitMQ

Step-by-step guide for migrating from in-memory or file-based stores to RabbitMQ in production.

## Table of Contents

- [Prerequisites](#prerequisites)
- [Migration Strategy](#migration-strategy)
- [Step-by-Step Migration](#step-by-step-migration)
- [Verification](#verification)
- [Rollback Plan](#rollback-plan)
- [Common Issues](#common-issues)

## Prerequisites

### Infrastructure
- ✅ RabbitMQ server deployed and accessible
- ✅ Network connectivity from application to RabbitMQ
- ✅ Firewall rules configured (port 5672/5671)
- ✅ Monitoring and alerting configured

### Application
- ✅ SaaStack version with RabbitMQ support
- ✅ Configuration management in place
- ✅ Deployment pipeline ready

### Knowledge
- ✅ Basic understanding of message queues
- ✅ Access to RabbitMQ Management UI
- ✅ Rollback procedures documented

## Migration Strategy

### Recommended Approach: Blue-Green Deployment

1. **Development**: Test with RabbitMQ locally
2. **Staging**: Deploy with RabbitMQ, validate
3. **Production**: Blue-green deployment with instant switch
4. **Validation**: Monitor for 24-48 hours
5. **Cleanup**: Remove old testing stores

### Alternative: Gradual Migration

1. Run both stores in parallel
2. Migrate queue by queue
3. Compare results
4. Fully switch when confident

## Step-by-Step Migration

### Phase 1: Development Environment (Day 1)

#### 1.1 Install RabbitMQ Locally

```bash
# Option A: Docker (Recommended)
docker-compose -f Infrastructure.Hosting.Common.RabbitMQ/docker-compose.rabbitmq.yml up -d

# Option B: Native installation
# See README.md for platform-specific instructions
```

#### 1.2 Update appsettings.Development.json

```json
{
  "ApplicationServices": {
    "RabbitMQ": {
      "Enabled": true,  // Enable RabbitMQ
      "HostName": "localhost",
      "Port": 5672,
      "Username": "guest",
      "Password": "guest",
      "VirtualHost": "/"
    }
  }
}
```

#### 1.3 Test Locally

```bash
# Run application
dotnet run --project src/Hosts/ApiHost1

# Verify in logs
# Look for: "RabbitMQ connection is healthy"

# Test a simple operation
curl -X POST http://localhost:5001/api/endpoint

# Check RabbitMQ Management UI
open http://localhost:15672
# Login: guest/guest
# Verify queues and exchanges exist
```

#### 1.4 Run Integration Tests

```bash
cd src/Framework/Infrastructure/Infrastructure.Hosting.Common.RabbitMQ.IntegrationTests
dotnet test
```

**Expected:** All tests pass ✅

### Phase 2: Staging Environment (Days 2-3)

#### 2.1 Deploy RabbitMQ to Staging

```bash
# Using Kubernetes (example)
kubectl apply -f rabbitmq-cluster.yaml -n staging

# Verify deployment
kubectl get pods -n staging | grep rabbitmq
kubectl logs -n staging rabbitmq-0
```

#### 2.2 Create Application User

```bash
# Don't use 'guest' in staging/production!
rabbitmqctl add_user saastack-staging "$(generate-password)"
rabbitmqctl add_vhost /staging
rabbitmqctl set_permissions -p /staging saastack-staging ".*" ".*" ".*"
```

#### 2.3 Update Staging Configuration

```json
{
  "ApplicationServices": {
    "RabbitMQ": {
      "Enabled": true,
      "HostName": "rabbitmq.staging.internal",
      "Port": 5672,
      "Username": "saastack-staging",
      "Password": "${RABBITMQ_PASSWORD}",  // From secrets management
      "VirtualHost": "/staging"
    }
  }
}
```

#### 2.4 Deploy Application to Staging

```bash
# Example using GitHub Actions
gh workflow run deploy-staging.yml

# Monitor deployment
kubectl get pods -n staging | grep apihost
kubectl logs -f -n staging apihost-xxx
```

#### 2.5 Validate in Staging

**Test checklist:**

- [ ] Application starts successfully
- [ ] Health check passes (`/health/ready` returns 200)
- [ ] Can publish messages
  ```bash
  # Example: Create a booking
  curl -X POST https://staging.app.com/api/bookings \
    -H "Content-Type: application/json" \
    -d '{"carId":"xxx","startDate":"2024-01-01"}'
  ```
- [ ] Messages appear in RabbitMQ queues
  ```bash
  rabbitmqctl list_queues name messages
  ```
- [ ] Workers are consuming messages
  ```bash
  rabbitmqctl list_consumers
  ```
- [ ] No errors in application logs
  ```bash
  kubectl logs -n staging apihost-xxx | grep -i error
  ```

#### 2.6 Load Testing (Optional but Recommended)

```bash
# Use k6, JMeter, or similar
k6 run loadtest.js

# Monitor RabbitMQ during load
watch -n 1 'rabbitmqctl list_queues name messages consumers'
```

**Expected metrics:**
- Message rate: > 100/sec
- Queue depth: < 1000
- Consumer utilization: > 70%
- No memory alarms

### Phase 3: Production Deployment (Day 4)

#### 3.1 Pre-Deployment Checklist

- [ ] Staging validation complete
- [ ] RabbitMQ cluster deployed (3+ nodes)
- [ ] Load balancer configured
- [ ] Monitoring and alerting active
- [ ] Rollback plan documented
- [ ] Team notified
- [ ] Maintenance window scheduled (if needed)

#### 3.2 Deploy RabbitMQ Cluster

```bash
# Production should use cluster for HA
kubectl apply -f rabbitmq-cluster-prod.yaml -n production

# Wait for all nodes to be ready
kubectl wait --for=condition=Ready pod/rabbitmq-0 -n production --timeout=300s
kubectl wait --for=condition=Ready pod/rabbitmq-1 -n production --timeout=300s
kubectl wait --for=condition=Ready pod/rabbitmq-2 -n production --timeout=300s

# Verify cluster status
kubectl exec -n production rabbitmq-0 -- rabbitmqctl cluster_status
```

#### 3.3 Create Production User

```bash
# Strong password, store in secrets manager
RABBITMQ_PASSWORD=$(generate-strong-password)

# Create user and vhost
kubectl exec -n production rabbitmq-0 -- rabbitmqctl add_user saastack-prod "$RABBITMQ_PASSWORD"
kubectl exec -n production rabbitmq-0 -- rabbitmqctl add_vhost /production
kubectl exec -n production rabbitmq-0 -- rabbitmqctl set_permissions -p /production saastack-prod ".*" ".*" ".*"

# Remove guest user
kubectl exec -n production rabbitmq-0 -- rabbitmqctl delete_user guest
```

#### 3.4 Update Production Configuration

```yaml
# In your configuration management (e.g., Azure App Configuration, AWS Systems Manager)
ApplicationServices__RabbitMQ__Enabled: "true"
ApplicationServices__RabbitMQ__HostName: "rabbitmq-lb.production.internal"
ApplicationServices__RabbitMQ__Port: "5672"
ApplicationServices__RabbitMQ__Username: "saastack-prod"
ApplicationServices__RabbitMQ__Password: "<from-secrets-manager>"
ApplicationServices__RabbitMQ__VirtualHost: "/production"
```

#### 3.5 Blue-Green Deployment

```bash
# Deploy new version (green) with RabbitMQ enabled
kubectl apply -f deployment-green.yaml -n production

# Wait for green to be healthy
kubectl wait --for=condition=Ready pod -l version=green -n production --timeout=300s

# Verify green is working
curl https://green.production.internal/health/ready

# Switch traffic to green
kubectl patch service apihost -n production -p '{"spec":{"selector":{"version":"green"}}}'

# Monitor for 5 minutes
kubectl logs -f -l version=green -n production

# If all good, scale down blue
kubectl scale deployment apihost-blue -n production --replicas=0
```

#### 3.6 Post-Deployment Validation

**Immediate checks (0-30 minutes):**
- [ ] Application started successfully
- [ ] Health checks passing
- [ ] No error spike in logs
- [ ] RabbitMQ connections active
- [ ] Messages being published
- [ ] Workers consuming messages

**Short-term monitoring (1-4 hours):**
- [ ] End-to-end flows working (create booking → receive notification)
- [ ] Queue depths normal
- [ ] No memory/disk alarms
- [ ] Response times normal
- [ ] Error rate < 0.1%

**Long-term monitoring (24-48 hours):**
- [ ] No message loss
- [ ] Performance stable
- [ ] Resource utilization normal
- [ ] No customer complaints

### Phase 4: Optimization (Days 5-7)

#### 4.1 Enable Queue Mirroring

```bash
# For critical queues
kubectl exec -n production rabbitmq-0 -- \
  rabbitmqctl set_policy ha-critical "^(emails|bookings|payments)$" \
  '{"ha-mode":"exactly","ha-params":2,"ha-sync-mode":"automatic"}'
```

#### 4.2 Configure Dead Letter Queues

```bash
# For failed messages
kubectl exec -n production rabbitmq-0 -- \
  rabbitmqctl set_policy dlx-policy ".*" \
  '{"dead-letter-exchange":"dlx","message-ttl":86400000}'
```

#### 4.3 Tune Performance

```bash
# Adjust based on monitoring
# Example: Increase prefetch for high-throughput queues
# (Done in application code, not RabbitMQ)
```

## Verification

### Verify Messages Are Flowing

```bash
# Check queue stats
watch -n 2 'rabbitmqctl list_queues name messages consumers memory | column -t'

# Check message rates
rabbitmqctl list_queues name message_stats
```

### Verify No Message Loss

```bash
# Compare before/after counts
# Example: Create 100 test messages before migration
# Verify all 100 are processed after migration

# Create test messages
for i in {1..100}; do
  curl -X POST https://api/test-endpoint -d "{\"id\":$i}"
done

# Check all processed
rabbitmqctl list_queues | grep test-queue
# Should show: test-queue  0  (empty)
```

### Verify Performance

```bash
# Compare response times before/after
# Example using Apache Bench
ab -n 1000 -c 10 https://api/endpoint

# Compare:
# - 95th percentile latency
# - Throughput (requests/sec)
# - Error rate
```

## Rollback Plan

### Immediate Rollback (< 1 hour after deployment)

```bash
# Switch traffic back to blue (old version)
kubectl patch service apihost -n production -p '{"spec":{"selector":{"version":"blue"}}}'

# Scale up blue if needed
kubectl scale deployment apihost-blue -n production --replicas=3

# Disable RabbitMQ in configuration
# (Traffic goes to old version which uses testing stores)
```

### Delayed Rollback (> 1 hour, < 24 hours)

```bash
# Deploy old version with RabbitMQ disabled
kubectl apply -f deployment-old.yaml -n production

# Gradually shift traffic back
# Use canary deployment: 90% old, 10% new
# Then 100% old

# Note: Messages in RabbitMQ queues will be lost
# Consider draining queues first if possible
```

### Data Preservation During Rollback

```bash
# If you need to preserve messages in RabbitMQ:

# 1. Stop new messages from being published
kubectl scale deployment apihost-new -n production --replicas=0

# 2. Let workers drain existing messages
# Wait until: rabbitmqctl list_queues | grep -v " 0 "
# Shows no messages remaining

# 3. Then rollback
```

## Common Issues

### Issue: Application can't connect to RabbitMQ

**Symptoms:**
- Health check fails
- Logs show: "Connection refused" or "Authentication failed"

**Solutions:**
1. Check network connectivity:
   ```bash
   kubectl exec -n production apihost-xxx -- nc -zv rabbitmq 5672
   ```

2. Verify credentials:
   ```bash
   rabbitmqctl authenticate_user saastack-prod "$PASSWORD"
   ```

3. Check firewall rules

### Issue: Messages not being consumed

**Symptoms:**
- Queue depth increasing
- No consumer activity

**Solutions:**
1. Check workers are running:
   ```bash
   kubectl get pods -n production -l component=worker
   ```

2. Check consumer registrations:
   ```bash
   rabbitmqctl list_consumers
   ```

3. Check worker logs for errors

### Issue: Memory alarms

**Symptoms:**
- RabbitMQ logs: "Memory alarm"
- Publishers blocked

**Solutions:**
1. Increase RabbitMQ memory:
   ```yaml
   resources:
     limits:
       memory: 4Gi  # Increase this
   ```

2. Reduce queue buildup:
   - Scale up workers
   - Add message TTL
   - Configure DLQ

3. Optimize message size

### Issue: Performance degradation

**Symptoms:**
- Slower response times
- Higher latency

**Solutions:**
1. Check queue depths - if too high, scale workers
2. Enable connection pooling in application
3. Use batch publishing for bulk operations
4. Check disk I/O - RabbitMQ needs fast disks

### Issue: Message loss after restart

**Symptoms:**
- Messages disappear after pod restart

**Solutions:**
1. Ensure durable queues:
   ```csharp
   channel.QueueDeclare(queue, durable: true, ...)
   ```

2. Ensure persistent messages:
   ```csharp
   properties.Persistent = true;
   ```

3. Use quorum queues for critical data

## Post-Migration Cleanup

After 1 week of stable production operation:

```bash
# 1. Remove old testing store code (optional)
# Keep for development use

# 2. Document new architecture
# Update system diagrams, runbooks

# 3. Train team on RabbitMQ operations
# Management UI, troubleshooting, monitoring

# 4. Review and optimize
# Based on 1 week of metrics, tune configuration
```

## Success Criteria

Migration is considered successful when:

- ✅ Application running in production with RabbitMQ
- ✅ All health checks passing
- ✅ Zero message loss
- ✅ Performance equal or better than before
- ✅ Error rate < 0.1%
- ✅ Team trained on operations
- ✅ Monitoring and alerting active
- ✅ 7 days of stable operation

## Support

If you encounter issues during migration:

1. Check logs first:
   ```bash
   kubectl logs -n production apihost-xxx
   rabbitmqctl status
   ```

2. Review PRODUCTION.md guide

3. Check RabbitMQ Management UI

4. Consult troubleshooting section above

5. Contact your platform team or infrastructure support

## Additional Resources

- [RabbitMQ Documentation](https://www.rabbitmq.com/documentation.html)
- [PRODUCTION.md](./PRODUCTION.md) - Production deployment guide
- [README.md](./README.md) - Usage documentation
- [RabbitMQ Best Practices](https://www.rabbitmq.com/best-practices.html)
