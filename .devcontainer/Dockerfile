FROM mcr.microsoft.com/devcontainers/base:ubuntu-24.04

# Switch to root to install globally
USER root

# Install necessary tools
RUN apt-get update && export DEBIAN_FRONTEND=noninteractive \
    && apt-get -y install --no-install-recommends \
        curl \
        wget \
        git \
        sqlite3 \
    && apt-get clean -y \
    && rm -rf /var/lib/apt/lists/*

# Install .NET SDK version based on global.json (11.0.100-preview.7.26381.103)
RUN wget https://dot.net/v1/dotnet-install.sh -O /tmp/dotnet-install.sh \
    && chmod +x /tmp/dotnet-install.sh \
    && /tmp/dotnet-install.sh --version 11.0.100-preview.7.26381.103 --install-dir /usr/share/dotnet \
    && ln -s /usr/share/dotnet/dotnet /usr/bin/dotnet \
    && rm /tmp/dotnet-install.sh

ENV DOTNET_ROOT=/usr/share/dotnet
ENV PATH=$PATH:/usr/share/dotnet

# Install dotnet tools for the vscode user
USER vscode
RUN dotnet tool install --global dotnet-ef

ENV PATH=$PATH:/home/vscode/.dotnet/tools

