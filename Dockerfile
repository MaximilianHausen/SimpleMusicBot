FROM mcr.microsoft.com/dotnet/sdk:7.0-alpine as builder
WORKDIR /source
COPY . .
RUN dotnet publish -c release -o ../app

FROM mcr.microsoft.com/dotnet/runtime:7.0-alpine
COPY --from=builder /app /app
WORKDIR /app
ENTRYPOINT ["dotnet", "SimpleMusicBot.dll"]
