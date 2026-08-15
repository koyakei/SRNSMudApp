param location string = resourceGroup().location
param appName string = 'blazorapp-${uniqueString(resourceGroup().id)}'
param sqlAdmin string = 'dbadmin'
@secure()
param sqlPassword string

// 1. App Service Plan (F1 無料枠 / Linux)
resource appServicePlan 'Microsoft.Web/serverfarms@2022-09-01' = {
  name: '${appName}-plan'
  location: location
  sku: {
    name: 'F1'
    tier: 'Free'
  }
  properties: {
    reserved: true // Linux環境を指定
  }
}

// 2. SQL Server と Database (General Purpose Serverless / Free Offer適用)
resource sqlServer 'Microsoft.Sql/servers@2022-05-01-preview' = {
  name: '${appName}-sql'
  location: location
  properties: {
    administratorLogin: sqlAdmin
    administratorLoginPassword: sqlPassword
  }
}

resource sqlDb 'Microsoft.Sql/servers/databases@2024-05-01-preview' = {
  // APIバージョンを更新
  parent: sqlServer
  name: 'MudBlazorDb'
  location: location
  sku: {
    name: 'GP_S_Gen5_1' // 無料枠は最大4vCoreまで対応
    tier: 'GeneralPurpose'
  }
  properties: {
    autoPauseDelay: 60 // 60分で自動一時停止（無料枠の無駄な消費を防ぐ）
    useFreeLimit: true // 毎月のAzure SQL無料枠を適用する
    freeLimitExhaustionBehavior: 'AutoPause' // 無料枠を使い切った場合は翌月まで一時停止し、追加課金を防ぐ
  }
}

// 3. ファイアウォール設定 (Azure サービスからのアクセス許可)
resource sqlFirewall 'Microsoft.Sql/servers/firewallRules@2022-05-01-preview' = {
  parent: sqlServer
  name: 'AllowAllWindowsAzureIps'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

resource webApp 'Microsoft.Web/sites@2022-09-01' = {
  name: appName
  location: location
  properties: {
    serverFarmId: appServicePlan.id
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|11.0'
      appSettings: [
        {
          name: 'ConnectionStrings__DefaultConnection'
          value: 'Server=tcp:${sqlServer.name}${environment().suffixes.sqlServerHostname},1433;Initial Catalog=MudBlazorDb;User ID=${sqlAdmin};Password=${sqlPassword};Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;'
        }
      ]
    }
  }
}
