# Contributing

Thanks for your interest! Bug reports and suggestions are welcome through GitHub Issues.

## Build and test
Requires the .NET 8 SDK on Windows.

    dotnet build server/MAGNOS.Server.csproj
    dotnet build client/MAGNOS.Client.csproj
    dotnet test tests/Server.Tests/Server.Tests.csproj

## Pull requests
- Keep changes focused and describe what and why.
- Add or update tests for billing, pricing, or database changes.
- Do not commit keys, passwords, databases, or build output.

