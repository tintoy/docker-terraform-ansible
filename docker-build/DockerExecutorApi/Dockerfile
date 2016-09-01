# Build this with the context of the repository root folder (../.. relative to this file)

FROM microsoft/dotnet:1.0.0-preview2-sdk

VOLUME /var/run/docker.sock
EXPOSE 5050

RUN mkdir -p /dotnetapp
WORKDIR /dotnetapp

COPY NuGet.config /dotnetapp
COPY src/DockerExecutorApi /dotnetapp
RUN ["dotnet", "restore"]
RUN ["dotnet", "build"]

ENTRYPOINT ["dotnet", "run"]
