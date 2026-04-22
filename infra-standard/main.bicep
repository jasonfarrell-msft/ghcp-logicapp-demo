targetScope = 'subscription'

@description('Azure region for the resource group and all resources.')
param location string = 'eastus'

@description('Short environment name, e.g. dev or prod.')
param environmentName string = 'dev'

@description('Email address that will receive approval requests.')
param approverEmail string

var rgName = 'rg-ghcp-logicapp-standard-${environmentName}'

resource rg 'Microsoft.Resources/resourceGroups@2022-09-01' = {
  name: rgName
  location: location
}

module logicAppStandard 'modules/logicAppStandard.bicep' = {
  name: 'logicAppStandard-${environmentName}'
  scope: rg
  params: {
    location: location
    environmentName: environmentName
    approverEmail: approverEmail
  }
}

output resourceGroupName string = rg.name
output siteName          string = logicAppStandard.outputs.siteName
output siteHostname      string = logicAppStandard.outputs.defaultHostName
