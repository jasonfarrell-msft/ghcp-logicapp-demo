using '../main.bicep'

param location = 'eastus'
param environmentName = 'prod'
param approverEmail = 'approvals@contoso.com'
param threshold = 5000
param escalationApproverEmail = 'cfo-approvals@contoso.com'
param escalationThreshold     = 25000
param teamsChannelId = ''
param teamsGroupId   = ''
