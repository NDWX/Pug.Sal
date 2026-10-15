#!/usr/bin/env pwsh
# The one entry point for building, testing, packing and releasing Pug.Sal. The GitHub workflows call
# nothing else, and the same commands work on a developer's machine (RELEASING.md).
#
#   build.ps1 -Stage identify  [-Ref refs/tags/sal-pre-v0.1.0]
#   build.ps1 -Stage test      [-DockerViaSg]
#   build.ps1 -Stage pack      [-Version 0.1.0]
#   build.ps1 -Stage checksums
#   build.ps1 -Stage push      [-DryRun]                      (NUGET_API_KEY in the environment)
#   build.ps1 -Stage release   -Tag sal-v0.1.0 -Version 0.1.0 [-Prerelease] [-DryRun]   (GH_TOKEN in the environment)
#
# Without -Version, pack builds <Version>-dev.<UTC stamp>. Packages land in <repo>/artifacts/dist
# unless -ArtifactsDir says otherwise; dist/ holds exactly what a release publishes.
#
# The release stage creates a draft GitHub release for -Tag holding exactly what is in dist/ (run
# checksums first so SHA256SUMS is among it), through the gh CLI. A draft that already exists is
# brought in line with dist/; a published release is never touched, because its assets are
# immutable. -DryRun prints what push or release would do and calls nothing.

[CmdletBinding()]
param(
	[Parameter( Mandatory )]
	[ValidateSet( 'identify', 'test', 'pack', 'checksums', 'push', 'release' )]
	[string] $Stage,

	# the git ref being built; identify reads the version from a release tag
	[string] $Ref = $env:GITHUB_REF,

	[string] $Version,

	# release stage: the tag to release, and whether it is a pre-release
	[string] $Tag,

	[switch] $Prerelease,

	# push and release stages: print the commands instead of running them
	[switch] $DryRun,

	[string] $ArtifactsDir,

	# run the Postgres tests through 'sg docker -c', for a shell started before joining the docker group
	[switch] $DockerViaSg
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:RepoRoot = ( Resolve-Path ( Join-Path $PSScriptRoot '..' ) ).Path
$script:Solution = Join-Path $script:RepoRoot 'Pug.Sal.slnx'
$script:Props = Join-Path $script:RepoRoot 'Directory.Build.props'

# sal-v1.2.3 is a release; sal-pre-v1.2.3 a pre-release
$script:TagPattern = '^sal(?<pre>-pre)?-v(?<version>\d+\.\d+\.\d+)$'

$artifacts = $ArtifactsDir ? [IO.Path]::GetFullPath( $ArtifactsDir ) : ( Join-Path $script:RepoRoot 'artifacts' )
$dist = Join-Path $artifacts 'dist'

# Runs a native command and throws on a non-zero exit code. -Capture returns stdout as one string.
# -Secret values are shown as *** wherever the command is echoed.
function Invoke-Native
{
	param( [Parameter( Mandatory )] [string] $FilePath, [string[]] $Arguments = @(), [switch] $Capture, [string[]] $Secret = @() )

	$shown = ( $Arguments | ForEach-Object { $Secret -contains $_ ? '***' : $_ } ) -join ' '

	if( $DryRun -and $Stage -in 'push', 'release' )
	{
		Write-Host "[dry run] $FilePath $shown"
		return $null
	}

	Write-Host "> $FilePath $shown"

	if( $Capture )
	{
		$output = & $FilePath @Arguments | Out-String
	}
	else
	{
		& $FilePath @Arguments | Out-Host
	}

	if( $LASTEXITCODE -ne 0 )
	{
		throw "$FilePath exited with code $LASTEXITCODE"
	}

	if( $Capture )
	{
		return $output.TrimEnd()
	}
}

# The <Version> every package shares (Directory.Build.props).
function Get-ProjectVersion
{
	[xml] $props = Get-Content -Raw $script:Props
	$version = @( $props.Project.PropertyGroup | ForEach-Object { $_.SelectSingleNode( 'Version' ) } | Where-Object { $_ } )

	if( $version.Count -ne 1 )
	{
		throw 'Directory.Build.props must declare exactly one <Version>'
	}

	return $version[0].InnerText.Trim()
}

# The pug-sal Claude Code plugin is versioned with the packages (DECISIONS D24): every <Version> bump
# bumps claude/pug-sal/.claude-plugin/plugin.json in the same commit.
function Assert-PluginVersion
{
	$manifest = Join-Path $script:RepoRoot 'claude/pug-sal/.claude-plugin/plugin.json'
	$pluginVersion = ( Get-Content -Raw $manifest | ConvertFrom-Json ).version
	$projectVersion = Get-ProjectVersion

	if( $pluginVersion -ne $projectVersion )
	{
		throw "plugin.json declares version $pluginVersion but Directory.Build.props declares $projectVersion; bump both together (RELEASING.md)"
	}
}

# UTC, 24-hour, so stamps sort in build order.
function Get-BuildStamp
{
	return [DateTime]::UtcNow.ToString( 'yyyyMMddHHmm', [Globalization.CultureInfo]::InvariantCulture )
}

# Stage outputs for the workflow (GITHUB_OUTPUT), echoed for a local run.
function Write-StageOutputs
{
	param( [Parameter( Mandatory )] [System.Collections.IDictionary] $Values )

	foreach( $entry in $Values.GetEnumerator() )
	{
		$value = $entry.Value -is [bool] ? $entry.Value.ToString().ToLowerInvariant() : "$( $entry.Value )"
		Write-Host "$( $entry.Key )=$value"

		if( $env:GITHUB_OUTPUT )
		{
			Add-Content -LiteralPath $env:GITHUB_OUTPUT -Value "$( $entry.Key )=$value"
		}
	}
}

# What a ref builds. A tag decides the version, which must match <Version>; any other ref (a branch,
# through workflow_dispatch, or a local run) builds <Version>-dev.<stamp> and releases nothing.
function Resolve-BuildIdentity
{
	param( [string] $Ref )

	$projectVersion = Get-ProjectVersion
	$tag = $Ref -like 'refs/tags/*' ? $Ref.Substring( 'refs/tags/'.Length ) : $null

	if( -not $tag )
	{
		return [ordered]@{
			version = "$projectVersion-dev.$( Get-BuildStamp )"; prerelease = $true; release = $false; tag = ''
		}
	}

	$match = [regex]::Match( $tag, $script:TagPattern )

	if( -not $match.Success )
	{
		throw "tag '$tag' is not sal-v<X.Y.Z> or sal-pre-v<X.Y.Z>"
	}

	$baseVersion = $match.Groups['version'].Value

	if( $baseVersion -ne $projectVersion )
	{
		throw "tag '$tag' names version $baseVersion but Directory.Build.props declares $projectVersion"
	}

	$prerelease = $match.Groups['pre'].Success

	return [ordered]@{
		version    = $prerelease ? "$baseVersion-pre.$( Get-BuildStamp )" : $baseVersion
		prerelease = $prerelease
		release    = $true
		tag        = $tag
	}
}

# A release (not pre-release) version may be on nuget.org already only from this same commit, as when
# a release is re-run (DECISIONS D26). The push skips duplicates, so a sal-v tag moved to another
# commit would otherwise "release" new binaries on GitHub while nuget.org keeps the old ones. Every
# project under src is packable, under its project name as package id, and its nuspec records the
# commit it was packed from.
function Assert-VersionUnpublished
{
	param( [Parameter( Mandatory )] [string] $Version )

	$commit = Invoke-Native -FilePath 'git' -Arguments @( '-C', $script:RepoRoot, 'rev-parse', 'HEAD' ) -Capture

	$conflicts = foreach( $project in Get-ChildItem -File -Path ( Join-Path $script:RepoRoot 'src' ) -Filter '*.csproj' -Recurse )
	{
		$id = $project.BaseName.ToLowerInvariant()
		$index = Invoke-WebRequest -Uri "https://api.nuget.org/v3-flatcontainer/$id/index.json" -SkipHttpErrorCheck

		if( $index.StatusCode -eq 404 )
		{
			continue
		}

		if( $index.StatusCode -ne 200 )
		{
			throw "nuget.org answered $( $index.StatusCode ) for $( $project.BaseName )"
		}

		if( ( $index.Content | ConvertFrom-Json ).versions -notcontains $Version )
		{
			continue
		}

		[xml] $nuspec = ( Invoke-WebRequest -Uri "https://api.nuget.org/v3-flatcontainer/$id/$Version/$id.nuspec" ).Content
		$published = $nuspec.package.metadata.repository.commit

		if( $published -ne $commit )
		{
			"$( $project.BaseName ) (from $( $published ? $published : 'an unknown commit' ))"
		}
	}

	if( $conflicts )
	{
		throw "version $Version is already on nuget.org from another commit: $( @( $conflicts ) -join ', ' ); bump <Version> instead of moving a release tag (RELEASING.md)"
	}
}

function Get-Packages
{
	return @( Get-ChildItem -File -Path $dist -Filter '*.nupkg' -ErrorAction SilentlyContinue | Sort-Object Name )
}

# Creates the draft release for -Tag from dist/, or replaces the assets of the draft a previous run
# left. A published release is an error: its assets cannot be replaced.
function Publish-GitHubRelease
{
	if( -not $Tag )
	{
		throw 'stage release needs -Tag'
	}

	if( $Tag -notmatch $script:TagPattern )
	{
		throw "'$Tag' is not sal-v<X.Y.Z> or sal-pre-v<X.Y.Z>"
	}

	$files = @( Get-ChildItem -File -Path $dist -ErrorAction SilentlyContinue | Sort-Object Name )

	if( -not $files )
	{
		throw "nothing to release: $dist is empty"
	}

	if( $files.Name -notcontains 'SHA256SUMS' )
	{
		throw "$dist has no SHA256SUMS; run -Stage checksums first"
	}

	if( -not $Version )
	{
		throw 'stage release needs -Version'
	}

	$title = "Pug.Sal $Version"
	$notes = "$title. The packages are published to nuget.org; verify these copies against SHA256SUMS."
	$paths = @( $files | ForEach-Object FullName )

	# the releases API lists drafts, which a lookup by tag does not reliably find; the tag is
	# validated above, so it is safe inside the jq program
	$listing = Invoke-Native -FilePath 'gh' -Capture -Arguments @(
		'api', '--paginate', 'repos/{owner}/{repo}/releases?per_page=100', '--jq',
		".[] | select( .tag_name == `"$Tag`" ) | { draft, assets: [ .assets[].name ] } | tojson" )

	$found = @( "$listing" -split "`n" | Where-Object { $_.Trim() } | ForEach-Object { $_ | ConvertFrom-Json } )

	if( $found | Where-Object { -not $_.draft } )
	{
		throw "release $Tag is already published and its assets are immutable; delete the release (not the tag) or release a new version"
	}

	if( -not $found )
	{
		$null = Invoke-Native -FilePath 'gh' -Arguments ( @(
			'release', 'create', $Tag, '--draft', '--verify-tag', '--title', $title, '--notes', $notes ) +
			( $Prerelease ? @( '--prerelease' ) : @() ) + $paths )

		return
	}

	$null = Invoke-Native -FilePath 'gh' -Arguments @(
		'release', 'edit', $Tag, '--title', $title, '--notes', $notes, "--prerelease=$( $Prerelease ? 'true' : 'false' )" )

	foreach( $asset in @( $found | ForEach-Object { $_.assets } | Where-Object { $_ -and $files.Name -notcontains $_ } ) )
	{
		$null = Invoke-Native -FilePath 'gh' -Arguments @( 'release', 'delete-asset', $Tag, $asset, '--yes' )
	}

	$null = Invoke-Native -FilePath 'gh' -Arguments ( @( 'release', 'upload', $Tag, '--clobber' ) + $paths )
}

switch( $Stage )
{
	'identify'
	{
		Assert-PluginVersion
		$identity = Resolve-BuildIdentity -Ref $Ref

		if( $identity.release -and -not $identity.prerelease )
		{
			Assert-VersionUnpublished -Version $identity.version
		}

		Write-StageOutputs $identity
	}

	# The completion gate (CLAUDE.md): the plugin and package versions agree, the solution builds
	# without warnings, and both test suites pass, the Postgres one against a Testcontainers
	# container (Docker required).
	'test'
	{
		Assert-PluginVersion
		Invoke-Native -FilePath 'dotnet' -Arguments @( 'build', $script:Solution, '-c', 'Release', '-warnaserror', '-m:1', '-nologo' )

		Invoke-Native -FilePath 'dotnet' -Arguments @(
			'test', ( Join-Path $script:RepoRoot 'tests/Pug.Sal.Tests' ), '-c', 'Release', '--no-build', '-m:1', '-nologo' )

		$postgres = @( 'test', ( Join-Path $script:RepoRoot 'tests/Pug.Sal.Postgres.Tests' ), '-c', 'Release', '--no-build', '-m:1', '-nologo' )

		if( $DockerViaSg )
		{
			Invoke-Native -FilePath 'sg' -Arguments @( 'docker', '-c', ( ( @( 'dotnet' ) + $postgres | ForEach-Object { "'$_'" } ) -join ' ' ) )
		}
		else
		{
			Invoke-Native -FilePath 'dotnet' -Arguments $postgres
		}
	}

	# Every packable project at one version, with symbol packages. Test projects are not packable.
	'pack'
	{
		if( -not $Version )
		{
			$Version = "$( Get-ProjectVersion )-dev.$( Get-BuildStamp )"
		}

		if( Test-Path $dist )
		{
			Remove-Item -Recurse -Force $dist
		}

		Invoke-Native -FilePath 'dotnet' -Arguments @(
			'pack', $script:Solution, '-c', 'Release', '-warnaserror', '-m:1', '-nologo', '-o', $dist,
			"-p:Version=$Version", '-p:IncludeSymbols=true', '-p:SymbolPackageFormat=snupkg', '-p:ContinuousIntegrationBuild=true' )

		$wrong = @( Get-Packages | Where-Object { -not $_.Name.EndsWith( ".$Version.nupkg" ) } )

		if( $wrong )
		{
			throw "packages not at version ${Version}: $( $wrong.Name -join ', ' )"
		}

		Write-Host "packed $( ( Get-Packages ).Count ) packages at $Version into $dist"
	}

	'checksums'
	{
		$sums = Get-ChildItem -File -Path $dist |
			Where-Object Name -ne 'SHA256SUMS' |
			Sort-Object Name |
			ForEach-Object { "$( ( Get-FileHash -Algorithm SHA256 -LiteralPath $_.FullName ).Hash.ToLowerInvariant() )  $( $_.Name )" }

		[IO.File]::WriteAllText( ( Join-Path $dist 'SHA256SUMS' ), ( $sums -join "`n" ) + "`n" )
	}

	# Pushes every package in dist/ to nuget.org; each .snupkg goes with its .nupkg. A version already
	# there is skipped, so a re-run after a partial push completes it.
	'push'
	{
		$packages = Get-Packages

		if( -not $packages )
		{
			throw "nothing to push: $dist has no packages"
		}

		if( -not $DryRun -and -not $env:NUGET_API_KEY )
		{
			throw 'stage push needs NUGET_API_KEY in the environment'
		}

		foreach( $package in $packages )
		{
			$key = $DryRun ? 'NUGET_API_KEY' : $env:NUGET_API_KEY

			Invoke-Native -FilePath 'dotnet' -Secret $key -Arguments @(
				'nuget', 'push', $package.FullName, '--api-key', $key,
				'--source', 'https://api.nuget.org/v3/index.json', '--skip-duplicate' )
		}
	}

	'release'
	{
		Publish-GitHubRelease
	}
}
