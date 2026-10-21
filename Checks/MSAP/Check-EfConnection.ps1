$ErrorActionPreference = 'Stop'
Push-Location (Join-Path $PSScriptRoot '../..')
try {
    $connections = foreach ($context in @('IBS.DataAccess.Data.ApplicationDbContext', 'IBS.DataAccess.MSAP.Data.MsapDbContext')) {
        $output = & dotnet ef dbcontext info --project IBS.DataAccess --startup-project IBSWeb --context $context --configuration Debug --no-build --json
        if ($LASTEXITCODE -ne 0) { throw "EF could not create $context." }
        $jsonStart = [array]::IndexOf($output, '{')
        if ($jsonStart -lt 0) { throw "EF returned no connection info for $context." }
        ($output[$jsonStart..($output.Count - 1)] -join "`n") | ConvertFrom-Json
    }
    if (-not $connections[0].databaseName -or $connections[0].databaseName -ne $connections[1].databaseName -or $connections[0].dataSource -ne $connections[1].dataSource) {
        throw 'MSAP EF tooling does not use the host database connection.'
    }
    Write-Output "PASS: both EF contexts use $($connections[0].databaseName) on $($connections[0].dataSource)."
}
finally {
    Pop-Location
}
