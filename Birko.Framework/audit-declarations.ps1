# audit-declarations.ps1 - does every shared project account for the external packages it uses?
#
# Sibling of audit-dependencies.ps1, which asks a different question (are any resolved packages
# vulnerable). This one asks whether a Birko.X shared project that `using`s an external library either
# DECLARES it or DOCUMENTS why it does not. Both are the rule from CLAUDE-maintenance.md
# section "External dependencies", settled in TASK-234:
#
#   - the project that WRAPS a library owns it, declared and floating within its major;
#   - a sibling built on that project records the pairing in a comment (declaring twice is NU1504);
#   - a project using the same library independently documents it as consumer-supplied;
#   - a FrameworkReference is never owned.
#
# A comment mentioning TASK-234 and naming the package counts as accounted for. That matters: without it
# this script reported 25 findings against 25 projects that were all following the rule, which is the
# failure mode this codebase keeps meeting - a checker that cannot tell compliance from the defect.
#
# TWO THINGS TO KNOW BEFORE TRUSTING A ZERO:
#
# 1. The map below is namespace-root -> package id, written out EXPLICITLY, and it is the limit of what
#    this script can see. A namespace with no entry is invisible: TASK-234's own count of 38 missed
#    Microsoft.AspNetCore.Authentication.JwtBearer for exactly that reason, and it was found by reading
#    usings by hand. When adding a dependency to the framework, add its namespace here too.
# 2. Do not shortcut the map by treating a namespace root as a package id. `Raven.*` ships in
#    RavenDB.Client and `NpgsqlTypes` in Npgsql, and guessing inflated the first survey from 38 to 43.
#
# Verify the check can fail before believing it: delete a TASK-234 comment from any satellite projitems
# and re-run - it must report exactly that project.

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

# namespace root => list of package ids that satisfy it (any one counts as declared)
$map = @{
    'Nest'                            = @('NEST')
    'Elasticsearch'                   = @('NEST', 'Elasticsearch.Net')
    'MongoDB'                         = @('MongoDB.Driver', 'MongoDB.Bson')
    'StackExchange'                   = @('StackExchange.Redis')
    'Microsoft.AspNetCore'            = @('Microsoft.AspNetCore.App')
    'Microsoft.Azure.Cosmos'          = @('Microsoft.Azure.Cosmos')
    'Raven'                           = @('RavenDB.Client')
    'Grpc'                            = @('Grpc.Net.Client', 'Grpc.AspNetCore', 'Grpc.Core.Api')
    'Microsoft.IdentityModel'         = @('Microsoft.IdentityModel.Tokens', 'Microsoft.IdentityModel.JsonWebTokens')
    'InfluxDB'                        = @('InfluxDB.Client')
    'Npgsql'                          = @('Npgsql')
    'NpgsqlTypes'                     = @('Npgsql')
    'MQTTnet'                         = @('MQTTnet')
    'System.IdentityModel.Tokens.Jwt' = @('System.IdentityModel.Tokens.Jwt')
    'Newtonsoft'                      = @('Newtonsoft.Json')
    'ProtoBuf'                        = @('protobuf-net')
    'YamlDotNet'                      = @('YamlDotNet')
    'Microsoft.Data.Sqlite'           = @('Microsoft.Data.Sqlite')
    'Microsoft.Data.SqlClient'        = @('Microsoft.Data.SqlClient')
    'MySql'                           = @('MySql.Data', 'MySqlConnector')
    'Amazon'                          = @('AWSSDK.Core', 'AWSSDK.S3')
    'Google'                          = @('Google.Cloud.Storage.V1', 'Google.Protobuf')
    'Minio'                           = @('Minio')
    'Azure'                           = @('Azure.Storage.Blobs', 'Azure.Messaging.ServiceBus')
    'Confluent'                       = @('Confluent.Kafka')
    'RabbitMQ'                        = @('RabbitMQ.Client')
    'Avalonia'                        = @('Avalonia')
    'OpenTelemetry'                   = @('OpenTelemetry')
    'MessagePack'                     = @('MessagePack')
    'SkiaSharp'                       = @('SkiaSharp')
}

$rows = @()

Get-ChildItem -Path $root -Directory -Filter 'Birko.*' | ForEach-Object {
    $dir = $_.FullName
    $projitems = Get-ChildItem -Path $dir -Filter '*.projitems' -File -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $projitems) { return }

    $declared = Get-Content $projitems.FullName -Raw

    $usings = @{}
    Get-ChildItem -Path $dir -Filter '*.cs' -Recurse -File -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '\\(obj|bin)\\' } |
        ForEach-Object {
            foreach ($line in [System.IO.File]::ReadAllLines($_.FullName)) {
                if ($line -match '^\s*(global\s+)?using\s+(static\s+)?([A-Za-z_][A-Za-z0-9_.]*)\s*;') {
                    $usings[$Matches[3]] = $true
                }
            }
        }

    foreach ($ns in $usings.Keys) {
        foreach ($key in $map.Keys) {
            if ($ns -eq $key -or $ns.StartsWith("$key.")) {
                $pkgs = $map[$key]
                $isDeclared = $false
                foreach ($p in $pkgs) {
                    # Include="Pkg" as PackageReference or FrameworkReference
                    if ($declared -match [regex]::Escape("Include=`"$p`"")) { $isDeclared = $true }
                    # ...or a TASK-234 comment naming the package: a satellite whose base declares it, or
                    # a documented consumer-supplied carve-out. Both are the rule being FOLLOWED, and a
                    # scan that cannot tell them from the defect reports 25 findings where there are none.
                    if ($declared -match 'TASK-234' -and $declared -match [regex]::Escape($p)) { $isDeclared = $true }
                }
                if (-not $isDeclared) {
                    $rows += [pscustomobject]@{
                        Project = $_.Name
                        Package = ($pkgs -join ' | ')
                        Via     = $ns
                    }
                }
                break
            }
        }
    }
}

$rows = $rows | Sort-Object Project, Package -Unique

Write-Host "=== Undeclared: $(($rows | Select-Object Project, Package -Unique).Count) (project,package) pairs across $(($rows.Project | Sort-Object -Unique).Count) projects"
$rows | Select-Object Project, Package -Unique | Group-Object Package |
    Sort-Object Count -Descending |
    ForEach-Object { "{0,-45} {1}" -f $_.Name, $_.Count }
Write-Host ""
Write-Host "=== Detail"
$rows | Select-Object Project, Package -Unique | Sort-Object Package, Project | ForEach-Object { "{0,-45} {1}" -f $_.Package, $_.Project }
