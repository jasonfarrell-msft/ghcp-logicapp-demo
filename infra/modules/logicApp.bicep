@description('Azure region for all resources.')
param location string

@description('Short environment name, e.g. dev or prod.')
param environmentName string

@description('Email address that will receive approval requests.')
param approverEmail string

@description('Amount above which approval is required.')
param threshold int = 1000

@description('Email address that receives high-value escalation approvals.')
param escalationApproverEmail string = 'escalation@contoso.com'

@description('Amount above which escalation is required before the standard approval runs.')
param escalationThreshold int = 10000

@description('Microsoft Teams channel id to post adaptive cards to.')
param teamsChannelId string = ''

@description('Microsoft Teams group (team) id that owns the channel.')
param teamsGroupId string = ''

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
          teams: {
            connectionId: teamsConnection.id
            connectionName: teamsName
            id: subscriptionResourceId('Microsoft.Web/locations/managedApis', location, 'teams')
          }
        }
      }
      approverEmail: {
        value: approverEmail
      }
      threshold: {
        value: threshold
      }
      escalationApproverEmail: {
        value: escalationApproverEmail
      }
      escalationThreshold: {
        value: escalationThreshold
      }
      teamsChannelId: {
        value: teamsChannelId
      }
      teamsGroupId: {
        value: teamsGroupId
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
        approverEmail: {
          type: 'String'
          defaultValue: 'approver@contoso.com'
        }
        threshold: {
          type: 'Int'
          defaultValue: 1000
        }
        escalationApproverEmail: {
          type: 'String'
          defaultValue: 'escalation@contoso.com'
        }
        escalationThreshold: {
          type: 'Int'
          defaultValue: 10000
        }
        teamsChannelId: {
          type: 'String'
          defaultValue: ''
        }
        teamsGroupId: {
          type: 'String'
          defaultValue: ''
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
        RequestApproval: {
          type: 'Scope'
          actions: {
            Check_escalation_threshold: {
              type: 'If'
              expression: {
                and: [
                  { greater: [ '@triggerBody()?[\'amount\']', '@parameters(\'escalationThreshold\')' ] }
                ]
              }
              actions: {
                Send_escalation_email: {
                  type: 'ApiConnectionWebhook'
                  inputs: {
                    host: {
                      connection: { name: '@parameters(\'$connections\')[\'office365\'][\'connectionId\']' }
                    }
                    path: '/approvalmail/$subscriptions'
                    body: {
                      NotificationUrl: '@{listCallbackUrl()}'
                      Message: {
                        To: '@parameters(\'escalationApproverEmail\')'
                        Subject: 'ESCALATION: high-value approval needed for @{triggerBody()?[\'requestId\']}'
                        Options: 'Approve, Reject'
                        Body: 'Requester: @{triggerBody()?[\'requester\']}\nAmount: @{triggerBody()?[\'amount\']}\nDescription: @{triggerBody()?[\'description\']}'
                      }
                    }
                    retryPolicy: {
                      type: 'exponential'
                      count: 4
                      interval: 'PT10S'
                    }
                  }
                  runAfter: {}
                }
                Switch_on_escalation_response: {
                  type: 'Switch'
                  expression: '@body(\'Send_escalation_email\')?[\'SelectedOption\']'
                  cases: {
                    Approve: {
                      case: 'Approve'
                      actions: {}
                    }
                    Reject: {
                      case: 'Reject'
                      actions: {
                        Respond_escalation_denied: {
                          type: 'Response'
                          kind: 'Http'
                          inputs: {
                            statusCode: 403
                            body: {
                              requestId: '@triggerBody()?[\'requestId\']'
                              status: 'escalation-denied'
                            }
                          }
                          runAfter: {}
                        }
                        Terminate_after_escalation_denied: {
                          type: 'Terminate'
                          inputs: {
                            runStatus: 'Cancelled'
                          }
                          runAfter: { Respond_escalation_denied: [ 'Succeeded' ] }
                        }
                      }
                    }
                  }
                  default: { actions: {} }
                  runAfter: { Send_escalation_email: [ 'Succeeded' ] }
                }
              }
              else: {
                actions: {}
              }
              runAfter: {}
            }
            Check_amount_against_threshold: {
              type: 'If'
              expression: {
                and: [
                  { greater: [ '@triggerBody()?[\'amount\']', '@parameters(\'threshold\')' ] }
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
                        To: '@parameters(\'approverEmail\')'
                        Subject: 'Approval needed for request @{triggerBody()?[\'requestId\']}'
                        Options: 'Approve, Reject'
                        Body: 'Requester: @{triggerBody()?[\'requester\']}\nAmount: @{triggerBody()?[\'amount\']}\nDescription: @{triggerBody()?[\'description\']}'
                      }
                    }
                    retryPolicy: {
                      type: 'exponential'
                      count: 4
                      interval: 'PT10S'
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
                        Post_adaptive_card_to_Teams: {
                          type: 'ApiConnection'
                          inputs: {
                            host: {
                              connection: { name: '@parameters(\'$connections\')[\'teams\'][\'connectionId\']' }
                            }
                            method: 'post'
                            path: '/v1.0/teams/conversation/adaptivecard/poster/Flow bot/location/@{encodeURIComponent(encodeURIComponent(json(concat(\'{"groupId":"\',parameters(\'teamsGroupId\'),\'","channelId":"\',parameters(\'teamsChannelId\'),\'"}\'))))}'
                            body: {
                              messageBody: '{"type":"AdaptiveCard","$schema":"http://adaptivecards.io/schemas/adaptive-card.json","version":"1.4","body":[{"type":"TextBlock","size":"Medium","weight":"Bolder","text":"Approval granted"},{"type":"FactSet","facts":[{"title":"Request","value":"@{triggerBody()?[\'requestId\']}"},{"title":"Requester","value":"@{triggerBody()?[\'requester\']}"},{"title":"Amount","value":"@{triggerBody()?[\'amount\']}"},{"title":"Description","value":"@{triggerBody()?[\'description\']}"}]}],"actions":[{"type":"Action.OpenUrl","title":"View run","url":"@{concat(\'https://portal.azure.com/#resource\', workflow().id, \'/runs/\', workflow().run.name)}"}]}'
                            }
                          }
                          runAfter: { Respond_approved: [ 'Succeeded' ] }
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
              runAfter: { Check_escalation_threshold: [ 'Succeeded' ] }
            }
          }
          runAfter: {}
        }
        HandleFailure: {
          type: 'Scope'
          actions: {
            Dead_letter_post: {
              type: 'Http'
              inputs: {
                method: 'POST'
                uri: 'https://example.com/dead-letter'
                headers: { 'Content-Type': 'application/json' }
                body: {
                  runId: '@{workflow().run.id}'
                  workflowName: '@{workflow().name}'
                  trigger: '@triggerBody()'
                }
              }
              runAfter: {}
            }
            Respond_502: {
              type: 'Response'
              kind: 'Http'
              inputs: {
                statusCode: 502
                body: {
                  requestId: '@triggerBody()?[\'requestId\']'
                  status: 'approval-failed'
                  runId: '@{workflow().run.id}'
                }
              }
              runAfter: { Dead_letter_post: [ 'Succeeded', 'Failed', 'TimedOut' ] }
            }
          }
          runAfter: { RequestApproval: [ 'Failed', 'TimedOut' ] }
        }
      }
      outputs: {}
    }
  }
}

output workflowName string = approvalWorkflow.name
output workflowId   string = approvalWorkflow.id
