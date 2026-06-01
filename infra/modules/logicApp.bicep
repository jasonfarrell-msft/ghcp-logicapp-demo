@description('Azure region for all resources.')
param location string

@description('Short environment name, e.g. dev or prod.')
param environmentName string

@description('Email address that will receive approval requests.')
param approverEmail string

@description('Amount above which approval is required.')
param threshold int = 1000

var workflowName   = 'la-approval-${environmentName}'
var office365Name  = 'con-office365-${environmentName}'
var teamsName      = 'con-teams-${environmentName}'

resource office365Connection 'Microsoft.Web/connections@2016-06-01' = {
  name: office365Name
  location: location
  properties: {
    displayName: 'Office 365 Outlook (${environmentName})'
    api: {
      id: subscriptionResourceId('Microsoft.Web/locations/managedApis', location, 'office365')
    }
  }
}

resource teamsConnection 'Microsoft.Web/connections@2016-06-01' = {
  name: teamsName
  location: location
  properties: {
    displayName: 'Microsoft Teams (${environmentName})'
    api: {
      id: subscriptionResourceId('Microsoft.Web/locations/managedApis', location, 'teams')
    }
  }
}

resource approvalWorkflow 'Microsoft.Logic/workflows@2019-05-01' = {
  name: workflowName
  location: location
  properties: {
    state: 'Enabled'
    parameters: {
      '$connections': {
        value: {
          office365: {
            connectionId: office365Connection.id
            connectionName: office365Name
            id: subscriptionResourceId('Microsoft.Web/locations/managedApis', location, 'office365')
          }
        }
      }
    }
    definition: {
      '$schema': 'https://schema.management.azure.com/providers/Microsoft.Logic/schemas/2016-06-01/workflowdefinition.json#'
      contentVersion: '1.0.0.0'
      parameters: {
        '$connections': {
          defaultValue: {}
          type: 'Object'
        }
      }
      triggers: {
        When_an_approval_request_is_received: {
          type: 'Request'
          kind: 'Http'
          inputs: {
            schema: {
              type: 'object'
              properties: {
                requestId:   { type: 'string' }
                requester:   { type: 'string' }
                amount:      { type: 'number' }
                description: { type: 'string' }
              }
              required: [ 'requestId', 'requester', 'amount' ]
            }
          }
        }
      }
      actions: {
        Initialize_approverEmail: {
          type: 'InitializeVariable'
          inputs: {
            variables: [
              { name: 'approverEmail', type: 'string', value: approverEmail }
            ]
          }
          runAfter: {}
        }
        Initialize_threshold: {
          type: 'InitializeVariable'
          inputs: {
            variables: [
              { name: 'threshold', type: 'integer', value: threshold }
            ]
          }
          runAfter: { Initialize_approverEmail: [ 'Succeeded' ] }
        }
        Check_amount_against_threshold: {
          type: 'If'
          expression: {
            and: [
              { greater: [ '@triggerBody()?[\'amount\']', '@variables(\'threshold\')' ] }
            ]
          }
          actions: {
            Send_approval_email: {
              type: 'ApiConnectionWebhook'
              inputs: {
                host: {
                  connection: { name: '@parameters(\'$connections\')[\'office365\'][\'connectionId\']' }
                }
                path: '/approvalmail/$subscriptions'
                body: {
                  NotificationUrl: '@{listCallbackUrl()}'
                  Message: {
                    To: '@variables(\'approverEmail\')'
                    Subject: 'Approval needed for request @{triggerBody()?[\'requestId\']}'
                    Options: 'Approve, Reject'
                    Body: 'Requester: @{triggerBody()?[\'requester\']}\nAmount: @{triggerBody()?[\'amount\']}\nDescription: @{triggerBody()?[\'description\']}'
                  }
                }
              }
              runAfter: {}
            }
            Switch_on_approver_response: {
              type: 'Switch'
              expression: '@body(\'Send_approval_email\')?[\'SelectedOption\']'
              cases: {
                Approve: {
                  case: 'Approve'
                  actions: {
                    Respond_approved: {
                      type: 'Response'
                      kind: 'Http'
                      inputs: {
                        statusCode: 200
                        body: {
                          requestId: '@triggerBody()?[\'requestId\']'
                          status: 'approved'
                        }
                      }
                      runAfter: {}
                    }
                  }
                }
                Reject: {
                  case: 'Reject'
                  actions: {
                    Respond_rejected: {
                      type: 'Response'
                      kind: 'Http'
                      inputs: {
                        statusCode: 200
                        body: {
                          requestId: '@triggerBody()?[\'requestId\']'
                          status: 'rejected'
                        }
                      }
                      runAfter: {}
                    }
                  }
                }
              }
              default: { actions: {} }
              runAfter: { Send_approval_email: [ 'Succeeded' ] }
            }
          }
          else: {
            actions: {
              Respond_auto_approved: {
                type: 'Response'
                kind: 'Http'
                inputs: {
                  statusCode: 200
                  body: {
                    requestId: '@triggerBody()?[\'requestId\']'
                    status: 'auto-approved'
                  }
                }
                runAfter: {}
              }
            }
          }
          runAfter: { Initialize_threshold: [ 'Succeeded' ] }
        }
      }
      outputs: {}
    }
  }
}

output workflowName string = approvalWorkflow.name
output workflowId   string = approvalWorkflow.id
