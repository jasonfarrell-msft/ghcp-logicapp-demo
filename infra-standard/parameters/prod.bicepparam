using '../main.bicep'

param location = 'swedencentral'
param environmentName = 'prod'
param approverEmail = 'approvals@contoso.com'
param threshold = 5000
param escalationApproverEmail = 'cfo-approvals@contoso.com'
param escalationThreshold = 25000
param teamsGroupId = '00000000-0000-0000-0000-000000000000'
param teamsChannelId = '19:00000000000000000000000000000000@thread.tacv2'
