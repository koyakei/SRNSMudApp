#!/bin/bash
# postCreateCommand.sh
# devcontainer 起動後に実行されるスクリプト
# パラメータ: $1=SA password, $2=dacpac path, $3=sql script(s) path

SA_PASSWORD="${1:-P@ssw0rd}"

echo "=== postCreateCommand: 開始 ==="

# 1. HTTPS 開発者証明書の生成（devcontainer 内で証明書が存在しないため）
echo ">>> HTTPS 開発者証明書を生成中..."
dotnet dev-certs https 2>/dev/null || true

# 2. dotnet ツールの復元
echo ">>> dotnet ツールを復元中..."
dotnet tool restore 2>/dev/null || true

# 3. NuGet パッケージの復元
echo ">>> NuGet パッケージを復元中..."
dotnet restore

# 4. SQL Server の起動を待機
echo ">>> SQL Server の起動を待機中..."
MAX_RETRIES=30
RETRY_COUNT=0
until /opt/mssql-tools18/bin/sqlcmd -S localhost,1433 -U sa -P "$SA_PASSWORD" -C -Q "SELECT 1" &>/dev/null || [ "$RETRY_COUNT" -ge "$MAX_RETRIES" ]; do
    echo "  SQL Server 未起動... 再試行 ($((RETRY_COUNT + 1))/$MAX_RETRIES)"
    sleep 2
    RETRY_COUNT=$((RETRY_COUNT + 1))
done

if [ "$RETRY_COUNT" -ge "$MAX_RETRIES" ]; then
    echo ">>> [警告] SQL Server への接続がタイムアウトしました。手動で確認してください。"
else
    echo ">>> SQL Server 接続成功"

    # 5. データベース作成（存在しなければ）
    echo ">>> データベース 'SRNSMudApp' を作成中..."
    /opt/mssql-tools18/bin/sqlcmd -S localhost,1433 -U sa -P "$SA_PASSWORD" -C -Q \
        "IF NOT EXISTS (SELECT name FROM sys.databases WHERE name = 'SRNSMudApp') CREATE DATABASE SRNSMudApp;"
fi

# 6. E2Eテスト用 Playwright ブラウザのインストール
echo ">>> Playwright ブラウザをインストール中..."
dotnet build SRNSMudApp.E2ETests/SRNSMudApp.E2ETests.csproj 2>/dev/null
pwsh SRNSMudApp.E2ETests/bin/Debug/net11.0/playwright.ps1 install --with-deps chromium
#    ARM64 Linux でも Playwright の Chromium はサポートされている。
#    --with-deps が Ubuntu resolute で失敗する場合に備え、依存ライブラリを先にインストール。
echo ">>> Playwright 依存ライブラリをインストール中..."
sudo apt-get update
sudo apt-get install -y --no-install-recommends \
    libnss3 libnspr4 libatk1.0-0t64 libatk-bridge2.0-0t64 libcups2t64 libdrm2 \
    libxkbcommon0 libatspi2.0-0t64 libxcomposite1 libxdamage1 libxfixes3 \
    libxrandr2 libgbm1 libpango-1.0-0 libcairo2 libasound2t64 libwayland-client0 \
    2>/dev/null || \
sudo apt-get install -y --no-install-recommends \
    libnss3 libnspr4 libatk1.0-0 libatk-bridge2.0-0 libcups2 libdrm2 \
    libxkbcommon0 libatspi2.0-0 libxcomposite1 libxdamage1 libxfixes3 \
    libxrandr2 libgbm1 libpango-1.0-0 libcairo2 libasound2 libwayland-client0 \
    2>/dev/null || true

echo ">>> Playwright Chromium をインストール中..."
dotnet build SRNSMudApp.E2ETests/SRNSMudApp.E2ETests.csproj -c Debug 2>/dev/null || true
pwsh SRNSMudApp.E2ETests/bin/Debug/net11.0/playwright.ps1 install chromium

echo "=== postCreateCommand: 完了 ==="

