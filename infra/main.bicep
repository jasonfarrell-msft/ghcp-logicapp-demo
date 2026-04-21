targetScope = 'subscription'

@description('Azure region for the resource group and all resources.')
param location string = 'eastus'

@description('Short environment name, e.g. dev or prod.')
param environmentName string = 'dev'

@description('Email address that will receive approval requests.')
param approverEmail string

@description('Amount above which approval is required.')
param threshold int = 1000

var rgName = 'rg-ghcp-logicapp-${environmentName}'

resource rg 'Microsoft.Resources/resourceGroups@2022-09-01' = {
  name: rgName
  location: location
}

module logicApp 'modules/logicApp.bicep' = {
  name: 'logicApp-${environmentName}'
  scope: rg
  params: {
    location: location
    environmentName: environmentName
    approverEmail: approverEmail
    threshold: threshold
  }
}

output resourceGroupName string = rg.name
output workflowName      string = logicApp.outputs.workflowName
