// ============================================================
//  SRNSMudApp — VNet対応構成 (App Service + Azure SQL)
//  デプロイ先スコープ: リソースグループ
// ============================================================

targetScope = 'resourceGroup'

@description('Web アプリ名。<name>.azurewebsites.net になるためグローバル一意が必要。')
param name string = 'srns'

@description('リソースの作成先リージョン。既定でデプロイ先リソースグループのリージョンに自動追従します。')
param location string = resourceGroup().location

@description('App Service プランの SKU。※VNet統合を行う場合は B1 以上を指定してください。')
@allowed([
  'B1'
  'P0v4'
  'P1v4'
  'F1'
  'S1'
])
param appServiceSku string = 'B1'

@description('VNet統合を有効にするかどうか。')
param enableVnet bool = true

@description('App Service プラン名。')
param hostingPlanName string = 'ASP-${name}-${toLower(appServiceSku)}'

@description('ランタイム。App Service Linux の linuxFxVersion 形式。')
param linuxFxVersion string = 'DOTNETCORE|11.0'

@description('論理 SQL サーバー名（グローバル一意）。')
param serverName string = '${name}-server'

@description('データベース名。')
param databaseName string = '${name}-database'

@description('SQL 管理者ログイン名。')
param serverUsername string = '${name}-server-admin'

@description('照合順序。')
param collation string = 'Japanese_XJIS_140_CI_AS_UTF8'

@description('サーバーレス General Purpose の SKU。')
param sqlDbSkuName string = 'GP_S_Gen5_2'

@description('自動一時停止までの分数。-1 で無効。')
param autoPauseDelay int = 60

@description('無料枠（毎月 10 万 vCore 秒 + 32GB ストレージ）を使うかどうか。')
param useFreeLimit bool = true

@description('無料枠を使い切った後の挙動。')
@allowed([
  'AutoPause'
  'BillOverUsage'
])
param freeLimitExhaustionBehavior string = 'AutoPause'

@description('接続文字列に使う名前。')
param connectionStringName string = 'DefaultConnection'

@description('ASPNETCORE_ENVIRONMENT の値。')
param aspNetCoreEnvironment string = 'Production'

@description('開発端末から SQL へ直接つなぐ場合の IP。空なら規則を作らない。')
param clientIpAddress string = ''

@description('Google OAuth Client ID（空の場合は設定しません）。')
param googleClientId string = ''

@description('Azure Notification Hub の接続文字列（空の場合は直接 WebPush モードで動作します）。')
@secure()
param notificationHubConnectionString string = ''

@description('Azure Notification Hub 名。')
param notificationHubName string = ''

@description('Web Push (VAPID) の連絡先 Subject (例: mailto:admin@example.com)。')
param vapidSubject string = ''

@description('Web Push (VAPID) の公開鍵。')
param vapidPublicKey string = ''

@description('Web Push (VAPID) の秘密鍵。')
@secure()
param vapidPrivateKey string = ''

@description('Firebase プロジェクト ID。')
param firebaseProjectId string = ''

@description('Firebase サービスアカウント秘密鍵 JSON。')
@secure()
param firebaseServiceAccountJson string = ''

@description('初回起動時の DB 自動マイグレーション（テーブル作成）を有効にするかどうか。')
param autoMigrate bool = true

@description('GitHub リポジトリ URL（継続的デプロイ用。空の場合は設定しません）。')
param gitHubRepoUrl string = 'https://github.com/hnutKoyanagi/SRNSMudApp'

@description('デプロイ対象のブランチ名。')
param gitHubBranch string = 'master'

// VNet用変数 (F1 は VNet 統合非対応のため常に無効化)
var isVnetEnabled = enableVnet && appServiceSku != 'F1'
var vnetName = '${name}-vnet'
var appSubnetName = 'AppSubnet'
var maxSizeBytes = 34359738368 // 32 GB
var serverPassword = 'Az!9_${take(uniqueString(resourceGroup().id, subscription().id), 8)}_${toUpper(take(uniqueString(subscription().id, resourceGroup().name), 6))}'
var systemUserInitialPassword = 'Sys!9_${take(uniqueString(subscription().id, resourceGroup().id), 12)}_${toUpper(take(uniqueString(resourceGroup().name, subscription().id), 8))}'

// ------------------------------------------------------------
//  VNetとサブネットの定義
// ------------------------------------------------------------
resource vnet 'Microsoft.Network/virtualNetworks@2023-11-01' = if (isVnetEnabled) {
  name: vnetName
  location: location
  properties: {
    addressSpace: {
      addressPrefixes: [
        '10.0.0.0/16'
      ]
    }
    // VNetリソースの内部でサブネットを定義することで、親子の依存関係が自動的に解決されます
    subnets: [
      {
        name: appSubnetName
        properties: {
          addressPrefix: '10.0.1.0/24'
          delegations: [
            {
              name: 'dlg-appServices'
              properties: {
                serviceName: 'Microsoft.Web/serverfarms'
              }
            }
          ]
          serviceEndpoints: [
            {
              service: 'Microsoft.Sql'
            }
          ]
        }
      }
    ]
  }
}

// ------------------------------------------------------------
//  App Service プラン
// ------------------------------------------------------------
resource plan 'Microsoft.Web/serverfarms@2024-04-01' = {
  name: hostingPlanName
  location: location
  kind: 'linux'
  sku: {
    name: appServiceSku
    tier: appServiceSku == 'F1' ? 'Free' : (startsWith(appServiceSku, 'P') ? 'PremiumV4' : 'Basic')
    capacity: 1
  }
  properties: {
    reserved: true
  }
}

// ------------------------------------------------------------
//  論理 SQL サーバー
// ------------------------------------------------------------
resource sqlServer 'Microsoft.Sql/servers@2023-08-01-preview' = {
  name: serverName
  location: location
  properties: {
    administratorLogin: serverUsername
    #disable-next-line use-secure-value-for-secure-inputs
    administratorLoginPassword: serverPassword
    version: '12.0'
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
  }
}

resource allowAzureServices 'Microsoft.Sql/servers/firewallRules@2023-08-01-preview' = {
  parent: sqlServer
  name: 'AllowAllWindowsAzureIps'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

resource allowClientIp 'Microsoft.Sql/servers/firewallRules@2023-08-01-preview' = if (!empty(clientIpAddress)) {
  parent: sqlServer
  name: 'AllowDevClient'
  properties: {
    startIpAddress: clientIpAddress
    endIpAddress: clientIpAddress
  }
}

resource sqlVnetRule 'Microsoft.Sql/servers/virtualNetworkRules@2023-08-01-preview' = if (isVnetEnabled) {
  parent: sqlServer
  name: 'AllowAppSubnet'
  properties: {
    virtualNetworkSubnetId: resourceId('Microsoft.Network/virtualNetworks/subnets', vnetName, appSubnetName)
    ignoreMissingVnetServiceEndpoint: false
  }
  dependsOn: [
    vnet
  ]
}

// ------------------------------------------------------------
//  データベース
// ------------------------------------------------------------
resource sqlDb 'Microsoft.Sql/servers/databases@2023-08-01-preview' = {
  parent: sqlServer
  name: databaseName
  location: location
  sku: {
    name: sqlDbSkuName
    tier: 'GeneralPurpose'
  }
  properties: {
    collation: collation
    maxSizeBytes: maxSizeBytes
    autoPauseDelay: autoPauseDelay
    minCapacity: json('0.5')
    zoneRedundant: false
    requestedBackupStorageRedundancy: 'Local'
    useFreeLimit: useFreeLimit
    freeLimitExhaustionBehavior: useFreeLimit ? freeLimitExhaustionBehavior : null
  }
}

// ------------------------------------------------------------
//  Web アプリ
// ------------------------------------------------------------
resource site 'Microsoft.Web/sites@2024-04-01' = {
  name: name
  location: location
  kind: 'app,linux'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    clientAffinityEnabled: true
    // VNet統合: isVnetEnabled が true の場合のみサブネットIDを渡す (F1 は非対応)
    virtualNetworkSubnetId: isVnetEnabled
      ? resourceId('Microsoft.Network/virtualNetworks/subnets', vnetName, appSubnetName)
      : null
    siteConfig: {
      linuxFxVersion: linuxFxVersion
      appCommandLine: 'dotnet /home/site/wwwroot/SRNSMudApp.dll'
      ftpsState: 'FtpsOnly'
      minTlsVersion: '1.2'
      alwaysOn: appServiceSku != 'F1'
      webSocketsEnabled: true
      healthCheckPath: null
      vnetRouteAllEnabled: isVnetEnabled // アウトバウンドトラフィックをすべてVNetにルーティング
      appSettings: concat(
        [
          {
            name: 'ASPNETCORE_ENVIRONMENT'
            value: aspNetCoreEnvironment
          }
          {
            name: 'WEBSITES_ENABLE_APP_SERVICE_STORAGE'
            value: 'true'
          }
          {
            name: 'WEBSITES_PORT'
            value: '8080'
          }
          {
            name: 'AUTO_MIGRATE'
            value: autoMigrate ? 'true' : 'false'
          }
          {
            name: 'SYSTEM_USER_INITIAL_PASSWORD'
            value: systemUserInitialPassword
          }
        ],
        !empty(googleClientId)
          ? [
              {
                name: 'Authentication__Google__ClientId'
                value: googleClientId
              }
            ]
          : [],
        !empty(notificationHubConnectionString)
          ? [
              {
                name: 'AzureNotificationHub__ConnectionString'
                value: notificationHubConnectionString
              }
            ]
          : [],
        !empty(notificationHubName)
          ? [
              {
                name: 'AzureNotificationHub__HubName'
                value: notificationHubName
              }
            ]
          : [],
        !empty(vapidSubject)
          ? [
              {
                name: 'Vapid__Subject'
                value: vapidSubject
              }
            ]
          : [],
        !empty(vapidPublicKey)
          ? [
              {
                name: 'Vapid__PublicKey'
                value: vapidPublicKey
              }
            ]
          : [],
        !empty(vapidPrivateKey)
          ? [
              {
                name: 'Vapid__PrivateKey'
                value: vapidPrivateKey
              }
            ]
          : [],
        !empty(firebaseProjectId)
          ? [
              {
                name: 'Firebase__ProjectId'
                value: firebaseProjectId
              }
            ]
          : [],
        !empty(firebaseServiceAccountJson)
          ? [
              {
                name: 'Firebase__ServiceAccountJson'
                value: firebaseServiceAccountJson
              }
            ]
          : []
      )
      connectionStrings: [
        {
          name: connectionStringName
          type: 'SQLAzure'
          connectionString: 'Server=tcp:${sqlServer.properties.fullyQualifiedDomainName},1433;Initial Catalog=${databaseName};Authentication=Active Directory Managed Identity;MultipleActiveResultSets=False;Encrypt=True;TrustServerCertificate=False;Connection Timeout=60;'
        }
      ]
    }
  }
  dependsOn: [
    sqlDb
    allowAzureServices
    vnet
    sqlVnetRule
  ]
}

// ------------------------------------------------------------
//  SQL Server の Microsoft Entra 管理者
// ------------------------------------------------------------
resource sqlEntraAdmin 'Microsoft.Sql/servers/administrators@2023-08-01-preview' = {
  parent: sqlServer
  name: 'ActiveDirectory'
  properties: {
    administratorType: 'ActiveDirectory'
    login: site.name
    sid: site.identity.principalId
    tenantId: subscription().tenantId
  }
}

// ------------------------------------------------------------
//  継続的デプロイ (GitHub Actions)
// ------------------------------------------------------------
resource sourceControl 'Microsoft.Web/sites/sourcecontrols@2024-04-01' = if (!empty(gitHubRepoUrl)) {
  parent: site
  name: 'web'
  properties: {
    repoUrl: gitHubRepoUrl
    branch: gitHubBranch
    isManualIntegration: false
    isGitHubAction: true
    deploymentRollbackEnabled: false
    gitHubActionConfiguration: {
      generateWorkflowFile: false
      isLinux: true
    }
  }
}

output siteUrl string = 'https://${site.properties.defaultHostName}'
output sitePrincipalId string = site.identity.principalId
output sqlServerFqdn string = sqlServer.properties.fullyQualifiedDomainName
output connectionStringSettingName string = connectionStringName
