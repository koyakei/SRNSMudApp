using './main.bicep'

param name = 'srns'
// param location = 'japaneast' // 未指定の場合はデプロイ先リソースグループのリージョン（resourceGroup().location）に自動追従します
param hostingPlanName = 'ASP-srns-b1'
param appServiceSku = 'F1' // F1: Free (VNet統合不可)
param enableVnet = false // VNet統合を無効化
param linuxFxVersion = 'DOTNETCORE|11.0'

param serverName = 'srns-server'
param databaseName = 'srns-database'
param serverUsername = 'srns-server-admin'

// SQL サーバーの初期管理者パスワードは main.bicep 内で安全な既定値が自動生成されるため入力不要です。
// （明示的に指定したい場合のみ以下を有効化してください）
// param serverPassword = readEnvironmentVariable('SQL_ADMIN_PASSWORD', '')

param sqlDbSkuName = 'GP_S_Gen5_2'
param useFreeLimit = true
param freeLimitExhaustionBehavior = 'AutoPause'
param autoPauseDelay = 60

param connectionStringName = 'DefaultConnection'
param aspNetCoreEnvironment = 'Production'

// EF Core の migration をローカルから流す場合のみ自分の IP を入れる
param clientIpAddress = ''

// Google OAuth Client ID
param googleClientId = '890065771342-2ruam1rjo1ppvjs5fe11n4eh7mp7t9vv.apps.googleusercontent.com'

// Azure Notification Hub 設定（Web Push 連携用。未指定時は直接 WebPush モードで動作します）
// param notificationHubConnectionString = readEnvironmentVariable('NOTIFICATION_HUB_CONNECTION_STRING', '')
// param notificationHubName = 'srns-hub'

// Web Push (VAPID) 設定
param vapidSubject = 'mailto:admin@example.com'
param vapidPublicKey = 'BME5mMIYAibD51e8ERrgTU6u-Vl14GmGmXkxSgnHh_9RjPAKRWYs17cQzLFkKJo8u2-2fKeLYENgbByXjJinVSc'
param vapidPrivateKey = ''

// Firebase 設定
param firebaseProjectId = 'srnswebapp'
param firebaseServiceAccountJson = ''

// 継続的デプロイ (GitHub Actions)
param gitHubRepoUrl = 'https://github.com/hnutKoyanagi/SRNSMudApp'
param gitHubBranch = 'master'
