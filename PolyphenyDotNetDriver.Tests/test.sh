#!/bin/sh

# Install reportgenerator globally first
# dotnet tool install -g dotnet-reportgenerator-globaltool

# Run the tests
rm -rf TestResults
dotnet test --collect:"XPlat Code Coverage"
reportgenerator "-reports:TestResults/*/coverage.cobertura.xml" "-targetdir:coveragereport"
xdg-open coveragereport/index.htm
