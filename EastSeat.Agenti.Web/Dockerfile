# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy solution and project files
COPY Agenti.slnx ./
COPY EastSeat.Agenti.Web/EastSeat.Agenti.Web.csproj EastSeat.Agenti.Web/
COPY tests/EastSeat.Agenti.UnitTests/EastSeat.Agenti.UnitTests.csproj tests/EastSeat.Agenti.UnitTests/
COPY tests/EastSeat.Agenti.IntegrationTests/EastSeat.Agenti.IntegrationTests.csproj tests/EastSeat.Agenti.IntegrationTests/
COPY tests/EastSeat.Agenti.E2ETests/EastSeat.Agenti.E2ETests.csproj tests/EastSeat.Agenti.E2ETests/

# Restore dependencies
RUN dotnet restore

# Copy remaining source code
COPY . .

# Build and publish
WORKDIR /src/EastSeat.Agenti.Web
RUN dotnet publish -c Release -o /app/publish --no-restore

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Create non-root user for security
RUN adduser --disabled-password --gecos '' appuser

# Copy published app
COPY --from=build /app/publish .

# Set ownership and switch to non-root user
RUN chown -R appuser:appuser /app
USER appuser

# Expose port
EXPOSE 8080

# Set environment variables
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

# Health check
HEALTHCHECK --interval=30s --timeout=10s --start-period=5s --retries=3 \
    CMD curl --fail http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "EastSeat.Agenti.Web.dll"]
