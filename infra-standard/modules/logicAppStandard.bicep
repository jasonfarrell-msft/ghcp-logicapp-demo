@description('Azure region for all resources.')
param location string

@description('Short environment name, e.g. dev or prod.')
param environmentName string

@description('Email address that will receive approval requests.')
param approverEmail string

@description('Amount above which approval is required.')
param threshold int = 1000

@description('Email address for high-value escalation approvals.')
param escalationApproverEmail string

@description('Amount above which escalation approval is required.')
param escalationThreshold int = 10000

@description('Teams Group ID for posting approval results.')
param teamsGroupId string

@description('Teams Channel ID for posting approval results.')
param teamsChannelId string

var storageAccountName = 'st${replace(environmentName, '-', '')}laappr'
var appServicePlanName = 'asp-la-standard-${environmentName}'
var workflowAppName = 'la-approval-std-${environmentName}'
var office365ConnectionName = 'con-office365-${environmentName}'

resource storageAccount 'Microsoft.Storage/storageAccounts@2023-01-01' = {
  name: storageAccountName
  location: location
  tags: {
    SecurityControl: 'Ignore'
  }
  sku: {
    name: 'Standard_LRS'
  }
  kind: 'StorageV2'
  properties: {
    supportsHttpsTrafficOnly: true
    minimumTlsVersion: 'TLS1_2'
    allowBlobPublicAccess: true
    allowSharedKeyAccess: true
    networkAcls: {
      bypass: 'AzureServices'
      defaultAction: 'Allow'
    }
  }
}

resource fileService 'Microsoft.Storage/storageAccounts/fileServices@2023-01-01' = {
  parent: storageAccount
  name: 'default'
  properties: {
    shareDeleteRetentionPolicy: {
      enabled: true
      days: 7
    }
  }
}

resource appServicePlan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: appServicePlanName
  location: location
  sku: {
    name: 'WS1'
    tier: 'WorkflowStandard'
  }
  kind: 'elastic'
  properties: {
    maximumElasticWorkerCount: 20
  }
}

resource office365Connection 'Microsoft.Web/connections@2016-06-01' = {
  name: office365ConnectionName
  location: location
  properties: {
    displayName: 'Office 365 Outlook (${environmentName})'
    api: {
      id: subscriptionResourceId('Microsoft.Web/locations/managedApis', location, 'office365')
    }
  }
}

resource workflowApp 'Microsoft.Web/sites@2023-12-01' = {
  name: workflowAppName
  location: location
  kind: 'functionapp,workflowapp'
  identity: {
    type: 'SystemAssigned'
  }
  dependsOn: [
    fileService
  ]
  properties: {
    serverFarmId: appServicePlan.id
    httpsOnly: true
    siteConfig: {
      netFrameworkVersion: 'v6.0'
      appSettings: [
        {
          name: 'AzureWebJobsStorage'
          value: 'DefaultEndpointsProtocol=https;AccountName=${storageAccount.name};EndpointSuffix=${environment().suffixes.storage};AccountKey=${storageAccount.listKeys().keys[0].value}'
        }
        {
          name: 'FUNCTIONS_EXTENSION_VERSION'
          value: '~4'
        }
        {
          name: 'FUNCTIONS_WORKER_RUNTIME'
          value: 'node'
        }
        {
          name: 'WEBSITE_NODE_DEFAULT_VERSION'
          value: '~20'
        }
        {
          name: 'APP_KIND'
          value: 'workflowApp'
        }
        {
          name: 'WEBSITE_CONTENTAZUREFILECONNECTIONSTRING'
          value: 'DefaultEndpointsProtocol=https;AccountName=${storageAccount.name};EndpointSuffix=${environment().suffixes.storage};AccountKey=${storageAccount.listKeys().keys[0].value}'
        }
        {
          name: 'WEBSITE_CONTENTSHARE'
          value: '${workflowAppName}-content'
        }
        {
          name: 'WORKFLOWS_SUBSCRIPTION_ID'
          value: subscription().subscriptionId
        }
        {
          name: 'WORKFLOWS_RESOURCE_GROUP_NAME'
          value: resourceGroup().name
        }
        {
          name: 'WORKFLOWS_LOCATION_NAME'
          value: location
        }
      ]
    }
  }
}

output workflowAppName string = workflowApp.name
output workflowAppId string = workflowApp.id
output storageAccountName string = storageAccount.name
output office365ConnectionName string = office365Connection.name
