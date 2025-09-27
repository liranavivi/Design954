# Port Assignment Documentation

This document tracks the port assignments for all services in the Design73 system to prevent conflicts and ensure proper service discovery.

**Last Updated**: 2025-08-18 - Added Manager.Plugin and documented current port conflict issue

## Infrastructure Services

### MongoDB
- **Port**: 27017
- **Service**: Database
- **Access**: Internal only (Docker network)

### RabbitMQ
- **Management UI**: 15672 (http://localhost:15672)
- **AMQP Port**: 5672
- **Service**: Message Broker
- **Credentials**: guest/guest (development)

### Apache Kafka
- **Broker Port**: 9092 (localhost:9092)
- **Internal Port**: 29092 (kafka:29092 - Docker network)
- **External Port**: 9093 (reserved for external access)
- **Service**: Distributed Streaming Platform
- **Protocol**: PLAINTEXT (development)

### Zookeeper
- **Client Port**: 2181
- **Service**: Kafka Coordination Service
- **Access**: Internal only (Docker network)

### Kafka UI
- **Web Interface**: 8082 (http://localhost:8082)
- **Service**: Kafka Management Interface
- **Features**: Topic management, consumer groups, message browsing

### Hazelcast
- **Member Port**: 5701
- **Management Center**: 8080 (http://localhost:8080)
- **Service**: Distributed Cache
- **Cluster Name**: EntitiesManager

### OpenTelemetry Collector
- **OTLP gRPC**: 4317
- **OTLP HTTP**: 4318
- **Prometheus Metrics**: 8889 (http://localhost:8889/metrics)
- **Health Check**: 8888 (http://localhost:8888/metrics)

## Manager Services

**Port Scheme**: 51XX series with standardized increments of 10

### Manager.Orchestrator
- **HTTP**: 5100
- **HTTPS**: 5101
- **Service**: Orchestration management and coordination
- **Swagger**: http://localhost:5100/swagger
- **Endpoints**:
  - POST /api/Orchestration/start/{orchestratedFlowId}
  - POST /api/Orchestration/stop/{orchestratedFlowId}
  - GET /api/Orchestration/status/{orchestratedFlowId}

### Manager.Processor
- **HTTP**: 5110
- **HTTPS**: 5111
- **Service**: Processor entity management
- **Swagger**: http://localhost:5110/swagger

### Manager.Address
- **HTTP**: 5120
- **HTTPS**: 5121
- **Service**: Address entity management
- **Swagger**: http://localhost:5120/swagger

### Manager.Assignment
- **HTTP**: 5130
- **HTTPS**: 5131
- **Service**: Assignment entity management
- **Swagger**: http://localhost:5130/swagger

### Manager.OrchestratedFlow
- **HTTP**: 5140
- **HTTPS**: 5141
- **Service**: Orchestrated flow entity management
- **Swagger**: http://localhost:5140/swagger
- **Note**: Moved from 5040/5041 due to conflict with Windows CDPSvc

### Manager.Delivery
- **HTTP**: 5150
- **HTTPS**: 5151
- **Service**: Delivery entity management
- **Swagger**: http://localhost:5150/swagger

### Manager.Schema
- **HTTP**: 5160
- **HTTPS**: 5161
- **Service**: Schema entity management
- **Swagger**: http://localhost:5160/swagger

### Manager.Step
- **HTTP**: 5170
- **HTTPS**: 5171
- **Service**: Step entity management
- **Swagger**: http://localhost:5170/swagger

### Manager.Plugin
- **HTTP**: 5175
- **HTTPS**: 5176
- **Service**: Plugin entity management
- **Swagger**: http://localhost:5175/swagger
- **Note**: Added 2025-08-18 - Currently configured to use same ports as Address Manager (conflict)

### Manager.Workflow
- **HTTP**: 5180
- **HTTPS**: 5181
- **Service**: Workflow entity management
- **Swagger**: http://localhost:5180/swagger

## Processor Services

### Processor.File (v1.9.9)
- **HTTP**: 5080
- **HTTPS**: 5081
- **Service**: File processing (legacy version)

### Processor.File (v3.2.1)
- **HTTP**: 5090
- **HTTPS**: 5091
- **Service**: File processing (current version)

## Infrastructure Port Usage

### Currently Used Infrastructure Ports
- **2181**: Zookeeper client port
- **5672**: RabbitMQ AMQP
- **5701**: Hazelcast member port
- **8080**: Hazelcast Management Center
- **8082**: Kafka UI web interface
- **9092**: Kafka broker (external)
- **9200**: Elasticsearch HTTP API
- **9300**: Elasticsearch transport
- **15672**: RabbitMQ Management UI

## Reserved Ports

The following port ranges are reserved for future services:

- **5185-5189**: Reserved for additional Manager services (5175-5184 now used)
- **5200-5209**: Reserved for additional Processor services
- **5210-5219**: Reserved for additional infrastructure services

## Port Configuration History

### 2025-08-04: Major Port Reorganization
**Issue**: Port mismatches between service configurations were causing resilient policy delays (7+ second timeouts)

**Root Cause**: Services were configured to listen on different ports than what other services expected to call them on.

**Resolution**: Standardized all manager services to 51XX port scheme with increments of 10:

| Service | Old Ports | New Ports | Status |
|---------|-----------|-----------|---------|
| Manager.Step | 5000/5001 | 5170/5171 | ✅ Fixed |
| Manager.Assignment | 5010/5011 | 5130/5131 | ✅ Fixed |
| Manager.Workflow | 5030/5031 | 5180/5181 | ✅ Fixed |
| Manager.Orchestrator | 5050/5051 | 5100/5101 | ✅ Fixed |
| Manager.Schema | 5100/5101 | 5160/5161 | ✅ Fixed |
| Manager.Delivery | 5130/5131 | 5150/5151 | ✅ Fixed |

**Impact**: Eliminated 7+ second delays in inter-service communication, improved API response times by 97%

### 2025-08-18: Manager Port Conflicts Identified
**Issue**: All managers currently configured to use the same ports (5120/5121) in their launchSettings.json files

**Root Cause**: When launching multiple managers simultaneously, all attempt to bind to the same ports causing "address already in use" errors.

**Current Status**:
- ✅ Manager.Address: Successfully runs on 5120/5121 (first to start)
- ❌ All other managers: Fail to start due to port conflicts
- ❌ Manager.Plugin: Missing from previous port assignments

**Resolution Required**: Update each manager's launchSettings.json to use their assigned unique ports:

| Manager | Assigned Ports | Current Config | Status |
|---------|----------------|----------------|---------|
| Manager.Address | 5120/5121 | 5120/5121 | ✅ Correct |
| Manager.Assignment | 5130/5131 | 5120/5121 | ❌ Needs Fix |
| Manager.Delivery | 5150/5151 | 5120/5121 | ❌ Needs Fix |
| Manager.OrchestratedFlow | 5140/5141 | 5120/5121 | ❌ Needs Fix |
| Manager.Orchestrator | 5100/5101 | 5120/5121 | ❌ Needs Fix |
| Manager.Plugin | 5175/5176 | 5120/5121 | ❌ Needs Fix |
| Manager.Processor | 5110/5111 | 5120/5121 | ❌ Needs Fix |
| Manager.Schema | 5160/5161 | 5120/5121 | ❌ Needs Fix |
| Manager.Step | 5170/5171 | 5120/5121 | ❌ Needs Fix |
| Manager.Workflow | 5180/5181 | 5120/5121 | ❌ Needs Fix |

## Known Port Conflicts

### Windows CDPSvc (Connected Devices Platform Service)
- **Port**: 5040
- **Service**: Windows system service for device connectivity
- **Impact**: Manager.OrchestratedFlow moved from 5040/5041 to 5140/5141
- **Resolution**: Do NOT terminate CDPSvc - use alternative ports instead

## Port Conflict Resolution

If you encounter port conflicts:

1. **Check what's using a port**:
   ```bash
   # Windows
   netstat -ano | findstr :PORT_NUMBER
   
   # Linux/Mac
   lsof -i :PORT_NUMBER
   ```

2. **Kill process using port** (if safe to do so):
   ```bash
   # Windows
   taskkill /PID <PID> /F
   
   # Linux/Mac
   kill -9 <PID>
   ```

3. **Update service configuration** to use alternative port if needed

## Immediate Action Required (2025-08-18)

**CRITICAL**: All manager services except Manager.Address are currently misconfigured and cannot start simultaneously.

### Quick Fix Steps:

1. **Update each manager's `Properties/launchSettings.json`** to use their assigned ports:

   ```bash
   # Example for Manager.Plugin (change 5120/5121 to 5175/5176)
   # Update applicationUrl in launchSettings.json:
   "applicationUrl": "https://localhost:5176;http://localhost:5175"
   ```

2. **Verify port assignments** match this documentation

3. **Test simultaneous startup** of all managers

4. **Update any hardcoded URLs** in configuration files to use correct ports

### Files to Update:
- `Manager.Assignment/Properties/launchSettings.json` → ports 5130/5131
- `Manager.Delivery/Properties/launchSettings.json` → ports 5150/5151
- `Manager.OrchestratedFlow/Properties/launchSettings.json` → ports 5140/5141
- `Manager.Orchestrator/Properties/launchSettings.json` → ports 5100/5101
- `Manager.Plugin/Properties/launchSettings.json` → ports 5175/5176
- `Manager.Processor/Properties/launchSettings.json` → ports 5110/5111
- `Manager.Schema/Properties/launchSettings.json` → ports 5160/5161
- `Manager.Step/Properties/launchSettings.json` → ports 5170/5171
- `Manager.Workflow/Properties/launchSettings.json` → ports 5180/5181

## Service Discovery

All services are configured to communicate using the following patterns:

- **Manager-to-Manager**: HTTP calls using configured URLs in appsettings.json
- **Manager-to-Infrastructure**: Direct connection using localhost ports
- **Processor-to-Infrastructure**: Direct connection using localhost ports
- **Orchestrator-to-Managers**: HTTP calls to gather orchestration data

### Inter-Service Communication URLs

Manager services communicate with each other using these standardized URLs:

```json
"ManagerUrls": {
  "Step": "http://localhost:5170",
  "Assignment": "http://localhost:5130",
  "Address": "http://localhost:5120",
  "Delivery": "http://localhost:5150",
  "Schema": "http://localhost:5160",
  "OrchestratedFlow": "http://localhost:5140",
  "Plugin": "http://localhost:5175",
  "Workflow": "http://localhost:5180",
  "Orchestrator": "http://localhost:5100",
  "Processor": "http://localhost:5110"
}
```

## Health Check Endpoints

All services expose health check endpoints on their respective ports:

- **Path**: `/health`
- **Method**: GET
- **Response**: 200 OK (healthy) or 503 Service Unavailable (unhealthy)

## Notes

- All HTTP ports are for development use
- HTTPS ports are configured but may require SSL certificate setup
- Docker Compose services use internal networking when running in containers
- Local development uses localhost with the assigned ports
- All manager services include CORS configuration for Swagger UI testing
- Port assignments follow a standardized 51XX scheme for easy management
- Resilient policies are optimized for development (shorter timeouts and fewer retries)
