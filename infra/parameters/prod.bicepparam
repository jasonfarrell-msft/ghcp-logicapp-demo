using '../main.bicep'

param location = 'eastus'
param environmentName = 'prod'
param approverEmail = 'approvals@contoso.com'
param threshold = 5000
param escalationApproverEmail = 'cfo-approvals@contoso.com'
param escalationThreshold     = 25000
// TODO: Replace with your actual Teams channel ID (e.g., '19:abc123...@thread.tacv2')
param teamsChannelId = 'YOUR_CHANNEL_ID_HERE'
// TODO: Replace with your actual Teams group/team ID (GUID format)
param teamsGroupId   = 'YOUR_TEAM_ID_HERE'
