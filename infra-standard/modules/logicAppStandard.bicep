@description('Azure region for all resources.')
param location string

@description('Short environment name, e.g. dev or prod.')
param environmentName string

@description('Email address used by the deployed workflow definition.')
param approverEmail string

var siteName       = 'la-approval-std-${environmentName}'
var planName       = 'plan-la-std-${environmentName}'
var storageBase    = toLower(replace('lastd${environmentName}${uniqueString(resourceGroup().id)}', '-', ''))
var storageName    = take(storageBase, 24)
var office365Name  = 'con-office365-std-${environmentName}'

resource storage 'Microsoft.Storage/storageAccounts@2023-01-01' = {
  name: storageName
  location: location
  kind: 'StorageV2'
  sku: {
    name: 'Standard_LRS'
  }
  properties: {
    minimumTlsVersion: 'TLS1_2'
    allowBlobPublicAccess: false
  }
}

resource plan 'Microsoft.Web/serverfarms@2022-09-01' = {
  name: planName
  location: location
  sku: {
    name: 'WS1'
    tier: 'WorkflowStandard'
  }
  kind: 'elastic'
  properties: {
    targetWorkerSizeId: 0
    targetWorkerCount: 1
    maximumElasticWorkerCount: 20
  }
}

resource office365Connection 'Microsoft.Web/connections@2016-06-01' = {
  name: office365Name
  location: location
  properties: {
    displayName: 'Office 365 Outlook (standard ${environmentName})'
    api: {
      id: subscriptionResourceId('Microsoft.Web/locations/managedApis', location, 'office365')
    }
  }
}

resource site 'Microsoft.Web/sites@2022-09-01' = {
  name: siteName
  location: location
  kind: 'functionapp,workflowapp'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    siteConfig: {
      appSettings: [
        { name: 'AzureWebJobsStorage',           value: 'DefaultEndpointsProtocol=https;AccountName=${storage.name};EndpointSuffix=${environment().suffixes.storage};AccountKey=${storage.listKeys().keys[0].value}' }
        { name: 'WEBSITE_CONTENTAZUREFILECONNECTIONSTRING', value: 'DefaultEndpointsProtocol=https;AccountName=${storage.name};EndpointSuffix=${environment().suffixes.storage};AccountKey=${storage.listKeys().keys[0].value}' }
        { name: 'WEBSITE_CONTENTSHARE',          value: toLower(siteName) }
        { name: 'FUNCTIONS_EXTENSION_VERSION',   value: '~4' }
        { name: 'FUNCTIONS_WORKER_RUNTIME',      value: 'node' }
        { name: 'WEBSITE_NODE_DEFAULT_VERSION',  value: '~18' }
        { name: 'APP_KIND',                      value: 'workflowApp' }
        { name: 'AzureFunctionsJobHost__extensionBundle__id',      value: 'Microsoft.Azure.Functions.ExtensionBundle.Workflows' }
        { name: 'AzureFunctionsJobHost__extensionBundle__version', value: '[1.*, 2.0.0)' }
        { name: 'WORKFLOWS_SUBSCRIPTION_ID',     value: subscription().subscriptionId }
        { name: 'WORKFLOWS_LOCATION_NAME',       value: location }
        { name: 'WORKFLOWS_RESOURCE_GROUP_NAME', value: resourceGroup().name }
        { name: 'WORKFLOWS_ENVIRONMENT',         value: environmentName }
        { name: 'WORKFLOWS_PARAMETER_approverEmail', value: approverEmail }
      ]
    }
  }
}

output siteName        string = site.name
output defaultHostName string = site.properties.defaultHostName
output office365ConnectionId string = office365Connection.id
