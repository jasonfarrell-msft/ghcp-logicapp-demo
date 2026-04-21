using '../main.bicep'

param location = 'eastus'
param environmentName = 'dev'
param approverEmail = 'approver@contoso.com'
param threshold = 1000
param escalationApproverEmail = 'escalation@contoso.com'
param escalationThreshold     = 10000
param teamsChannelId = ''
param teamsGroupId   = ''
