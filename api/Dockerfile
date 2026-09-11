FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY RideForgeApi.csproj .
RUN dotnet restore
COPY . .
RUN dotnet publish -c Release -o /app/publish

# Migrations ship as an EF bundle: one executable that Railway's pre-deploy step runs (see
# migrate.sh), because the runtime image below has no SDK for `dotnet ef`. Keep the
# tool version in step with dotnet-tools.json at the repo root, which is outside this build context.
ENV PATH="${PATH}:/root/.dotnet/tools"
RUN dotnet tool install --global dotnet-ef --version 10.0.12
RUN dotnet ef migrations bundle --project RideForgeApi.csproj --configuration Release --output /app/publish/efbundle
# Strip CR: `railway up` from a Windows checkout would otherwise ship a script sh cannot run.
RUN sed 's/\r$//' migrate.sh > /app/publish/migrate.sh

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "RideForgeApi.dll"]
