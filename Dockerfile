FROM mcr.microsoft.com/dotnet/sdk:6.0-alpine as builder
ADD "https://api.github.com/repos/MaximilianHausen/SimpleMusicBot/commits?per_page=1" docker_cachebust
WORKDIR /source
RUN git clone https://github.com/MaximilianHausen/SimpleMusicBot.git ./
RUN dotnet publish -c release -o ../app

FROM mcr.microsoft.com/dotnet/runtime:6.0-alpine
COPY --from=builder /app /app
WORKDIR /app
ENTRYPOINT ["dotnet", "SimpleMusicBot.dll"]
