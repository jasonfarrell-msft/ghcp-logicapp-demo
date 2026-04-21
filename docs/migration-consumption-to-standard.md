# Migration plan: Consumption → Standard

This document tracks the side-by-side migration of the approval Logic App
from **Consumption** (`Microsoft.Logic/workflows`, multi-tenant) to **Standard**
(`Microsoft.Web/sites kind=workflowapp`, single-tenant). The original
Consumption deployment under `infra/` stays in place for live comparison.

## (a) New Azure resources

| Concern | Consumption (today) | Standard (target) |
|---|---|---|
| Compute | `Microsoft.Logic/workflows` (serverless, per-execution billing) | `Microsoft.Web/sites` `kind: 'functionapp,workflowapp'` running on a WS-tier App Service Plan |
| Plan | n/a | `Microsoft.Web/serverfarms` SKU `WS1` (`tier: WorkflowStandard`) |
| Storage | n/a (managed) | `Microsoft.Storage/storageAccounts` `Standard_LRS` (host metadata, run history, queues) |
| Connections | `Microsoft.Web/connections` (still supported) | `Microsoft.Web/connections` *plus* `connections.json` mapping in the project; built-in connectors run in-process and need no external resource |
| Identity | Workflow-level managed identity | Site-level system-assigned managed identity (recommended) |

## (b) On-disk file layout

```
standard/
├── host.json                  # Functions host config + workflow extensionBundle
├── connections.json           # managed + service-provider connection map
├── parameters.json            # workflow parameter values (replaces Bicep workflow `parameters`)
├── local.settings.json        # local-only secrets / env (NEVER committed with real values)
└── Approval/
    └── workflow.json          # the workflow definition (one folder per workflow)
```

Multi-workflow projects add sibling folders (e.g. `Reminder/workflow.json`).

## (c) Office 365 approval-email action

Two options:

1. **Managed API connection (chosen for v1 of this migration).** Re-uses the
   existing `Microsoft.Web/connections` resource of kind `office365` and
   references it from `connections.json` via `connectionRuntimeUrl` and an
   `authentication` block (`ManagedServiceIdentity` or raw key). Behaviour
   identical to Consumption.
2. **Built-in service-provider connector.** Faster (no out-of-process call),
   no per-execution connector cost, supports VNet, but the Office 365
   built-in connector is currently **preview / not available** for the
   `SendApprovalEmail` action shape. We leave a TODO in
   `standard/Approval/workflow.json` to revisit once GA.

## (d) `$connections` and workflow `parameters` change

- The workflow definition no longer declares a `$connections` workflow
  parameter. The runtime injects connection bindings from
  `connections.json` instead.
- Workflow `parameters` (e.g. `approverEmail`, `threshold`) now read from
  `parameters.json` at runtime, with values overrideable per-environment via
  app settings of the form `WORKFLOWS_PARAMETER_<name>`.
- Built-in service-provider actions (`Http`, `serviceBus`, `sql`, etc.) do
  **not** appear in `connections.json` at all.

## (e) Local development

- Install **Azure Functions Core Tools v4** and the **Azure Logic Apps
  (Standard) VS Code extension**.
- `cd standard && func start` boots the runtime locally on
  `http://localhost:7071`. The HTTP-triggered workflow gets a local URL
  printed in the console and can be POSTed with the same payload used
  against the Consumption deployment.
- Managed connection actions still need a real `connectionRuntimeUrl` and
  cannot be exercised purely offline; built-in actions can.
- `local.settings.json` keeps `AzureWebJobsStorage` (`UseDevelopmentStorage=true`
  for Azurite) and `WORKFLOWS_SUBSCRIPTION_ID` placeholders.
