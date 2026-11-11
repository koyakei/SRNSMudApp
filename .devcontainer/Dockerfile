FROM mcr.microsoft.com/devcontainers/dotnet:11.0

# Install SQL Tools: cross-platform sqlcmd (arm64 & amd64 support)
RUN ARCH=$(uname -m) && \
    case "$ARCH" in \
    x86_64) SQLCMD_ARCH="amd64" ;; \
    aarch64) SQLCMD_ARCH="arm64" ;; \
    *) echo "Unsupported architecture: $ARCH" && exit 1 ;; \
    esac && \
    curl -fsSL "https://github.com/microsoft/go-sqlcmd/releases/latest/download/sqlcmd-linux-${SQLCMD_ARCH}.tar.bz2" -o /tmp/sqlcmd.tar.bz2 && \
    tar -xjf /tmp/sqlcmd.tar.bz2 -C /usr/local/bin sqlcmd && \
    chmod +x /usr/local/bin/sqlcmd && \
    mkdir -p /opt/mssql-tools18/bin && \
    ln -sf /usr/local/bin/sqlcmd /opt/mssql-tools18/bin/sqlcmd && \
    rm -f /tmp/sqlcmd.tar.bz2

ARG DOTNET_SDK_VERSION=11.0.100

RUN curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh \
    && bash /tmp/dotnet-install.sh --version "$DOTNET_SDK_VERSION" --install-dir /usr/share/dotnet \
    && rm /tmp/dotnet-install.sh
# [Optional] Uncomment this section to install additional OS packages.
# RUN apt-get update && export DEBIAN_FRONTEND=noninteractive \
#     && apt-get -y install --no-install-recommends <your-package-list-here>

# [Optional] Uncomment this line to install global node packages.
RUN su vscode -c "source /usr/local/share/nvm/nvm.sh && nvm install --lts && nvm use --lts && npm install -g typescript" 2>&1
# RUN su vscode -c "source /usr/local/share/nvm/nvm.sh && npm install -g <your-package-here>" 2>&1

# Dev Container マウント時の Git 所有権エラー (dubious ownership) を恒久的に防止するためシステム設定およびラッパーを登録
RUN mkdir -p /usr/local/etc \
    && git config --file /etc/gitconfig --replace-all safe.directory /workspaces/SRNSWebApp \
    && git config --file /etc/gitconfig --add safe.directory '*' \
    && chmod 644 /etc/gitconfig \
    && cp -f /etc/gitconfig /usr/local/etc/gitconfig \
    && chmod 644 /usr/local/etc/gitconfig \
    && if [ -x /usr/lib/git-core/git ]; then \
        cp -p /usr/bin/git /usr/bin/git.real 2>/dev/null || true; \
        rm -f /usr/bin/git; \
        printf '#!/bin/sh\nunset GIT_CONFIG_NOSYSTEM\nexport GIT_CONFIG_SYSTEM=/etc/gitconfig\n[ "$GIT_CONFIG_GLOBAL" = "/dev/null" ] && export GIT_CONFIG_GLOBAL=/etc/gitconfig\nexec /usr/lib/git-core/git -c safe.directory='\''*'\'' -c safe.directory=/workspaces/SRNSWebApp "$@"\n' > /usr/bin/git; \
        chmod 755 /usr/bin/git; \
    fi \
    && if [ -x /usr/local/libexec/git-core/git ]; then \
        cp -p /usr/local/bin/git /usr/local/bin/git.real 2>/dev/null || true; \
        rm -f /usr/local/bin/git; \
        printf '#!/bin/sh\nunset GIT_CONFIG_NOSYSTEM\nexport GIT_CONFIG_SYSTEM=/etc/gitconfig\n[ "$GIT_CONFIG_GLOBAL" = "/dev/null" ] && export GIT_CONFIG_GLOBAL=/etc/gitconfig\nexec /usr/local/libexec/git-core/git -c safe.directory='\''*'\'' -c safe.directory=/workspaces/SRNSWebApp "$@"\n' > /usr/local/bin/git; \
        chmod 755 /usr/local/bin/git; \
    fi

