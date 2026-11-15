# Reads BrimDeck repository / installer MSBuild properties (fork overrides come from Directory.Build.props).
function Get-RepositoryBuildProperties {
    $project = Join-Path $PSScriptRoot '..\src\BrimDeck\BrimDeck.csproj'
    $json = & dotnet msbuild $project '-getProperty:BrimDeckRepositoryUrl;BrimDeckInstallerAppId;BrimDeckAppPublisher;BrimDeckDataDirSubfolder' -nologo -v:q
    if ($LASTEXITCODE -ne 0) { throw 'Could not evaluate repository build properties.' }
    $payload = $json | ConvertFrom-Json
    $values = $payload.Properties
    foreach ($name in @('BrimDeckRepositoryUrl', 'BrimDeckInstallerAppId', 'BrimDeckAppPublisher', 'BrimDeckDataDirSubfolder')) {
        if (!$values.$name) { throw "Missing MSBuild property $name." }
    }
    $repo = $values.BrimDeckRepositoryUrl
    [ordered]@{
        RepositoryUrl    = $repo
        InstallerAppId   = $values.BrimDeckInstallerAppId.Trim('{}')
        AppPublisher     = $values.BrimDeckAppPublisher
        DataDirSubfolder = $values.BrimDeckDataDirSubfolder
        AppPublisherURL  = $repo
        AppSupportURL    = "$repo/issues"
        AppUpdatesURL    = "$repo/releases"
        AppCopyright     = "Copyright (C) $($values.BrimDeckAppPublisher)"
    }
}