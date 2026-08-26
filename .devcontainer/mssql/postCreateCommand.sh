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

echo "=== postCreateCommand: 完了 ==="

