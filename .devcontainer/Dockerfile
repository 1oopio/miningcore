# See here for image contents: https://github.com/microsoft/vscode-dev-containers/tree/v0.241.1/containers/dotnet/.devcontainer/base.Dockerfile

# [Choice] .NET version: 6.0, 3.1, 6.0-bullseye, 3.1-bullseye, 6.0-focal, 3.1-focal
ARG VARIANT="6.0-bullseye-slim"
# FROM mcr.microsoft.com/vscode/devcontainers/dotnet:0-${VARIANT}
FROM mcr.microsoft.com/dotnet/sdk:6.0.301-bullseye-slim-amd64

# [Choice] Node.js version: none, lts/*, 18, 16, 14
# ARG NODE_VERSION="none"
# RUN if [ "${NODE_VERSION}" != "none" ]; then su vscode -c "umask 0002 && . /usr/local/share/nvm/nvm.sh && nvm install ${NODE_VERSION} 2>&1"; fi

RUN wget https://packages.microsoft.com/config/debian/11/packages-microsoft-prod.deb -O packages-microsoft-prod.deb
RUN dpkg -i packages-microsoft-prod.deb
RUN rm packages-microsoft-prod.deb
# [Optional] Uncomment this section to install additional OS packages.
RUN apt-get update && export DEBIAN_FRONTEND=noninteractive \
     && apt-get -y install --no-install-recommends dotnet-sdk-6.0 git cmake build-essential libssl-dev pkg-config libboost-all-dev libsodium-dev libzmq5-dev

# [Optional] Uncomment this line to install global node packages.
# RUN su vscode -c "source /usr/local/share/nvm/nvm.sh && npm install -g <your-package-here>" 2>&1