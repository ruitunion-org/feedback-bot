FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /app

COPY ./src/*.csproj ./
# Temporary solution for this issue:
# https://github.com/TelegramBots/Telegram.Bot/issues/1375
COPY ./src/nuget.config ./
RUN dotnet restore

COPY ./src/ ./
RUN dotnet publish -c Release -o out

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
LABEL org.opencontainers.image.source="https://github.com/ruitunion-org/feedback-bot"
WORKDIR /app

COPY --from=build /app/out .

ENTRYPOINT ["dotnet", "Bot.dll"]
