# Build this with the context of the repository root folder (../.. relative to this file)

FROM microsoft/dotnet:1.0.0-preview2-sdk

MAINTAINER Adam Friedman <tintoy@tintoy.io>

RUN mkdir -p /var/lib/tfa/state
VOLUME /var/lib/tfa/state

EXPOSE 5050

RUN mkdir -p /dotnetapp
WORKDIR /dotnetapp

COPY NuGet.config /dotnetapp
COPY src/DockerExecutorApi /dotnetapp
RUN ["dotnet", "restore"]
RUN ["dotnet", "build"]

ENV DOOZER_LocalStateDirectory=/var/lib/tfa/state

ENTRYPOINT ["dotnet", "run"]
