# Service Design Package: {service name}

## Metadata

| Field             | Value                                    |
|-------------------|------------------------------------------|
| **Version**       | e.g., 0.1                                |
| **Status**        | Draft \| Proposed \| Accepted \| Retired |
| **Service owner** |                                          |
| **Last updated**  | YYYY-MM-DD                               |

## Service overview

**Purpose:** {what this service does}  
**Consumers:** {humans, systems, other services}  
**Scope (in):** {…}  
**Scope (out):** {…}

## Functional requirements

1. {Requirement}
2. {Requirement}

## Non-functional requirements

| Area               | Target / note |
|--------------------|---------------|
| Availability / SLO |               |
| Latency            |               |
| Throughput         |               |
| Data retention     |               |
| RPO / RTO          |               |

## Dependencies

- **Upstream:** {services, queues, data stores}
- **Downstream:** {who depends on this service}
- **External:** {vendors, SaaS}

## Security & privacy

- **Data classes:** {e.g., public / internal / confidential}
- **Authentication / authorization:** {model}
- **Secrets:** {where stored, rotation}
- **Compliance:** {if any}

## Interfaces

- **APIs / events:** {links to specs, OpenAPI, schemas}
- **SLAs for consumers:** {…}

## Operations

- **Runbooks:** {paths or “to be created”}
- **Monitoring & alerting:** {metrics, dashboards, on-call}
- **Capacity & scaling:** {…}

## Transition & lifecycle

- **Deployment model:** {e.g., blue/green, canary}
- **Rollback:** {…}
- **Decommission:** {…}

## Approvals

| Role                                  | Name | Date |
|---------------------------------------|------|------|
| Service owner                         |      |      |
| Security / architecture (if required) |      |      |

## Sources

- `docs/adrs/…` (if any)
- Related user stories: `docs/user-stories/…`
