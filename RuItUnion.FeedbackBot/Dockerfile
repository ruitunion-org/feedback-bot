FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
RUN apt-get -y update && apt-get -y install curl
USER app
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Directory.Packages.props", "."]
COPY ["Directory.Build.props", "."]
COPY ["Directory.Build.targets", "."]
COPY ["RuItUnion.FeedbackBot/RuItUnion.FeedbackBot.csproj", "RuItUnion.FeedbackBot/"]
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