FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["src/OpenTicket.Worker/OpenTicket.Worker.csproj", "src/OpenTicket.Worker/"]
COPY ["src/OpenTicket.Domain/OpenTicket.Domain.csproj", "src/OpenTicket.Domain/"]
COPY ["src/OpenTicket.Infrastructure/OpenTicket.Infrastructure.csproj", "src/OpenTicket.Infrastructure/"]
RUN dotnet restore "src/OpenTicket.Worker/OpenTicket.Worker.csproj"
COPY . .
WORKDIR /src/src/OpenTicket.Worker
RUN dotnet publish -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "OpenTicket.Worker.dll"]
