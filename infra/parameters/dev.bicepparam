using '../main.bicep'

param location = 'eastus'
param environmentName = 'dev'
param approverEmail = 'approver@contoso.com'
param threshold = 1000
param escalationApproverEmail = 'escalation@contoso.com'
param escalationThreshold     = 10000
// FlowTest channel in your Teams tenant
param teamsChannelId = '19:lvTpiLW5_2h2x2MS9Qw7f8Fub-tUQMHfUrxc-86uKFY1@thread.tacv2'
param teamsGroupId   = '7fed6a4a-9ebe-40a1-b59c-9184bebc1a2d'
