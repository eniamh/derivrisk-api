# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy only the csproj for restore
COPY DerivativesRiskApi.csproj .
RUN dotnet restore DerivativesRiskApi.csproj

# Copy everything else
COPY . .

# Publish the specific project
RUN dotnet publish DerivativesRiskApi.csproj -c Release -o /app/out

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/out .
EXPOSE 10000
ENTRYPOINT ["dotnet", "DerivativesRiskApi.dll"]