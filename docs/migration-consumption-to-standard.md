# Migration Plan: Consumption → Standard Logic Apps

This document outlines the migration path for moving the approval workflow from **Logic Apps Consumption** (multi-tenant) to **Logic Apps Standard** (single-tenant), preserving exact behavior.

---

## 1. Resource diff

| Consumption Resource | Standard Equivalent | Notes |
|---|---|---|
| `Microsoft.Logic/workflows@2019-05-01` | `Microsoft.Web/sites@2023-01-01` | `kind: 'functionapp,workflowapp'` — Standard is a specialized App Service |
| _(implicit)_ | `Microsoft.Storage/storageAccounts@2023-01-01` | **New** — required for runtime state (run history, artifacts, checkpoints) |
| _(implicit)_ | `Microsoft.Web/serverfarms@2023-01-01` | **New** — requires SKU `WS1`, `WS2`, or `WS3` with `kind: 'elastic'` (dedicated Workflow Standard tier) |
| `Microsoft.Web/connections@2016-06-01` (Office 365) | _(unchanged)_ | Same managed API connection resource; must re-authorize after deploy |
| `Microsoft.Web/connections@2016-06-01` (Teams) | _(unchanged)_ | Same managed API connection resource; must re-authorize after deploy |

**Key difference:** Consumption workflows are standalone `Microsoft.Logic/workflows` resources; Standard workflows are **files inside a Web App** (`Microsoft.Web/sites`).

---

## 2. On-disk layout

### Project root structure (`standard/`)
```
standard/
├── host.json                    # Functions host configuration (required)
├── connections.json             # Managed API connection references (replaces $connections param)
├── parameters.json              # Workflow parameters (approverEmail, threshold, escalation params, Teams IDs)
└── Approval/
    └── workflow.json            # Workflow definition (stateful workflow)
```

### Infrastructure structure (`infra-standard/`)
```
infra-standard/
├── main.bicep                   # Subscription-scope entry point
├── modules/
│   └── logicAppStandard.bicep   # Storage Account + ASP + Logic App Standard + connections
└── parameters/
    ├── dev.bicepparam
    └── prod.bicepparam
```

**Workflow type:** `stateful` (default) — maintains run history and uses durable storage. Use `stateless` only for high-throughput fire-and-forget scenarios with no history requirements.

---

## 3. Connection model change

### Consumption approach
Workflow definition declares a `$connections` parameter, and the `Microsoft.Logic/workflows` resource injects connection metadata at deploy time via the `parameters.$connections.value` block in Bicep:

```json
// Snippet from Consumption workflow.json
"parameters": {
  "$connections": {
    "defaultValue": {},
    "type": "Object"
  }
}
```

```bicep
// Snippet from Consumption logicApp.bicep
resource approvalWorkflow 'Microsoft.Logic/workflows@2019-05-01' = {
  properties: {
    parameters: {
      '$connections': {
        value: {
          office365: {
            connectionId: office365Connection.id
            connectionName: office365Name
            id: subscriptionResourceId('Microsoft.Web/locations/managedApis', location, 'office365')
          }
          teams: { /* ... */ }
        }
      }
    }
  }
}
```

### Standard approach
Workflow definition **drops the `$connections` parameter entirely**. Connection metadata moves to a **separate `connections.json` file** at the project root, referencing `connectionRuntimeUrl` and using `@appsetting('...')` for authentication:

```json
// standard/connections.json
{
  "managedApiConnections": {
    "office365": {
      "api": {
        "id": "/subscriptions/{subscriptionId}/providers/Microsoft.Web/locations/eastus/managedApis/office365"
      },
      "connection": {
        "id": "/subscriptions/{subscriptionId}/resourceGroups/rg-ghcp-logicapp-dev/providers/Microsoft.Web/connections/con-office365-dev"
      },
      "connectionRuntimeUrl": "https://...",
      "authentication": {
        "type": "ManagedServiceIdentity"
      }
    },
    "teams": {
      "api": {
        "id": "/subscriptions/{subscriptionId}/providers/Microsoft.Web/locations/eastus/managedApis/teams"
      },
      "connection": {
        "id": "/subscriptions/{subscriptionId}/resourceGroups/rg-ghcp-logicapp-dev/providers/Microsoft.Web/connections/con-teams-dev"
      },
      "connectionRuntimeUrl": "https://...",
      "authentication": {
        "type": "ManagedServiceIdentity"
      }
    }
  }
}
```

**At deploy time,** Bicep writes the `connectionRuntimeUrl` and connection resource IDs into app settings (`WORKFLOWS_RESOURCE_GROUP_NAME`, `WORKFLOWS_SUBSCRIPTION_ID`, etc.), and the Logic App runtime resolves them automatically.

**Action syntax in workflow.json** remains unchanged — still uses `"connection": { "name": "@parameters('$connections')['office365']['connectionId']" }` for Consumption compatibility, but the Standard runtime resolves it via `connections.json` lookup instead of a parameter.

---

## 4. Workflow parameters change

### Before (Consumption)
```json
{
  "parameters": {
    "$connections": {
      "defaultValue": {},
      "type": "Object"
    },
    "teamsGroupId": {
      "type": "String"
    },
    "teamsChannelId": {
      "type": "String"
    }
  }
}
```

### After (Standard stateful workflow)
```json
{
  "parameters": {
    "teamsGroupId": {
      "type": "String"
    },
    "teamsChannelId": {
      "type": "String"
    }
  }
}
```

**Change:** The `$connections` parameter is **removed entirely** when migrating to Standard. Connections are resolved via `connections.json` instead.

**Parameter values** for `teamsGroupId`, `teamsChannelId`, `approverEmail`, etc. move to `standard/parameters.json`:

```json
{
  "approverEmail": "approver@contoso.com",
  "threshold": 1000,
  "escalationApproverEmail": "escalation@contoso.com",
  "escalationThreshold": 10000,
  "teamsGroupId": "7fed6a4a-9ebe-40a1-b59c-9184bebc1a2d",
  "teamsChannelId": "19:lvTpiLW5_2h2x2MS9Qw7f8Fub-tUQMHfUrxc-86uKFY1@thread.tacv2"
}
```

At deploy time, Bicep writes these into app settings, and the workflow references them via `@parameters('approverEmail')`, `@parameters('teamsGroupId')`, etc.

---

## 5. Runtime choice

**Recommendation for this demo:** `FUNCTIONS_WORKER_RUNTIME=node`

| Runtime | Use when | Trade-offs |
|---|---|---|
| **`node`** (recommended) | Workflow-only project with **no custom code** extensions | ✅ Faster cold start, smaller image, no compile step<br/>❌ Cannot host custom Node.js Functions in the same app |
| **`dotnet-isolated`** | Need custom .NET 8 Functions (e.g., custom connectors, triggers, or shared libraries) alongside workflows | ✅ Full extensibility via .NET<br/>❌ Slower cold start, larger image, requires compilation |

**For this approval workflow,** which uses only built-in actions (`Request`, `Response`, `InitializeVariable`, `ApiConnectionWebhook`, `ApiConnection`, `If`, `Switch`, `Scope`, `Compose`) and managed connectors (Office 365, Teams), **`node` is sufficient** and provides the best performance.

**Set in Bicep:**
```bicep
resource logicAppStandard 'Microsoft.Web/sites@2023-01-01' = {
  properties: {
    siteConfig: {
      appSettings: [
        { name: 'FUNCTIONS_WORKER_RUNTIME', value: 'node' }
        { name: 'WEBSITE_NODE_DEFAULT_VERSION', value: '~20' }
        // ...
      ]
    }
  }
}
```

---

## 6. What does NOT carry over

### Run history
- **Consumption run history** (stored in Azure Cosmos DB, managed by the platform) is **not migrated**.
- Standard run history starts fresh from first deployment.
- To preserve audit logs, export Consumption runs via the Azure Portal **before** decommissioning the old workflow.

### Trigger URL
- Consumption trigger URL format:
  ```
  https://prod-12.eastus.logic.azure.com/workflows/{guid}/triggers/When_an_approval_request_is_received/paths/invoke?...
  ```
- Standard trigger URL format:
  ```
  https://la-approval-standard-dev.azurewebsites.net/runtime/webhooks/workflow/api/management/workflows/Approval/triggers/When_an_approval_request_is_received/paths/invoke?...
  ```
- **Action required:** Update all clients (CI/CD pipelines, external systems, API Management policies) to use the new URL after migration.

### Managed API connection authorization
- The Office 365 and Teams `Microsoft.Web/connections` resources persist, **but their OAuth tokens may expire**.
- **Action required:** After deploying the Standard workflow, open each connection in the Azure Portal and click **"Authorize"** to re-consent.
- The Standard Logic App must have **Managed Identity enabled** with appropriate RBAC permissions on the connection resources (`Microsoft.Web/connections/read` or `Microsoft.Authorization/roleAssignments/write` on the connections).

### App Service-specific behaviors
- **Always On:** Standard workflows run on App Service infrastructure. For production, enable **Always On** (`WEBSITE_ALWAYS_ON=true`) to avoid cold starts, or use Premium plans with pre-warmed instances.
- **Scaling:** Standard uses App Service Plan scaling rules, not Consumption's automatic per-run scaling. Configure autoscale rules based on HTTP queue length or CPU metrics.
- **Cost model:** Standard is **not** pay-per-execution. You pay for the App Service Plan (WS1/WS2/WS3) **continuously**, even during idle periods. Consumption charges per action execution. Evaluate TCO before migrating large-scale, low-frequency workflows.

---

**Next steps:**
1. Scaffold `standard/` folder structure with placeholder workflow
2. Create `infra-standard/` Bicep modules for Storage Account, App Service Plan, and Standard Logic App
3. Copy workflow definition from `infra/workflows/approval.workflow.json` to `standard/Approval/workflow.json`, removing `$connections` parameter
4. Generate `connections.json` and `parameters.json` from current Bicep parameter values
5. Deploy to a new resource group (`rg-ghcp-logicapp-standard-dev`) and validate behavior parity
6. Update API consumers with new trigger URL
7. Re-authorize managed API connections
8. Decommission Consumption workflow after cutover validation period
