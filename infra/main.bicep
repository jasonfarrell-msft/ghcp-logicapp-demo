targetScope = 'subscription'

@description('Azure region for the resource group and all resources.')
param location string = 'eastus'

@description('Short environment name, e.g. dev or prod.')
param environmentName string = 'dev'

@description('Email address that will receive approval requests.')
param approverEmail string

@description('Amount above which approval is required.')
param threshold int = 1000

@description('Email address that receives high-value escalation approvals.')
param escalationApproverEmail string = 'escalation@contoso.com'

@description('Amount above which escalation is required before standard approval.')
param escalationThreshold int = 10000

@description('Microsoft Teams channel id to post adaptive cards to.')
param teamsChannelId string = ''

@description('Microsoft Teams group (team) id that owns the channel.')
param teamsGroupId string = ''

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
    escalationApproverEmail: escalationApproverEmail
    escalationThreshold: escalationThreshold
    teamsChannelId: teamsChannelId
    teamsGroupId: teamsGroupId
  }
}

output resourceGroupName string = rg.name
output workflowName      string = logicApp.outputs.workflowName
