# Build stage
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy everything
COPY . .

# Downgrade to .NET 9 for stable Docker support
RUN sed -i 's/net10.0/net9.0/g' *.csproj && \
    sed -i 's/Microsoft.AspNetCore.OpenApi" Version="10.[0-9.]*"/Microsoft.AspNetCore.OpenApi" Version="9.0.0"/g' *.csproj && \
    sed -i 's/Microsoft.AspNetCore.Authentication.JwtBearer" Version="10.[0-9.]*"/Microsoft.AspNetCore.Authentication.JwtBearer" Version="9.0.0"/g' *.csproj

# Restore and build
RUN dotnet restore
RUN dotnet publish -c Release -o /app/publish

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

# Copy published output
COPY --from=build /app/publish .

# Expose port (Render uses PORT env var)
EXPOSE 8080

# Set environment variables
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

# Run the application
ENTRYPOINT ["dotnet", "bixo-api.dll"]
