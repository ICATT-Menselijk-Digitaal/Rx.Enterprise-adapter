FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
ARG VERSION=0.0.0
ARG REVISION=local
WORKDIR /src
COPY ["src/RxEnterprise.Client/RxEnterprise.Client.csproj", "src/RxEnterprise.Client/"]
COPY ["src/RxEnterpriseZgwProxy/RxEnterpriseZgwProxy.csproj", "src/RxEnterpriseZgwProxy/"]
RUN dotnet restore "src/RxEnterpriseZgwProxy/RxEnterpriseZgwProxy.csproj"
COPY . .
WORKDIR "/src/src/RxEnterpriseZgwProxy"
RUN dotnet publish "RxEnterpriseZgwProxy.csproj" \
    -c $BUILD_CONFIGURATION \
    -o /app/publish \
    /p:UseAppHost=false \
    /p:Version=$VERSION \
    /p:SourceRevisionId=$REVISION

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
# certs/ and .env are expected to be mounted at runtime
ENTRYPOINT ["dotnet", "RxEnterpriseZgwProxy.dll"]
