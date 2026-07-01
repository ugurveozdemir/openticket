FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["src/OpenTicket.Api/OpenTicket.Api.csproj", "src/OpenTicket.Api/"]
COPY ["src/OpenTicket.Domain/OpenTicket.Domain.csproj", "src/OpenTicket.Domain/"]
COPY ["src/OpenTicket.Infrastructure/OpenTicket.Infrastructure.csproj", "src/OpenTicket.Infrastructure/"]
RUN dotnet restore "src/OpenTicket.Api/OpenTicket.Api.csproj"
COPY . .
WORKDIR /src/src/OpenTicket.Api
RUN dotnet publish -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app .
EXPOSE 8080
ENTRYPOINT ["dotnet", "OpenTicket.Api.dll"]
