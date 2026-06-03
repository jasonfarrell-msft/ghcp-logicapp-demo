targetScope = 'subscription'

@description('Azure region for the resource group and all resources.')
param location string = 'eastus'

@description('Short environment name, e.g. dev or prod.')
param environmentName string = 'dev'

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
    threshold: threshold
    escalationApproverEmail: escalationApproverEmail
    escalationThreshold: escalationThreshold
    teamsGroupId: teamsGroupId
    teamsChannelId: teamsChannelId
  }
}

output resourceGroupName string = rg.name
output workflowAppName string = logicAppStandard.outputs.workflowAppName
output storageAccountName string = logicAppStandard.outputs.storageAccountName
