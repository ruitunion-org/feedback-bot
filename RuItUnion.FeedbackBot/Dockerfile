FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS base
ENV \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=0 \
    LC_ALL=ru_RU.UTF-8 \
    LANG=ru_RU.UTF-8
RUN apk add --no-cache \
    icu-data-full \
    icu-libs \
    curl
USER $APP_UID
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Directory.Packages.props", "."]
COPY ["Directory.Build.props", "."]
COPY ["Directory.Build.targets", "."]
COPY ["RuItUnion.FeedbackBot/RuItUnion.FeedbackBot.csproj", "RuItUnion.FeedbackBot/"]
COPY ["RuItUnion.FeedbackBot.Data.Old/RuItUnion.FeedbackBot.Data.Old.csproj", "RuItUnion.FeedbackBot.Data.Old/"]
COPY ["RuItUnion.FeedbackBot.Data/RuItUnion.FeedbackBot.Data.csproj", "RuItUnion.FeedbackBot.Data/"]
COPY ["RuItUnion.FeedbackBot.ServiceDefaults/RuItUnion.FeedbackBot.ServiceDefaults.csproj", "RuItUnion.FeedbackBot.ServiceDefaults/"]
RUN dotnet restore "./RuItUnion.FeedbackBot/RuItUnion.FeedbackBot.csproj"
COPY . .
WORKDIR "/src/RuItUnion.FeedbackBot"
RUN dotnet build "./RuItUnion.FeedbackBot.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./RuItUnion.FeedbackBot.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
HEALTHCHECK --interval=30s --timeout=10s CMD curl -f http://localhost:8080/health || exit 1 
ENTRYPOINT ["dotnet", "RuItUnion.FeedbackBot.dll"]